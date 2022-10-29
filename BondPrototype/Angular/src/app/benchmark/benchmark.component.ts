import {ChangeDetectionStrategy, Component, ElementRef, Inject, OnInit, ViewChild} from '@angular/core';
import {IComponentHeaderText} from '../app.component';
import {concat, filter, fromEvent, last, map, merge, mergeScan, mergeWith, Observable, of, scan, shareReplay, startWith, Subject, switchMap, take, tap} from 'rxjs';
import {QueryService} from '../api/actions/query.service';
import {getParams} from '../api/actions/library/miscellaneous';
import {FormBuilder, FormGroup} from '@angular/forms';
import {HttpClient} from '@angular/common/http';
import {CdkDragDrop, moveItemInArray} from '@angular/cdk/drag-drop';
import {ENVIRONMENT_PROVIDER} from '../../core/services/enviroment.provider';

@Component({
    selector: 'app-benchmark',
    templateUrl: 'benchmark.component.html',
    styleUrls: ['./benchmark.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class BenchmarkComponent implements OnInit, IComponentHeaderText {

    readonly headerText = 'Speed benchmark';
    bondQueries: string[];
    querySelection$: Observable<BenchmarkResult[]>;
    queryPick = new Subject<string>();
    queryRemoval = new Subject<number>();
    queryBenchmarkRunClicked = new Subject<BenchmarkResult>();
    rowMove = new Subject<[number, number]>();

    paramsForm: FormGroup;

    get getParamNames(): string[] {
        return this.paramsForm ? Object.keys(this.paramsForm.controls) : [];
    }

    // get paramsFormArray(): FormArray {
    //     return this.paramsForm.get('params') as FormArray;
    // } 

    @ViewChild('trigger', {read: ElementRef}) dialogTrigger: ElementRef;

    constructor(private _api: QueryService, private _fb: FormBuilder, private _httpClient: HttpClient, @Inject(ENVIRONMENT_PROVIDER) private _environment: any) {

        // import dialog required module
        import('@spectrum-web-components/overlay');

        this.paramsForm = _fb.group({});

        this.bondQueries = Object.getOwnPropertyNames(Object.getPrototypeOf(_api)).filter(e => e !== 'constructor');

        let savedResults = JSON.parse(localStorage.getItem('benchmark-results') as string) ?? [];
        let querySequence = savedResults.length + 1;

        this.querySelection$ = this.queryPick.pipe(
            map(query => ({id: querySequence++, name: query, paramNames: this.getParams(query)})),
            switchMap(query => query.paramNames
                ? this.getParamsFromDialog(query.paramNames).pipe(filter(e => !!e), map(params => ({id: query.id, name: query.name, params})))
                : of({id: query.id, name: query.name})),
            mergeWith(this.queryBenchmarkRunClicked.pipe(map(query => ({...query, isRunning: true})))),
            mergeWith(this.queryBenchmarkRunClicked.pipe(mergeScan((_, query) => this.runBenchmark(query).pipe(map(query => ({...query, isRunning: false}))), 0 as any))),
            mergeWith(this.queryRemoval.pipe(map(id => ({id, remove: true})))),
            scan((acc, curr) => {

                if (savedResults) {
                    acc = savedResults;
                    savedResults = null;
                }

                if ('remove' in curr) return acc.filter(e => e.id !== curr.id);

                let existingRowIdx: number;
                if ((existingRowIdx = acc.findIndex(e => e.id == curr.id)) != -1) {
                    acc[existingRowIdx] = curr as BenchmarkResult;
                    return acc;
                } else {
                    let newRow = {id: curr.id, name: curr.name, params: 'params' in curr ? curr.params : undefined, numberOfIterations: 1, isClientSide: false} as BenchmarkResult;
                    return [...acc, newRow];
                }
            }, [] as BenchmarkResult[]),
            startWith(savedResults as BenchmarkResult[]),
            switchMap(rows => this.rowMove.pipe(tap(([from, to]) => moveItemInArray(rows, from, to)), map(_ => rows), startWith(rows))),
            tap(rows => localStorage.setItem('benchmark-results', JSON.stringify(rows))),
            shareReplay(1)
        ) as Observable<BenchmarkResult[]>;
    }

    drop(event: CdkDragDrop<string[]>) {
        console.log(event);
        // moveItemInArray(this.movies, event.previousIndex, event.currentIndex);
    }

    ngOnInit(): void {
    }

    runBenchmark(queryToRun: BenchmarkResult): Observable<BenchmarkResult> {

        let parameters = queryToRun.params;

        // if (parameters)
        //     console.log('Here are params: ', parameters);
        //
        // console.log('Is clientside: ' + queryToRun.isClientSide);

        // (this._api[queryToRun.name] as any).forceClientSide = queryToRun.isClientSide; // clears on its own

        return concat(...[...Array(queryToRun.numberOfIterations!)].map(_ => of(1).pipe(
                switchMap(_ => {
                    let startTime = performance.now();
                    (this._api[queryToRun.name] as any).forceClientSide = queryToRun.isClientSide; // clears on its own
                    let httpObservable: Observable<any> = parameters ? this._api[queryToRun.name](...Object.values(parameters)) : this._api[queryToRun.name]();
                    return httpObservable.pipe(
                        // tap(e => console.log(e)),
                        map(_ => performance.now() - startTime)
                    );
                })
            ))
        ).pipe(
            scan((acc, curr) => acc.concat(curr), [] as number[]),
            last(),
            map(times => {
                // mean = sum / n
                // standard dev = sqrt(sum (x_i - mean)**2 / n)
                // error = dev / sqrt(n)

                let n = queryToRun.numberOfIterations!;
                let mean = times.reduce((acc, curr) => acc + curr, 0) / n;
                let stdDev = Math.sqrt(times.reduce((acc, curr) => acc + Math.pow(curr - mean, 2), 0) / n);
                let error = stdDev / Math.sqrt(n);

                // [mean, stdDev, error] = [mean, stdDev, error].map(e => Math.round(e * 100) / 100);

                return {...queryToRun, mean, stdDev, error} as BenchmarkResult
            })
        ) as Observable<BenchmarkResult>;

    }


    getParams(queryName: string): string[] | null {
        let methodDefinition = this._api[queryName].originalMethod as string;
        let parameters = getParams(methodDefinition);

        if (!parameters.length) return null;

        if (this._environment.production)
            parameters = parameters.map((_, idx) => `param${idx}`)

        return parameters
    }


    getParamsFromDialog(paramNames: string[]): Observable<{ [key: string]: string } | false> {
        this.paramsForm = this._fb.group(paramNames.reduce((acc, curr) => ({...acc, [curr]: [null]}), {}));
        this.dialogTrigger.nativeElement.click();

        let overlayTrigger = this.dialogTrigger.nativeElement.parentElement;
        let dialogWrapper = overlayTrigger.clickContent;

        return merge(
            fromEvent(dialogWrapper, 'confirm').pipe(map(_ => this.paramsForm.value)),
            fromEvent(dialogWrapper, 'close').pipe(map(_ => false as false))
        ).pipe(take(1));
    }

    createEvent(eventName: string): Event {
        return new Event(eventName);
    }

    stringifyObject(object: any) {
        return (JSON.stringify(object) as any).replaceAll(/"/g, '').replaceAll(/^{|}$/g, '');
    }

}

// mean = sum / n
// error = dev / sqrt(n)
// standard dev = sqrt(sum (x_i - mean)**2 / n)
type BenchmarkResult = { id: number, name: string, params: { [key: string]: string } | null, numberOfIterations: number | null, isClientSide: boolean, mean: number | null, error: number | null, stdDev: number | null, isRunning: boolean };

function isBenchmarkResult(value: any): value is BenchmarkResult {
    return value.mean !== undefined;
}
