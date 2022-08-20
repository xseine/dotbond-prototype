import {Injectable} from '@angular/core';
import {QueryService} from '../../api/actions/query.service';
import {ActivatedRouteSnapshot, Resolve, RouterStateSnapshot} from '@angular/router';
import {Observable} from 'rxjs';
import {IMovieListDetails} from '../components/movie-list-item/movie-list-item.component';

@Injectable({
    providedIn: 'root'
})
export class MoviesResolverService implements Resolve<IMovieListDetails[]>{

    constructor(private _api: QueryService) {
    }

    resolve(route: ActivatedRouteSnapshot, state: RouterStateSnapshot): Observable<IMovieListDetails[]> | Promise<IMovieListDetails[]> | IMovieListDetails[] {
        return this._api.GetMovieListDetails();
    }
}
