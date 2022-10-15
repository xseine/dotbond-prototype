import {BaseEndpointsService, EndpointsContext} from './base-endpoints.service';
import {HttpClient} from '@angular/common/http';
import {customQuery} from './library/miscellaneous';
import {Inject, Injectable} from '@angular/core';
import {ENVIRONMENT_PROVIDER} from '../../../core/services/enviroment.provider';
import {IMovieListDetails} from '../../movies/components/movie-list-item/movie-list-item.component';
import {IActorShortProfile} from '../../actors/components/actor-short-profile/actor-short-profile.component';
import './library/dates/date-extend';
import './library/arrays/array-extend';


export class QueryServiceLock {

    private ctx: any;


    
    @lockedQuery
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

    @lockedQuery
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

    @lockedQuery
    public GetListOfActorNames() {
        return this.GetShortProfilesOfActors().asQueryable().map(e => ({name: e.name, id: e.id})).toListAsync();
    }

    @lockedQuery
    public GetBiography(actorId: number) {
        return this.ctx.MovieApi.GetActors()
            .filter(actor => actor.id == actorId)
            .map(actor => ({
                biography: actor.biography,
                movies: actor.actedIn.map(movie => movie.title)
            })).findAsync();
    }

    // aaaaaaaaaaa
    @lockedQuery
    public GetBiography2(actorId: number) {
        return this.ctx.MovieApi.GetActors()
            .filter(actor => actor.id == actorId)
            .map(actor => ({
                biography: actor.biography,
                movies: actor.actedIn.map(movie => movie.title)
            })).findAsync();
    }

    @lockedQuery
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
    
    
    @lockedQuery
    public MyCustomQuery() {
        return this.ctx.MovieApi.GetDirectors().filter(director => director.directed.length == 1).toListAsync();
    }
    
    
    @lockedQuery
    public AnotherCustomQuery() {
        return this.MyCustomQuery();
    }
    
    // aaaaaa
    @lockedQuery
    public AnotherOne(year: number) {
        return this.ctx.MovieApi.GetMoviesFromAYear(year, '').filter(movie => movie.awards.length && !movie.awards.length).toListAsync();
    }

    @lockedQuery
    public MyQuery() {
        return this.ctx.New.Test().map(e => ({value: e + this.ctx.New.TestTwo().find()})).toListAsync();
    }

    @lockedQuery
    public MyQuery2() {
        return this.ctx.New.Test().map(e => ({value: e + ' a ' + this.ctx.New.TestTwo().find()})).toListAsync();
    }

    @lockedQuery
    public MyQuery3() {
        return this.ctx.New.Test().map(e => ({value: e + ' a ' + this.ctx.New.TestTwo().find()})).toListAsync();
    }


    // as
    @lockedQuery
    public MyQuery4() {
        return this.ctx.New.TestThree().map(e => ({valueOne: e + '24', valueTwo: this.MyQuery3()})).findAsync();
    }

    @lockedQuery
    public MyQuery5() {
        return this.ctx.New.TestFour().map(e => ({valueOne: e + '24', valueTwo: this.MyQuery3()})).findAsync();
    }

}


function lockedQuery(target: any, propertyName: string, descriptor: TypedPropertyDescriptor<(...args: any) => any>) {

    let method = descriptor.value!.toString();
    descriptor.value = function () {
        return method;
    }
}