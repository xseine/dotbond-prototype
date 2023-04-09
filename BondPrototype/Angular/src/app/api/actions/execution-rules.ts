import { Increment, IntRange } from "./library/utilities";

export type ExecutionInsights = {
	GetMovies: GetMovies;
	GetActors: GetActors;
	GetDirectors: GetDirectors;
	GetAwards: GetAwards;
	GetMoviesFromAYear: GetMoviesFromAYear;
} & {
	[key in Exclude<
		string,
		| "GetMovies"
		| "GetActors"
		| "GetDirectors"
		| "GetAwards"
		| "GetMoviesFromAYear"
	>]: void;
};

export interface GetMovies<Depth extends number = 0> {
	0: -1 extends Depth ? true : false;
	1: Depth;
}

export interface GetActors<Depth extends number = 0> {
	0: -1 extends Depth ? true : false;
	2: Depth;
}

export interface GetDirectors<Depth extends number = 0> {
	0: -1 extends Depth ? true : false;
	3: Depth;
}

export interface GetAwards<Depth extends number = 0> {
	0: -1 extends Depth ? true : false;
	4: Depth;
}

export interface GetMoviesFromAYear<Depth extends number = 0> {
	0: -1 extends Depth ? true : false;
	5: Depth;
}

export type SuperInterface<Depth extends number = 0> = {
	[key in IntRange<1, 20>]?: Depth;
} & {
	0: -1 extends Depth ? true : false;
};

type GetQuery<Query, Depth extends number> = Query extends GetMovies<any>
	? GetMovies<Depth>
	: Query extends GetActors<any>
	? GetActors<Depth>
	: Query extends GetDirectors<any>
	? GetDirectors<Depth>
	: Query extends GetAwards<any>
	? GetAwards<Depth>
	: Query extends GetMoviesFromAYear<any>
	? GetMoviesFromAYear<Depth>
	: never;

type GetQueryName<Query> = Query extends GetMovies<any>
	? "GetMovies"
	: Query extends GetActors<any>
	? "GetActors"
	: Query extends GetDirectors<any>
	? "GetDirectors"
	: Query extends GetAwards<any>
	? "GetAwards"
	: Query extends GetMoviesFromAYear<any>
	? "GetMoviesFromAYear"
	: never;

export type ClientSide = { _ };
export type CustomQuery = { $ };

type RunOnClientWhen = {
	GetMovies: [GetActors, GetDirectors, GetAwards];
	GetActors: [GetMovies, GetMoviesFromAYear];
	GetDirectors: [GetMovies, GetMoviesFromAYear];
	GetAwards: [GetMovies, GetMoviesFromAYear];
	GetMoviesFromAYear: [GetActors, GetDirectors, GetAwards];
};

export type CombineQuery<TFirst, TSecondary = 0> = TSecondary extends
	| ClientSide
	| RunOnClientWhen[GetQueryName<TFirst>][number]
	? ClientSide
	: TSecondary extends SuperInterface<infer Depth>
	? GetQuery<TSecondary, Increment<Depth>>
	: TSecondary extends CustomQuery
	? GetQuery<TFirst, 1>
	: TFirst extends CustomQuery
	? GetQuery<TSecondary, 1>
	: never;
