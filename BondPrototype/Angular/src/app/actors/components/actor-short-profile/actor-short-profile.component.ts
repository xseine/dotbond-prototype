import {Component, Input, OnInit} from '@angular/core';

@Component({
    selector: 'actor-avatar',
    template: `
        <img [src]="picture | safeUrl" alt="Actor's photo"/>

        <h2 class="spectrum-Heading spectrum-Heading--sizeS">{{name}}</h2>
        <div class="number-of-movies">{{numberOfMovies}} movie{{numberOfMovies > 1 ? 's' : null}}</div>
    `,
    styleUrls: ['./actor-short-profile.component.scss']
})
export class ActorShortProfileComponent implements OnInit, IActorShortProfile {

    id: number
    picture: string;
    name: string;
    numberOfMovies: number;

    @Input() set data(value: IActorShortProfile) {
        Object.assign(this, value);
    }

    constructor() {
    }

    ngOnInit(): void {
    }

}

export interface IActorShortProfile {
    id: number;
    picture: string;
    name: string;
    numberOfMovies: number;
}