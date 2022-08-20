import {Component, OnInit} from '@angular/core';
import {IComponentHeaderText} from '../app.component';
import {IMovieListDetails} from './components/movie-list-item/movie-list-item.component';
import {QueryService} from '../api/actions/query.service';
import {map, Observable} from 'rxjs';
import {ActivatedRoute} from '@angular/router';

@Component({
    selector: 'app-movies',
    templateUrl: './movies.component.html',
    styleUrls: ['./movies.component.scss']
})
export class MoviesComponent implements OnInit, IComponentHeaderText {
    readonly headerText = 'Movies';

    movies$: Observable<IMovieListDetails[]>;

    constructor(private _api: QueryService, activatedRoute: ActivatedRoute) {
        this.movies$ = activatedRoute.data.pipe(map(({movies}) => movies)) as any;
    }

    ngOnInit(): void {
    }


}
