namespace DotBond.Generators;

public static class ApiBoilerplate
{
    // Note: don't change to expression body, or they won't collapse

    public static string GenerateBackendHttpEndpointsServiceContent(List<string> controllers)
    {
        return $@"
//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated from a template.
//
//     Manual changes to this file may cause unexpected behavior in your application.
//     Manual changes to this file will be overwritten if the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

import {{Observable}} from 'rxjs';
import {{HttpClient}} from '@angular/common/http';
import {{{string.Join(", ", controllers)}}} from ""./controller-definitions"";
import {{implementHttpCallsInController}} from './library/miscellaneous';
import {{IQueryable}} from './library/queryable';
/**
 * Class that provides methods for making http requests to backend API.
 * Properties, representing backend controllers, are objects that contain
 * methods, which are actions of that specific controller.
 * 
 */
export class BaseEndpointsService {{
    
    public readonly server: string;

    constructor(public http: HttpClient, server: string) {{
       
        this.server = server;
 
        for (let controllerName in this) {{
            if (controllerName === 'http' || controllerName === 'server') continue;

            implementHttpCallsInController(controllerName, this[controllerName], this.http, this.server, true);
        }}

    }}
    
    {string.Join("\n\t", controllers.Select(c => $"public {c[..^"Controller".Length]} = new {c}();"))}
}}


export class EndpointsContext {{

    /**
     *
     * @param endpointsService
     * @param currentCustomQueryName Name of the method that declares the query. (This is the name backend endpoint would use)
     */
    constructor(protected endpointsService: BaseEndpointsService, currentCustomQueryName: {{name: string}}) {{
    }}

    {string.Join("\n\t", controllers.Select(c => $"public {c[..^"Controller".Length]} = createQueryableController('{c[..^"Controller".Length]}', new {c}(), this.endpointsService);"))}
}}

// @ts-ignore
function createQueryableController<TController>(controllerName: string, controller: TController, endpointsService: BaseEndpointsService): {{ [TAction in keyof TController]: (...args: Parameters<TController[TAction]>) => IQueryable<ReturnType<TController[TAction]> extends Observable<infer U> ? U : never> }} {{
    implementHttpCallsInController(controllerName, controller, endpointsService.http, endpointsService.server, false);

    let actionNames = Object.getOwnPropertyNames(Object.getPrototypeOf(controller)).filter(name => name !== 'constructor');

    // @ts-ignore
    let result = {{}} as any;
    for (let action of actionNames) {{
        // ovo se moze dolje ubaciti
        let overridenActionCall = controller[action] as (...args: any) => Observable<any>;
        result[action] = function () {{
            return overridenActionCall(...arguments).asQueryable();
        }};
    }}

    return result;
}}";
    }

    public static string GetQueryServiceContent(string serverAddress)
    {
        return $@"import {{BaseEndpointsService, EndpointsContext}} from './base-endpoints.service';
import {{HttpClient}} from '@angular/common/http';
import {{customQuery}} from './library/miscellaneous';
import {{Injectable}} from '@angular/core';
import './library/dates/date-extend';
import './library/arrays/array-extend';

@Injectable({{
    providedIn: 'root'
}})
export class QueryService extends BaseEndpointsService {{

    constructor(http: HttpClient) {{
        {(serverAddress == null ? "throw 'Server address was not found in the proxy config. Add it, if needed, and then delete this line.'" : "// Server address is read from proxy configuration (it can be changed manually)")}
        super(http, '{serverAddress}');
    }}

    private ctx = new EndpointsContext(this, {{}} as any);

    
    /*========================== Custom Queries ==========================*/

    // Example of a custom query
    // @customQuery
    // public SpreadQuery() {{
    //     return this.ctx.MovieApi.GetMovies()
    //         .join(this.ctx.MovieApi.GetDirectors(), 'directedBy.id', 'id', (movie, director) => ({{
    //             movieTitle: movie.title.toLowerCase(),
    //             directorName: director.name,
    //             ...director,
    //             ...movie
    //         }})).filter(e => e.directed.every(movie => movie.rating >= 7) && e.name == ""Denis Villeneuve"".toLowerCase())
    //         .map(e => ({{title: e.movieTitle + e.id + 23, directorName: e.directorName}}))
    //         .findAsync();
    // }}
}}


";
    }
}