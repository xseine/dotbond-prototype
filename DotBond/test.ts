
export class MovieResult {
    public id: number;
    public title: string;
    public rating: number;
    public releaseDate: Date;
    public moviePoster: string;
    public description: string;
    public directedBy: MovieResult.Director;
    public actors: MovieResult.Actor[];
    public awards: MovieResult.Award[];
    get myProp(): any[] {
        console.log("Jack");

        return [
            (()=> {
                    let obj = {} as any;
                    obj.Name = "Jack White";
                    obj.Id = 1;
                    return obj;}
            )()
        ] as any[];
    }

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
        constructor(public type: any, public name: string) {
        }
    }
}
