import {AfterViewInit, ChangeDetectionStrategy, Component, ElementRef, OnInit, QueryList, ViewChildren} from '@angular/core';
import {IComponentHeaderText} from '../app.component';
import {combineLatestWith, delay, map, Observable, of, shareReplay, skip, Subject, switchMap, take, tap} from 'rxjs';
import {IActorShortProfile} from './components/actor-short-profile/actor-short-profile.component';
import {QueryService} from '../api/actions/query.service';
import {IActorFullProfile} from './components/actor-full-profile/actor-full-profile.component';
import {ActivatedRoute} from '@angular/router';
import {OverlayTrigger} from "@spectrum-web-components/bundle";

@Component({
    selector: 'app-actors',
    templateUrl: './actors.component.html',
    styleUrls: ['./actors.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class ActorsComponent implements OnInit, AfterViewInit, IComponentHeaderText {
    readonly headerText = 'Actors';
    actorProfiles$: Observable<IActorShortProfile[]>;
    selectedActorProfile$: Observable<IActorFullProfile | null>;

    selectedActorSubject = new Subject<number>();
    
    @ViewChildren('overlay', {read: ElementRef}) overlays: QueryList<ElementRef<OverlayTrigger>>;
    
    constructor(private _api: QueryService, activatedRoute: ActivatedRoute) {
        
        this.actorProfiles$ = activatedRoute.data.pipe(
            map(({actors}) => actors),
            tap(e => console.log(e))) as any;
        
        this.selectedActorProfile$ = this.actorProfiles$.pipe(combineLatestWith(this.selectedActorSubject.asObservable()), //.pipe(switchMap(actorId => _api.GetBiography(actorId)))),
            map(([profiles, selectedId]) => profiles.find(e => e.id === selectedId)),
            switchMap(profile => profile ? _api.GetBiography(profile?.id).pipe(map(bio => ({...profile, ...bio} as IActorFullProfile))) : of(null)),
            shareReplay(1)
        );
    }

    ngOnInit(): void {
    }


    onActorClick(actorId: number, event: Event): void {
        
        let targetAvatar = (event.target as HTMLElement).closest('actor-avatar') as HTMLElement;
        
        if (targetAvatar.matches('.actors-grid overlay-trigger:nth-of-type(4n + 3) actor-avatar, .actors-grid overlay-trigger:nth-of-type(4n + 4) actor-avatar')) {
            event.stopImmediatePropagation();
            let overlay = targetAvatar.parentElement as OverlayTrigger;
            setTimeout(() => overlay.open = 'click', 350);
        }

        if (targetAvatar.matches('overlay-trigger:nth-of-type(8) ~ overlay-trigger actor-avatar')) {
            targetAvatar.scrollIntoView({block: 'start'});
            event.stopImmediatePropagation();
            let overlay = targetAvatar.parentElement as OverlayTrigger;

            this.selectedActorProfile$.pipe(skip(1), take(1), delay(350)).subscribe(_ => overlay.open = 'click');
        }
        
        this.selectedActorSubject.next(actorId);
    }

    ngAfterViewInit(): void {
        this.overlays.forEach(e => e.nativeElement.addEventListener('sp-closed', _ => this.selectedActorSubject.next(null)));
    }
    
}