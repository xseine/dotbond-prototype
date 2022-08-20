


export class PersonResult {
    public id: number;
    public name: string;
    public dateOfBirth: Date | null;
    public picture: string;
    public biography: string;
    public directed: PersonResult.DirectedOrActedInMovie[];
    public actedIn: PersonResult.DirectedOrActedInMovie[];
}

export namespace PersonResult {
    export class DirectedOrActedInMovie {
        constructor(public id: number, public title: string, public rating: number, public releaseDate: Date) {
        }
    }
}
