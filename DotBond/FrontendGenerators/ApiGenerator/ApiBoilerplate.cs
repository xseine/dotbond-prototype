﻿namespace DotBond.Generators;

public static class ApiBoilerplate
{
    // Note: don't change to expression body, or they won't collapse

    public static string GenerateBackendHttpEndpointsServiceContent(List<string> controllers)
    {
        return $$"""
//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated from a template.
//
//     Manual changes to this file may cause unexpected behavior in your application.
//     Manual changes to this file will be overwritten if the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

import { Observable } from "rxjs";
import { HttpClient } from "@angular/common/http";
import {
	MovieApiController,
	NewController,
	TranslateDemoController,
} from "./controller-definitions";
import {asQueryable, implementHttpCallsInController} from "./library/miscellaneous";
import { IQueryable } from "./library/queryable";
import { ExecutionInsights } from "./execution-rules";
/**
 * Class that provides methods for making http requests to backend API.
 * Properties, representing backend controllers, are objects that contain
 * methods, which are actions of that specific controller.
 * 
 */
export class BaseEndpointsService<TAnalytics extends boolean = true> {
	public readonly server: string;

	constructor(public http: HttpClient, server: string) {
		this.server = server;

		for (let controllerName in this) {
			if (controllerName === "http" || controllerName === "server")
				continue;

			implementHttpCallsInController(
				controllerName,
				this[controllerName],
				this.http,
				this.server,
				true
			);
		}
	}
    
    	protected ctx: EndpointsContext<TAnalytics> =
    		new EndpointsContext<TAnalytics>(this, {} as any);
    
    {{string.Join("\n\t", controllers.Select(c => $"public {c[..^"Controller".Length]} = new {c}();"))}}
}

type Constructor<T = Record<string, unknown>> = {
	// eslint-disable-next-line @typescript-eslint/no-explicit-any
	new (...args: any[]): T;
	prototype: T;
};

export function BaseEndpointsServiceConstructorFn<TAnalytics extends boolean = true>(
	showAnalytics: TAnalytics = true as TAnalytics
): Constructor<BaseEndpointsService<TAnalytics>> {
	return BaseEndpointsService<TAnalytics>;
}

export class EndpointsContext<TAnalytics extends boolean = true> {
	/**
	 *
	 * @param endpointsService
	 * @param currentCustomQueryName Name of the method that declares the query. (This is the name backend endpoint would use)
	 */
	constructor(
		protected endpointsService: BaseEndpointsService<TAnalytics>,
		currentCustomQueryName: { name: string }
	) {}

    {{string.Join("\n", controllers.Select(name => $"""
		public {name[..^"Controller".Length]} = createQueryableController<{name}, TAnalytics>(
			"{name[..^"Controller".Length]}",
			new {name}(),
			this.endpointsService
		);
	"""))}}
}

type TController = {{string.Join(" | ", controllers)}};

function createQueryableController<
	T extends TController & { [k in keyof T & string]: (...args: any) => any },
	TAnalytics extends boolean = true
>(
	controllerName: string,
	controller: T,
	endpointsService: BaseEndpointsService<TAnalytics>
): {
	[TAction in keyof T]: (
		...args: Parameters<T[TAction]>
	) => TAnalytics extends true
		? IQueryable<
				ReturnType<T[TAction]> extends Observable<infer U> ? U : never,
				TAction extends string ? ExecutionInsights[TAction] : never
		  >
		: IQueryable<
				ReturnType<T[TAction]> extends Observable<infer U> ? U : never
		  >;
} {
	implementHttpCallsInController(
		controllerName,
		controller,
		endpointsService.http,
		endpointsService.server,
		false
	);

	let actionNames = Object.getOwnPropertyNames(
		Object.getPrototypeOf(controller)
	).filter((name) => name !== "constructor");

	// @ts-ignore
	let result = {} as any;
	for (let action of actionNames) {
		// ovo se moze dolje ubaciti
		let overridenActionCall = controller[action] as (
			...args: any
		) => Observable<any>;
		result[action] = function () {
			return asQueryable(overridenActionCall(...arguments));
		};
	}

	return result;
}
""";
    }

    public static string GetQueryServiceContent(string serverAddress)
    {
        return $$"""
import {BaseEndpointsService, BaseEndpointsServiceConstructorFn} from "./base-endpoints.service";
import { HttpClient } from "@angular/common/http";
import { asQueryable, customQuery } from "./library/miscellaneous";
import { Inject, Injectable } from "@angular/core";
import { ENVIRONMENT_PROVIDER } from "../../../core/services/enviroment.provider";
import { IMovieListDetails } from "../../movies/components/movie-list-item/movie-list-item.component";
import { IActorShortProfile } from "../../actors/components/actor-short-profile/actor-short-profile.component";
import "./library/dates/date-extend";
import "./library/arrays/array-extend";

@Injectable({
    providedIn: 'root'
})
export class QueryService extends BaseEndpointsServiceConstructorFn(true) {

    constructor(http: HttpClient) {
        {{(serverAddress == null ? "throw 'Server address was not found in the proxy config. Add it, if needed, and then delete this line.'" : "// Server address is read from proxy configuration (it can be changed manually)")}}
        super(http, '{{serverAddress}}');
    }

    private ctx = new EndpointsContext(this, {} as any);

    
    /*========================== Custom Queries ==========================*/

	// Place them here:

}
""";
    }
}