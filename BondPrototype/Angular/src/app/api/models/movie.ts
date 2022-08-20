import {Person} from './person';
import {Award} from './award';
import {key} from '../actions/decorators';

export class Movie {
    @key() public id: number;
    public title: string;
    public rating: number;
    public releaseDate: Date;
    public moviePoster: string;
    public description: string;
    public directedBy: Person;
    public actors: Person[];
    public awards: Award[];
}