import {BaseEndpointsService, EndpointsContext} from './base-endpoints.service';
import {HttpClient} from '@angular/common/http';
import {customQuery} from './library/miscellaneous';
import {Inject, Injectable} from '@angular/core';
import {ENVIRONMENT_PROVIDER} from '../../../core/services/enviroment.provider';
import {IMovieListDetails} from '../../movies/components/movie-list-item/movie-list-item.component';
import {IActorShortProfile} from '../../actors/components/actor-short-profile/actor-short-profile.component';
import './library/dates/date-extend';
import './library/arrays/array-extend';

@Injectable({
    providedIn: 'root'
})
export class QueryService extends BaseEndpointsService {

    constructor(http: HttpClient, @Inject(ENVIRONMENT_PROVIDER) environment: any) {
        super(http, environment.backendServer);
    }

    private ctx = new EndpointsContext(this, {} as any);


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
    // asdaaasdaaaaaaaaaaaaaasdaaaaaaaaaaaaaa
    
    @customQuery
    public GetMovieListDetails() {
        return this.ctx.MovieApi.GetMovies()
            .map(movie => (
                {
                    picture: movie.moviePoster,
                    name: movie.title,
                    year: movie.releaseDate.getFullYear(),
                    rating: movie.rating,
                    description: movie.description,
                    director: movie.directedBy.name,
                    actors: movie.actors.map(actor => actor.name),
                    awards: movie.awards?.map(award => ({type: award.type, name: award.name}))
                } as IMovieListDetails)).toListAsync();
    }

    @customQuery
    public GetShortProfilesOfActors() {
        return this.ctx.MovieApi.GetActors()
            .map(actor => (
                {
                    id: actor.id,
                    picture: actor.picture,
                    name: actor.name,
                    numberOfMovies: actor.actedIn.length
                } as IActorShortProfile)).toListAsync();
    }

    @customQuery
    public GetListOfActorNames() {
        return this.GetShortProfilesOfActors().asQueryable().map(e => ({name: e.name, id: e.id})).toListAsync();
    }

    @customQuery
    public GetBiography(actorId: number) {
        return this.ctx.MovieApi.GetActors()
            .filter(actor => actor.id == actorId)
            .map(actor => ({
                biography: actor.biography,
                movies: actor.actedIn.map(movie => movie.title)
            })).findAsync();
    }

    // aaaaaaaaaaa
    @customQuery
    public GetBiography2(actorId: number) {
        return this.ctx.MovieApi.GetActors()
            .filter(actor => actor.id == actorId)
            .map(actor => ({
                biography: actor.biography,
                movies: actor.actedIn.map(movie => movie.title)
            })).findAsync();
    }

    @customQuery
    public GetShortProfileAndWorkStats(actorId: number) {
        return this.ctx.MovieApi.GetActors()
            .map(actor => ({
                id: actor.id,
                average: actor.actedIn.map(e => e.rating).sum() / actor.actedIn.length,
                colleagues: this.ctx.MovieApi.GetActors()
                    .filter(potentialColleague => potentialColleague.actedIn.some(e => actor.actedIn.map(e => e.id).includes(e.id)))
                    .map(e => ({name: e.name, id: e.id}))
                    .filter(e => e.id != actor.id).toList()
            }))
            .join(() => this.GetShortProfilesOfActors().asQueryable(), 'id', 'id', (stats, profile: IActorShortProfile) => ({
                ...profile,
                ...stats
            }))
            .findAsync(e => e.id == actorId);
    }
    
    
    @customQuery
    public MyCustomQuery() {
        return this.ctx.MovieApi.GetDirectors().filter(director => director.directed.length == 1).toListAsync();
    }
    
    
    @customQuery
    public AnotherCustomQuery() {
        return this.MyCustomQuery();
    }
    
    // aaaaaa
    @customQuery
    public AnotherOne(year: number) {
        return this.ctx.MovieApi.GetMoviesFromAYear(year, '').filter(movie => movie.awards.length && !movie.awards.length).toListAsync();
    }

}


