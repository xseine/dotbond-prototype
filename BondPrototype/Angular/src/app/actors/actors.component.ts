import {ChangeDetectionStrategy, Component, OnInit} from '@angular/core';
import {IComponentHeaderText} from '../app.component';
import {combineLatestWith, map, Observable, of, share, Subject, switchMap, startWith, shareReplay, take, tap} from 'rxjs';
import {IActorShortProfile} from './components/actor-short-profile/actor-short-profile.component';
import {QueryService} from '../api/actions/query.service';
import {IActorFullProfile} from './components/actor-full-profile/actor-full-profile.component';
import {HttpClient} from '@angular/common/http';
import {ActivatedRoute} from '@angular/router';

@Component({
    selector: 'app-actors',
    template: `
        <div class="spectrum-grid">
            <div class="actors-grid spectrum-grid" #grid>
                <actor-short-profile
                        *ngFor="let actor of actorProfiles$ | async"
                        [data]="actor"
                        (click)="selectedActorSubject.next(actor.id)"
                        elipsisOnHover
                        [attr.active]="(selectedActorProfile$ | async)?.id == actor.id ? '' : null"
                >
                </actor-short-profile>
            </div>

            <actor-full-profile (mouseenter)="grid.classList.add('actor-active')" (mouseleave)="grid.classList.remove('actor-active')"
                                [data]="selectedActorProfile$ | async"
                                [style.pointer-events]="(selectedActorProfile$ | async) != null ? 'auto' : 'none'"></actor-full-profile>

        </div>`,

    styleUrls: ['./actors.component.scss'],
})
export class ActorsComponent implements OnInit, IComponentHeaderText {
    readonly headerText = 'Actors';
    actorProfiles$: Observable<IActorShortProfile[]>;
    selectedActorProfile$: Observable<IActorFullProfile | null>;

    selectedActorSubject = new Subject<number>();
    
    constructor(private _api: QueryService, activatedRoute: ActivatedRoute) {
        
        this.actorProfiles$ = activatedRoute.data.pipe(map(({actors}) => actors)) as any;
        this.selectedActorProfile$ = this.actorProfiles$.pipe(combineLatestWith(this.selectedActorSubject.asObservable()), //.pipe(switchMap(actorId => _api.GetBiography(actorId)))),
            map(([profiles, selectedId]) => profiles.find(e => e.id === selectedId)),
            switchMap(profile => profile ? _api.GetBiography(profile?.id).pipe(map(bio => ({...profile, ...bio} as IActorFullProfile))) : of(null)),
            shareReplay(1)
        );
    }

    ngOnInit(): void {
    }


}