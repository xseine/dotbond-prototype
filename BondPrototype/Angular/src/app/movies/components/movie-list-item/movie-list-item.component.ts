import {Component, Input, OnInit} from '@angular/core';
import {AwardType} from '../../../api/models/award';

@Component({
    selector: 'movie-list-item',
    templateUrl: './movie-list-item.component.html',
    styleUrls: ['./movie-list-item.component.scss']
})
export class MovieListItemComponent implements OnInit, IMovieListDetails {

    actors: string[];
    awards: { type: AwardType; name: string }[] | null;
    description: string;
    director: string;
    name: string;
    picture: string;
    rating: number;
    year: number;

    @Input() set data(value: IMovieListDetails) {
        Object.assign(this, value);
    }

    constructor() {
    }

    ngOnInit(): void {
    }



}

export interface IMovieListDetails {
    picture: string;
    name: string;
    year: number;
    rating: number;
    awards: { type: AwardType, name: string }[] | null;
    description: string;
    director: string;
    actors: string[];
}
