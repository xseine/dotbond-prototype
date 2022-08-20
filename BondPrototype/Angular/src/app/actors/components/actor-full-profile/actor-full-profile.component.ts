import {Component, Input, OnInit} from '@angular/core';
import {IActorShortProfile} from '../actor-short-profile/actor-short-profile.component';

@Component({
    selector: 'actor-full-profile',
    template: `
        <div *ngIf="id != null">
            <h1 class="spectrum-Heading spectrum-Heading--sizeL">{{name}}</h1>

            <div class="role-list spectrum-Body spectrum-Body--sizeM">Roles:</div>
            <ul class="role-list spectrum-Body spectrum-Body--sizeM">
                <li *ngFor="let movie of movies">{{movie}}</li>
            </ul>

            <p class="spectrum-Body spectrum-Body--sizeM biography-text">
                {{biography}}
            </p>    
        </div>

        <div *ngIf="id == null" class="placeholder">
            <h1 class="spectrum-Heading spectrum-Heading--sizeL spectrum-Heading--light"><em>Actor's name</em></h1>

            <div>Roles:</div>
            <ul class="role-list spectrum-Body spectrum-Body--sizeM">
                <li>[Movie]</li>
                <li>[Movie]</li>
            </ul>

            <p class="spectrum-Body spectrum-Body--sizeM biography-text">
                <em>Actor's biography will show here...</em>
            </p>

        </div>
        
    `,
    styleUrls: ['./actor-full-profile.component.scss']
})
export class ActorFullProfileComponent implements OnInit, IActorFullProfile {

    id: number;
    picture: string;
    name: string;
    numberOfMovies: number;
    biography: string;
    movies: string[];

    @Input() set data(value: IActorFullProfile | null) {
        Object.assign(this, value);
    }
    
    constructor() {
    }

    ngOnInit(): void {
    }

}


export interface IActorFullProfile extends IActorShortProfile {
    biography: string;
    movies: string[];
}