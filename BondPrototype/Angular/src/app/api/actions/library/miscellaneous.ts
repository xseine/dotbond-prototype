import {combineLatest, from, map, Observable, of, switchMap, tap} from 'rxjs';
import {HttpClient} from '@angular/common/http';
import {BaseEndpointsService} from '../base-endpoints.service';
import {IQueryable, Queryable} from './queryable';
import {QueryService} from '../query.service';
import {dateFieldsInReturnTypes} from '../return-type-dates';
import {QueryServiceLock} from '../query.service.lock';
import 'reflect-metadata';
// async import
import('./dates/date-format');

/*========================== Endpoint builder functionality ==========================*/

let fromBodyMetadataKey = Symbol('fromBody');
let fromUriMetadataKey = Symbol('fromUri');

export function fromBody(target: Object, propertyKey: string | symbol, parameterIndex: number) {
    Reflect.defineMetadata(fromBodyMetadataKey, parameterIndex, target, propertyKey);
}

export function fromUri(target: Object, propertyKey: string | symbol, parameterIndex: number) {
    let existingFromUriParameters: number[] = Reflect.getOwnMetadata(fromUriMetadataKey, target, propertyKey) || [];
    existingFromUriParameters.push(parameterIndex);
    Reflect.defineMetadata(fromUriMetadataKey, existingFromUriParameters, target, propertyKey);
}

/**
 * Overrides action methods so they return their name and the names of their parameters (important for binding).
 * Second override of action method uses this data to implement the http call.
 * @param methodName
 * @param usesSimpleRoute If the route template is [controller], don't use action name in route path
 */
export function method(methodName: 'GET' | 'POST', usesSimpleRoute = false) {
    return function (target: any, propertyName: string, descriptor: TypedPropertyDescriptor<(...args: any) => Observable<any>>) {
        let argumentNames = descriptor.value() as any as string[];

        let fromBodyParameter = argumentNames[Reflect.getOwnMetadata(fromBodyMetadataKey, target, propertyName) as number];
        let fromUriParameters = (Reflect.getOwnMetadata(fromUriMetadataKey, target, propertyName) as number[])?.map(e => argumentNames[e]);

        // Override the action method to return the method type and method parameters
        descriptor.value = function () {
            return [methodName, argumentNames, usesSimpleRoute, fromBodyParameter, fromUriParameters] as any;
        }
    }
}

/*========================== Queryable ==========================*/

let lockedQueryService = new QueryServiceLock();
/**
 * Array of custom queries to be sent alongside the base endpoint request.
 * If a query is calling another query immediately, both of these queries must be sent for server evaluation,
 * and depending on what is active, all, one or none of the queries will be executed client-side.
 */
export let stackOfCustomQueries: { queryName: string; args: any, isUnmodified: boolean }[] = [];
let isStackEmpty = true;

// Determines query's, and its subqueries', cache and config context
export let queryInstanceId: { 'id': number } = {id: null};
let queryInstanceIdSequence = 1;

let requestCacheInCustomQuery: { [instanceId: number]: { [baseUrl: string]: { [queries: string]: { argumentObject: any, resultPromise: Promise<any>, resultResolve: any }[] } } } = {};
let checkInCache = (instanceId: number, baseUrl: string, queries: string, argumentObject: any) => requestCacheInCustomQuery[instanceId]?.[baseUrl]?.[queries]?.find(e => JSON.stringify(e.argumentObject) === JSON.stringify(argumentObject))?.resultPromise;

// Internal api config for benchmarking client-side performance
let isQueryForcedClientSide: { [instanceId: number]: boolean } = {};


/**
 * Wrapper around method body which:
 * - when a query calls another query, it stacks them, so they are sent to the backend together
 * - creates a results cache while the top level query is active
 * - records whether a query, and its subqueries, should be executed client-side always (forced)
 */
