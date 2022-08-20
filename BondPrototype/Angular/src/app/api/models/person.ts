import {Movie} from './movie';
import {key} from '../actions/decorators';


export class Person {
    
    @key() public id: number;
    public name: string;
    public dateOfBirth: Date | null;
    public picture: string;
    public biography: string;
    public directed: Movie[];
    public actedIn: Movie[];
}