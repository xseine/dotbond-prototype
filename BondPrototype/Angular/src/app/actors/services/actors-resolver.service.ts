import {Injectable} from '@angular/core';
import {IActorShortProfile} from '../components/actor-short-profile/actor-short-profile.component';
import {ActivatedRouteSnapshot, Resolve, RouterStateSnapshot} from '@angular/router';
import {Observable} from 'rxjs';
import {QueryService} from '../../api/actions/query.service';

@Injectable({
    providedIn: 'root'
})
export class ActorsResolverService implements Resolve<IActorShortProfile[]> {

    constructor(private _api: QueryService) {
    }

    resolve(route: ActivatedRouteSnapshot, state: RouterStateSnapshot): Observable<IActorShortProfile[]> | Promise<IActorShortProfile[]> | IActorShortProfile[] {
        return this._api.GetShortProfilesOfActors();
    }

}