export function customQuery(target: any, propertyName: string, descriptor: TypedPropertyDescriptor<(...args: any) => any>) {

    let method = descriptor.value!.toString();
    let accessedProperties = [...method.matchAll(/.find\(\w*\)\s*\??\.\s*(?<property>\w+)/g)].map(e => (e.groups as any)?.property);
    accessedProperties.filter(prop => !!prop).forEach(prop => Queryable.accessedPropertiesAfterFind.add(prop));

    let originalMethod = descriptor.value!;

    descriptor.value = function () {
        let argumentValues = [...arguments];
        stackOfCustomQueries.push({
            queryName: propertyName,
            args: argumentValues.reduce((acc, curr, idx) => ({...acc, [`${propertyName}-param${idx}`]: curr}), {}), // argumentNames.map(argName => `${propertyName}-${argName}`).reduce((acc, curr, idx) => ({...acc, [curr]: argumentValues[idx]}), {}),
            isUnmodified: lockedQueryService[propertyName] && lockedQueryService[propertyName]() === method
        });

        // Caches are on top level
        let isTopLevelQuery = queryInstanceId.id == null;
        if (isTopLevelQuery) {
            queryInstanceId.id = queryInstanceIdSequence++;
            isQueryForcedClientSide[queryInstanceId.id] = (descriptor.value as any).forceClientSide === true;
        }

        // Query stacks follows function stack, i.e. gets de-referenced once pipe is built
        let clearStackWhenDone = isStackEmpty;
        if (isStackEmpty) isStackEmpty = false;

        // Check if client side is forced through internal API
        delete (descriptor.value as any).forceClientSide;

        // Proceed with the query body
        let result = originalMethod.bind(this)(...arguments);
        if (isTopLevelQuery) {
            let capturedQueryInstanceId = queryInstanceId.id;
            result = result.pipe(tap(_ => {
                delete requestCacheInCustomQuery[capturedQueryInstanceId];
                delete isQueryForcedClientSide[capturedQueryInstanceId];
            }));
        }

        queryInstanceId.id = null;

        if (clearStackWhenDone) {
            isStackEmpty = true;
            latestResponseFirstActiveQuery = new String();
            stackOfCustomQueries.length = 0;
        }

        return result;
    };

    (descriptor.value as any).originalMethod = method;
}


export type CustomQueryResponse = { firstActiveQuery: string | null, body: any };
export let latestResponseFirstActiveQuery: String | null = new String();

/**
 * Overrides action methods to make an http call when invoked.
 * Action method and parameters are introspected from the ts decorator that is added on the action definition.
 * @param controllerName
 * @param controller
 * @param http
 * @param serverAddress
 * @param isBaseController
 */
export function implementHttpCallsInController(controllerName: string, controller: any, http: HttpClient, serverAddress: string, isBaseController: boolean): void {

    // Scenario of 
    if (!serverAddress) return;

    let actions = Object.getOwnPropertyNames(Object.getPrototypeOf(controller)).filter(name => name !== 'constructor');

    for (let actionName of actions) {
        if (actionName === 'constructor') continue;

        // Provided by the controller definition @method attribute
        let [method, argumentNames, usesSimpleRoute, fromBodyParameter, fromUriParameters] = controller[actionName]();

        // Fallback url is used to tell server which data to fetch in case the composed query doesn't have an active endpoint.
        let baseUrl = `${serverAddress}/${controllerName}` + (!usesSimpleRoute ? `/${actionName}` : '');

        // Override the action method to make the http request
        controller[actionName] = function () {
            let argumentObject: any = {};
            [...arguments].forEach((value, idx) => argumentObject[argumentNames[idx]] = value);

            let queryUrls: string[] = [];

            for (let {queryName, args, isUnmodified} of stackOfCustomQueries)
                if (isUnmodified) {
                    argumentObject = {...argumentObject, ...args};
                    queryUrls.push(queryName)
                }

            let body: any;
            if (method === 'POST')
                [argumentObject, body] = configurePostRequest(fromUriParameters, fromBodyParameter, argumentObject);

            let latestResponseFirstActiveQueryLocal = latestResponseFirstActiveQuery;

            let cacheHit;
            let resultResolve;
            let isCustomQuery = !!queryInstanceId.id;

            if (isCustomQuery) {
                cacheHit = checkInCache(queryInstanceId.id, baseUrl, queryUrls.join(','), argumentObject);
                if (!cacheHit) {
                    let resultPromise = new Promise(resolve => resultResolve = resolve);
                    if (!requestCacheInCustomQuery[queryInstanceId.id]) requestCacheInCustomQuery[queryInstanceId.id] = {};
                    if (!requestCacheInCustomQuery[queryInstanceId.id][baseUrl]) requestCacheInCustomQuery[queryInstanceId.id][baseUrl] = {};
                    if (!requestCacheInCustomQuery[queryInstanceId.id][baseUrl][queryUrls.join(',')]) requestCacheInCustomQuery[queryInstanceId.id][baseUrl][queryUrls.join(',')] = [];

                    requestCacheInCustomQuery[queryInstanceId.id][baseUrl][queryUrls.join(',')].push({
                        argumentObject,
                        resultPromise: resultPromise,
                        resultResolve: resultResolve
                    });
                }
            }


            if (isQueryForcedClientSide[queryInstanceId.id])
                queryUrls.length = 0;

            // let capturedForceClientSide = isQueryForcedClientSide;
            let requestObs = cacheHit ? from(cacheHit) as Observable<any> : executeRequest(http, baseUrl, method, argumentObject, queryUrls.length ? {'bond-queries': queryUrls} : undefined, body);

            return requestObs.pipe(
                tap(e => {
                    if (queryUrls.length) {
                        latestResponseFirstActiveQueryLocal!.valueOf = function () {
                            return e.firstActiveQuery ?? '';
                        }

                        if (!e.firstActiveQuery)
                            stackOfCustomQueries = [];
                    }

                    if (!cacheHit && isCustomQuery)
                        resultResolve(e);
                }));
        }
    }
}

