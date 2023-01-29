import {ChangeDetectionStrategy, Component, OnInit} from '@angular/core';
import {IComponentHeaderText} from '../app.component';
import {QueryService} from '../api/actions/query.service';
import {map, mergeWith, Observable, scan, shareReplay, startWith, Subject, switchMap, tap} from 'rxjs';
// @ts-ignore
import emptyBoxSource from '/src/assets/icons/empty-box.svg';

@Component({
    selector: 'directing',
    template: `

        <div class="spectrum-Form-item" style="margin-top: 1em">
            <sp-field-label for="picker-m" size="m">Add an actor:</sp-field-label>
            <sp-picker id="picker-m" size="m" label="Actor name" #picker (change)="actorPick.next(+picker.value)">
                <sp-menu-item *ngFor="let actor of (actorSelection$ | async)!.listOfNames" value="{{actor.id}}">{{actor.name}}</sp-menu-item>
            </sp-picker>
        </div>
        
        <div class="spectrum-grid">
            <table class="actors spectrum-Table spectrum-Table--sizeM spectrum-Table--spacious spectrum-Table--quiet"
                   *ngIf="(actorSelection$ | async)!.profilesAndStats.length !== 0">
                <thead class="spectrum-Table-head">
                <tr>
                    <th class="spectrum-Table-headCell"></th>
                    <th class="spectrum-Table-headCell"></th>
                    <th class="spectrum-Table-headCell">Average movie rating</th>
                    <th class="spectrum-Table-headCell">Number of movies</th>
                    <th class="spectrum-Table-headCell">Colleagues</th>
                    <th></th>
                </tr>
                </thead>
                <tbody class="spectrum-Table-body" [transition-group]="'flip-list'">
                <tr class="spectrum-Table-row" *ngFor="let actor of (actorSelection$ | async)!.profilesAndStats"
                    (mouseenter)="addHoverOnColleagues(actor.id, getIds(actor.colleagues))"
                    (mouseleave)="removeHoverFromColleagues()"
                    [ngClass]="{'hover': hoveredColleagues.includes(actor.id)}"
                    transition-group-item
                >
                    <td class="spectrum-Table-cell"><img [src]="actor.picture | safeUrl" alt="Actor's photo"/></td>
                    <td class="spectrum-Table-cell"><h2 class="spectrum-Heading spectrum-Heading--sizeS">{{actor.name}}</h2></td>
                    <td class="spectrum-Table-cell">{{Math.round(actor.average * 10) / 10}} / 10</td>
                    <td class="spectrum-Table-cell">{{actor.numberOfMovies}}</td>
                    <td class="spectrum-Table-cell">{{getNames(actor.colleagues).join(',\\n')}}</td>
                    <td class="remove spectrum-Table-cell" (click)="actorRemoval.next(actor.id)">
                        <sp-icon-close></sp-icon-close>
                    </td>
                </tr>
                </tbody>
            </table>

            <empty-illustration heading="Table is empty" description="Select actors to view their stats"
                                *ngIf="(actorSelection$ | async)!.profilesAndStats.length === 0"></empty-illustration>
        </div>
    `,
    styleUrls: ['./directing.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class DirectingComponent implements OnInit, IComponentHeaderText {
    readonly headerText = 'Direct your own';

    actorSelection$: Observable<{ listOfNames: { name: string, id: number }[], profilesAndStats: ProfileAndStatsType[] }>;
    actorPick = new Subject<number>();
    actorRemoval = new Subject<number>();
    Math = Math;

    joinColleagues = new Subject<{actorId: number, colleagues: number[]}>();
    
    constructor(private _api: QueryService) {

        this.actorSelection$ = _api.GetListOfActorNames().pipe(
            switchMap(actors => this.actorPick.pipe(
                    switchMap(id => _api.GetShortProfileAndWorkStats(id).pipe(          // Load profile and stats
                        mergeWith(this.actorRemoval))),                                         // And subscribe to removal
                    scan((acc, curr) =>
                        isNaN(curr as any)
                            ? [...acc, curr as ProfileAndStatsType]
                            : acc.filter(e => e.id !== curr), [] as ProfileAndStatsType[]),     // Either append the new actor, or remove an existing
                    map(profilesAndStats => ({listOfNames: actors.filter(e => profilesAndStats.every(ee => ee.id != e.id)), profilesAndStats})),    // Output a list of names and a list of stats
                    startWith({
                        listOfNames: actors,
                        profilesAndStats: []
                    })
                )
            ),
            startWith({listOfNames: [], profilesAndStats: [] as ProfileAndStatsType[]}),
            switchMap(e => {
                let order = e.profilesAndStats;
                return this.joinColleagues.pipe(map(({actorId, colleagues}) => {
                    let nonColleaguesBefore = order.slice(0, order.findIndex(e => e.id === actorId)).filter(e => !colleagues.includes(e.id));
                    let nonColleaguesAfter = order.slice(order.findIndex(e => e.id === actorId) + 1).filter(e => !colleagues.includes(e.id));
                    
                    order = [...nonColleaguesBefore, ...order.filter(e => e.id === actorId || colleagues.includes(e.id)), ...nonColleaguesAfter];
                    
                    return {...e, profilesAndStats: [...order]};
                }), startWith(e));
            }),
            shareReplay(1)
        )
    }

    ngOnInit(): void {
    }

    /*========================== Event Listeners ==========================*/

    hoveredColleagues: number[] = [];

    addHoverOnColleagues(actorId: number, colleaguesIds: number[]): void {
        this.hoveredColleagues = colleaguesIds;
        this.joinColleagues.next({actorId, colleagues: colleaguesIds});
    }

    removeHoverFromColleagues(): void {
        this.hoveredColleagues = [];
    }

    /*========================== Public API ==========================*/

    public getNames(data: { name: string, id: number }[]): string[] {
        return data.map(e => e.name);
    }

    public getIds(data: { name: string, id: number }[]): number[] {
        return data.map(e => e.id);
    }

}

type ProfileAndStatsType = ReturnType<QueryService['GetShortProfileAndWorkStats']> extends Observable<infer ProfileAndStatsType> ? Omit<ProfileAndStatsType, '[Symbol.iterator]'> : never;
