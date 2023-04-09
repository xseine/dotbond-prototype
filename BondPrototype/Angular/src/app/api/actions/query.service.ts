import {
	BaseEndpointsService,
	BaseEndpointsServiceConstructorFn,
} from "./base-endpoints.service";
import { HttpClient } from "@angular/common/http";
import { asQueryable, customQuery } from "./library/miscellaneous";
import { Inject, Injectable } from "@angular/core";
import { ENVIRONMENT_PROVIDER } from "../../../core/services/enviroment.provider";
import { IMovieListDetails } from "../../movies/components/movie-list-item/movie-list-item.component";
import { IActorShortProfile } from "../../actors/components/actor-short-profile/actor-short-profile.component";
import "./library/dates/date-extend";
import "./library/arrays/array-extend";

@Injectable({
	providedIn: "root",
})
export class QueryService extends BaseEndpointsServiceConstructorFn(true) {
	constructor(
		http: HttpClient,
		@Inject(ENVIRONMENT_PROVIDER) environment: any
	) {
		super(http, environment.backendServer);
	}

	/*========================== Custom Queries ==========================*/

	// Example of a custom query
	// @customQuery
	// public SpreadQuery() {
	//     return this.ctx.MovieApi.GetMovies()
	//         .join(this.ctx.MovieApi.GetDirectors(), 'directedBy.id', 'id', (movie, director) => ({
	//             movieTitle: movie.title.toLowerCase(),
	//             directorName: director.name,
	//             ...director,
	//             ...movie
	//         })).filter(e => e.directed.every(movie => movie.rating >= 7) && e.name == "Denis Villeneuve".toLowerCase())
	//         .map(e => ({title: e.movieTitle + e.id + 23, directorName: e.directorName}))
	//         .findAsync();
	// }
	// asdaaasdaaaaaaaaaaaaaasdaaaaaaaaaaaaaa asd

	@customQuery
	public GetMovieListDetails() {
		return this.ctx.MovieApi.GetMovies()
			.map(
				(movie) =>
					({
						picture: movie.moviePoster,
						name: movie.title,
						year: movie.releaseDate.getFullYear(),
						rating: movie.rating,
						description: movie.description,
						director: movie.directedBy.name,
						actors: movie.actors.map((actor) => actor.name),
						awards: movie.awards?.map((award) => ({
							type: award.type,
							name: award.name,
						})),
					} as IMovieListDetails)
			)
			.toListAsync();
	}

	@customQuery
	public GetShortProfilesOfActors() {
		return this.ctx.MovieApi.GetActors()
			.map(
				(actor) =>
					({
						id: actor.id,
						picture: actor.picture,
						name: actor.name,
						numberOfMovies: actor.actedIn.length,
					} as IActorShortProfile)
			)
			.toListAsync();
	}

	@customQuery
	public GetListOfActorNames() {
		return asQueryable(this.GetShortProfilesOfActors())
			.map((e) => ({ name: e.name, id: e.id }))
			.toListAsync();
	}

	@customQuery
	public GetBiography(actorId: number) {
		return this.ctx.MovieApi.GetActors()
			.filter((actor) => actor.id == actorId)
			.map((actor) => ({
				biography: actor.biography,
				movies: actor.actedIn.map((movie) => movie.title),
			}))
			.findAsync();
	}

	// aaaaaaaaaaaaa
	@customQuery
	public GetBiography2(actorId: number) {
		return this.ctx.MovieApi.GetActors()
			.filter((actor) => actor.id == actorId)
			.map((actor) => ({
				biography: actor.biography,
				movies: actor.actedIn.map((movie) => movie.title),
			}))
			.findAsync();
	}

	@customQuery
	public GetShortProfileAndWorkStats(actorId: number) {
		return this.ctx.MovieApi.GetActors()
			.map((actor) => ({
				id: actor.id,
				average:
					actor.actedIn.map((e) => e.rating).sum() /
					actor.actedIn.length,
				colleagues: this.ctx.MovieApi.GetActors()
					.filter((potentialColleague) =>
						potentialColleague.actedIn.some((e) =>
							actor.actedIn.map((e) => e.id).includes(e.id)
						)
					)
					.map((e) => ({ name: e.name, id: e.id }))
					.filter((e) => e.id != actor.id)
					.toList(),
			}))
			.join(
				() => asQueryable(this.GetShortProfilesOfActors()),
				"id",
				"id",
				(stats, profile: IActorShortProfile) => ({
					...profile,
					...stats,
				})
			)
			.findAsync((e) => e.id == actorId);
	}

	@customQuery
	public TestExecutionRules() {
		return this.ctx.MovieApi.GetMovies()
			.join(
				() => this.ctx.MovieApi.GetMovies(),
				"id",
				"id",
				(movie1, movie2) => ({ movie1, movie2 })
			)
			.toListAsync();
	}

	@customQuery
	public MyCustomQuery() {
		return this.ctx.MovieApi.GetDirectors()
			.filter((director) => director.directed.length == 1)
			.toListAsync();
	}

	@customQuery
	public AnotherCustomQuery() {
		return this.MyCustomQuery();
	}

	// aaaaaaa
	@customQuery
	public AnotherOne(year: number) {
		return this.ctx.MovieApi.GetMoviesFromAYear(year, "")
			.filter((movie) => movie.awards.length && !movie.awards.length)
			.toListAsync();
	}

	@customQuery
	public MyQuery() {
		return this.ctx.New.Test()
			.map((e) => ({ value: e + this.ctx.New.TestTwo().find() }))
			.toListAsync();
	}

	@customQuery
	public MyQuery2() {
		return this.ctx.New.Test()
			.map((e) => ({ value: e + " a " + this.ctx.New.TestTwo().find() }))
			.toListAsync();
	}

	@customQuery
	public MyQuery3() {
		return this.ctx.New.Test()
			.map((e) => ({ value: e + " a " + this.ctx.New.TestTwo().find() }))
			.toListAsync();
	}

	// as
	@customQuery
	public MyQuery4() {
		return this.ctx.New.TestThree()
			.map((e) => ({ valueOne: e + "24", valueTwo: this.MyQuery3() }))
			.findAsync();
	}

	@customQuery
	public MyQuery5() {
		return this.ctx.New.TestFour()
			.map((e) => ({ valueOne: e + "24", valueTwo: this.MyQuery3() }))
			.findAsync();
	}
}