/**
 * Single point of making http calls to the backend.
 */
function executeRequest<THeaders extends { [p: string]: string | string[] }>(http: HttpClient, url: string, method: 'GET' | 'POST', parameters?: any, headers?: THeaders, body?: any)
    : Observable<THeaders extends { 'bond-queries': any } ? CustomQueryResponse : any> {

    let [controller, action] = url.split('/').slice(url.split('/').length - 2);
    let baseTypeDates = dateFieldsInReturnTypes[controller]?.[action];
    let bondTypeDates: string[] | null = null;

    if (!headers) headers = {} as any;
    (headers as any)['Accept'] = 'application/json';
    (headers as any)['Content-Type'] = 'application/json';

    if (headers?.['bond-queries']) {
        let bondUrls = headers['bond-queries'] as string[];
        bondTypeDates = bondUrls.map(url => url.split('/').slice(1)).flatMap(([controller, action]) => dateFieldsInReturnTypes[controller]?.[action]);
    }

    // Remove nulls query parameters
    parameters = Object.fromEntries(Object.entries(parameters).filter(([_, v]) => v != null));
    let request = method === 'GET' ? http.get(url, {params: parameters, headers, observe: 'response'}) : http.post(url, body, {params: parameters, headers, observe: 'response'})

    return request.pipe(map(response => {

        let body = response.body;
        let firstActiveQuery = response.headers.get('first-active-query');

        if (headers?.['bond-queries']) {
            // TODO: handle data fields

            return {firstActiveQuery, body} as CustomQueryResponse;
        } else {
            if (baseTypeDates)
                body = Array.isArray(body) ? body.map(e => convertDateFields(e, baseTypeDates)) : convertDateFields(body, baseTypeDates);
            return body;
        }

    })) as any;
}

declare module 'rxjs/internal/Observable' {
    interface Observable<T> {
        asQueryable<T>(this: Observable<T>): IQueryable<T>;
    }
}

Observable.prototype.asQueryable = function () {

    let latestResponseFirstActiveQueryLocal = latestResponseFirstActiveQuery;
    let stackOfCustomQueriesLocal = [...stackOfCustomQueries];

    return new Queryable(this.pipe(map(result => {
        let currentQueryName = stackOfCustomQueriesLocal.pop()?.queryName;
        let hasActiveEndpoint = latestResponseFirstActiveQueryLocal != '';
        if (currentQueryName == latestResponseFirstActiveQueryLocal) {
            latestResponseFirstActiveQueryLocal.valueOf = function () {
                return '';
            };
            stackOfCustomQueriesLocal.length = 0;
        }

        result = (result as any).firstActiveQuery !== undefined && (result as any).body ? (result as any).body : result;

        return ({result, shouldUseClientSideProcessing: !hasActiveEndpoint});
    }))) as any;
}

/**
 * Finds fields in the object that are Observables
 * and pipes them to replace the original field's value with its (Observable's) output value.
 *
 * Example:
 * object:
 * {
 *     country: Observable<Country>
 * }
 *
 * .pipe(tap(value => object.country = value))
 *
 */
