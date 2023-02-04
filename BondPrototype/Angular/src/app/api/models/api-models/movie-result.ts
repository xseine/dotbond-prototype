import {AwardType} from '../award';


export class MovieResult {
    public id: number;
    public title: string;
    public rating: number;
    public releaseDate: Date;
    public moviePoster: string;
    public description: string;
    public get savo() { return `Hello there..`; }
    public directedBy: MovieResult.Director;
    public actors: MovieResult.Actor[];
    public awards: MovieResult.Award[];
}

export namespace MovieResult {
    export class Director {
        constructor(public id: number, public name: string, public dateOfBirth: Date | null) {
        }
    }
    export class Actor {
        constructor(public id: number, public name: string, public dateOfBirth: Date | null) {
        }
    }
    export class Award {
        constructor(public type: AwardType, public name: string) {
        }
    }
}
