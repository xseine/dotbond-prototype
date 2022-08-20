import {AwardType} from '../award';


export class AwardResult {
    public id: number;
    public type: AwardType;
    public name: string;
    public year: number;
    public movieId: number;
    public movieName: string;
}