export function evaluateInnerObservables(object: any): Observable<any> {

    let findObservablesInChildren = (parent: any) => {

        let childrenObservables: { entry: any, index: any, observable: Observable<any> }[] = [];

        for (let entry of Object.entries(parent)) {
            if ((entry[1] as any)?.constructor.name === 'Observable')
                childrenObservables.push({entry: parent, index: entry[0], observable: entry[1] as any});
            else if ((entry[1] as any)?.constructor.name === 'Object')
                childrenObservables = [...childrenObservables, ...findObservablesInChildren(entry[1])];
        }

        return childrenObservables;
    }

    let entriesWithObservables = findObservablesInChildren(object);

    if (entriesWithObservables.length === 0) return of(object);

    let observeForInnerObservables = (observable: Observable<any>) => observable.pipe(
        switchMap(value => {
            if (typeof value !== 'object') return of(value);

            let entriesWithObservables = findObservablesInChildren(value);
            return entriesWithObservables.length === 0 ? of(value) : combineLatest(...entriesWithObservables.map(e => observeForInnerObservables(e.observable).pipe(tap(value => e.entry[e.index] = value)))).pipe(map(_ => value));
        }))

    return combineLatest(...entriesWithObservables.map(e => observeForInnerObservables(e.observable).pipe(tap(value => e.entry[e.index] = value)))).pipe(map(_ => object));
}

type Increment<N extends number> = [
    1, 2, 3, 4, 5, // as far as you need
    ...number[] // bail out with number
][N];

type PrimitiveType = number | string | boolean | Date;
type PathImpl<T, Key extends keyof T, TDepth extends number> = Key extends string
    ? T[Key] extends PrimitiveType ? Key                                                                                       // FormControl nodes represent custom controls, whose TValue properties cannot be accessed from ControlTree
        : T[Key] extends (infer U)[] ? U extends PrimitiveType ? `${Key}[%d]` : TDepth extends 5 ? never : `${Key}[%d].${PathImpl<U, keyof U, Increment<TDepth>>}`            // If not a primitive type, use a template string, where arrays have [$d] attached to the end.
            : T[Key] extends object ? TDepth extends 5 ? never : `${Key}.${PathImpl<T[Key], Exclude<keyof T[Key], keyof any[]>, Increment<TDepth>>}` : never                                                           // Parent.Child.<etc>
    : never;
/**
 * Type safe property access of typescript objects.
 */
export type PrimitiveKey<T> = 0 extends (1 & T) ? string : T extends PrimitiveType ? never            // Any or primitive
    : T extends (infer U)[] ? U extends PrimitiveType ? '[%d]' : `[%d].${PathImpl<U, keyof U, 0>}`       // Array
        : PathImpl<T, keyof T, 0>;                                                                       // Object
// Note 1: '0 extends (1 & T)' check for any type.


export function getParams(func): string[] {
    let STRIP_COMMENTS = /((\/\/.*$)|(\/\*[\s\S]*?\*\/))/mg;
    let ARGUMENT_NAMES = /([^\s,]+)/g;

    let fnStr = func.toString().replace(STRIP_COMMENTS, '');
    let result = fnStr.slice(fnStr.indexOf('(') + 1, fnStr.indexOf(')')).match(ARGUMENT_NAMES);
    if (result === null)
        result = [];
    return result;
}

function configurePostRequest(fromUriParameters, fromBodyParameter, argumentObject: any) {
    fromUriParameters = fromUriParameters ?? [];
    let body;

    if (fromBodyParameter) {
        if (argumentObject[fromBodyParameter])
            body = JSON.stringify(argumentObject[fromBodyParameter]);
        delete argumentObject[fromBodyParameter];
    } else {
        let complexNonUriEntry = Object.entries(argumentObject)
            .find(([k, v]) => !fromUriParameters.includes(k) && typeof v === 'object');
        if (complexNonUriEntry[1])
            body = JSON.stringify(complexNonUriEntry[1]);
    }

    argumentObject = Object.fromEntries(Object.entries(argumentObject).filter(([k, v]) => typeof v !== 'object' || fromUriParameters.includes(k)));
    return [argumentObject, body];
}

function convertDateFields(object: any, dateFields: string[]) {
    for (let key in object)
        if (dateFields.includes(key))
            object[key] = new Date(object[key]);

    return object;
}

export function accessUsingPath(object: any, path: string): any {
    path = path.replace(/\[(\w+)]/g, '.$1'); // convert indexes to properties
    return path.split('.').reduce((acc, curr) => acc && acc[curr] || null, object)
}

export function sortAscCmp(a: any, b: any) {
    return a > b ? 1 : a == b ? 0 : -1;
}

export function sortDescCmp(a: any, b: any) {
    return a > b ? -1 : a == b ? 0 : 1;
}

export type ApiControllers = {
    [TController in keyof Omit<BaseEndpointsService, 'executeRequest' | 'http'>]: {
        [TAction in keyof BaseEndpointsService[TController]]: BaseEndpointsService[TController][TAction]
    }
} & { Extended: { [TAction in keyof Omit<QueryService, keyof BaseEndpointsService>]: QueryService[TAction] } };
