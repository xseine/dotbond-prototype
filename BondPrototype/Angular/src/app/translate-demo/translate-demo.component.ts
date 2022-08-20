import {AfterViewInit, ChangeDetectionStrategy, ChangeDetectorRef, Component, ElementRef, OnDestroy, ViewChild} from '@angular/core';
import {IComponentHeaderText} from '../app.component';
import {catchError, debounceTime, filter, firstValueFrom, fromEvent, interval, merge, mergeWith, Observable, of, shareReplay, startWith, Subject, Subscription, switchMap, take, tap} from 'rxjs';
import {QueryService} from '../api/actions/query.service';
// @ts-ignore
import objectCreationSource from './examples/object-creation.txt';
// @ts-ignore
import declarationsSource from './examples/declarations.txt';
// @ts-ignore
import basicsSource from './examples/basics.txt';
// @ts-ignore
import linqSource from './examples/linq.txt';
// @ts-ignore
import patternMatchingSource from './examples/pattern-matching.txt';
import {spToast} from '../common/other/spToast';
import {UntilDestroy} from '@ngneat/until-destroy';
import XRegExp from 'xregexp';

export const TRANSLATION_STORAGE_KEY = 'translation';
export const USER_SHEETS_KEY = 'userSheets';

@UntilDestroy({arrayName: 'subscriptions'})
@Component({
    selector: 'app-translate-demo',
    templateUrl: 'translate-demo.component.html',
    styleUrls: ['./translate-demo.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class TranslateDemoComponent implements AfterViewInit, IComponentHeaderText, OnDestroy {
    readonly headerText = 'Translate Demo';

    @ViewChild('csharp', {read: ElementRef}) csharpTextArea: ElementRef;
    @ViewChild('consoleTrayTrigger', {read: ElementRef}) consoleTrayTrigger: ElementRef;
    @ViewChild('saveAsModalTrigger', {read: ElementRef}) saveAsModalTrigger: ElementRef;
    
    builtInExamples = ['Basics', 'Object Creation', 'Declarations', 'LINQ', 'Pattern Matching'] as const;
    translateClick = new Subject<void>();
    isLoadingTranslation = false;
    translation$: Observable<string>;
    selectedExample = new Subject<void>();
    isPoppedOut = false;
    executionResults: any[] = [];
    executionException: string;
    
    // Used for saving current code to localStorage
    nameOfUserCodeSheet: string;
    get userCodeSheets(): string[] {
        let savedUserSheets = JSON.parse(localStorage.getItem(USER_SHEETS_KEY)) ?? {};
        return Object.keys(savedUserSheets);
    }
    subscriptions: Subscription[] = [];

    constructor(private _api: QueryService, private _cd: ChangeDetectorRef) {
        
        // Main observable
        this.translation$ = this.translateClick.asObservable().pipe(
            switchMap(_ => fromEvent(this.csharpTextArea.nativeElement, 'keyup').pipe(
                debounceTime(1000),
                startWith(null as any),
                mergeWith(this.selectedExample)
            )),
            tap(_ => this.isLoadingTranslation = true),
            switchMap(_ => this._api.TranslateDemo.Post(this.csharpTextArea.nativeElement.value).pipe(catchError(_ => of('')))),
            // retry(1),
            tap(_ => this.isLoadingTranslation = false),
            shareReplay(1)
        );

        // Load typescript compiler on client
        if (!window['ts']) {
            let node = document.createElement('script');
            node.src = '/assets/js/typescript-client.js';
            node.type = 'text/javascript';
            document.body.appendChild(node);
        }
    }

    ngAfterViewInit(): void {
    }

    /*========================== Event Listeners ==========================*/

    public loadExampleOnPick(example: typeof this.builtInExamples[number] | string): void {
        let value = example === 'Object Creation'
            ? objectCreationSource
            : example === 'Declarations'
                ? declarationsSource
                : example === 'Basics'
                    ? basicsSource
                    : example === 'LINQ'
                        ? linqSource
                        : example === 'Pattern Matching' ?
                            patternMatchingSource 
                            : null;

        // If null, then it's a saved user sheet
        if (!value) {
            let savedUserSheets = JSON.parse(localStorage.getItem(USER_SHEETS_KEY));
            let entry = Object.entries(savedUserSheets).find(([key, _]) => key === example);
            
            if (entry) {
                this.nameOfUserCodeSheet = entry[0];
                value = entry[1];
            }
        }
        
        this.csharpTextArea.nativeElement.value = value;
        this.selectedExample.next();
    }

    public async copyToClipboard() {
        let translation = await firstValueFrom(this.translation$);
        navigator.clipboard.writeText(translation);
        spToast.positive('Copied to clipboard');
    }

    public popOut() {
        let url = '/translate-content-fullscreen';
        let settings = 'height=500,width=400,left=100,top=100,resizable=yes,scrollbars=yes,toolbar=no,menubar=no,location=no,directories=no, status=no';
        let popupWindow = window.open(url, 'somename', settings);

        let s1 = this.translation$.subscribe(translation => localStorage.setItem(TRANSLATION_STORAGE_KEY, translation));
        this.subscriptions.push(s1);
        this.isPoppedOut = true;

        fromEvent(popupWindow, 'visibilitychange').pipe(
            filter(_ => popupWindow.document.visibilityState === 'hidden'),
            switchMap(_ => interval(100).pipe(take(10))),
            filter(_ => popupWindow.window.closed),
            take(1)
        ).subscribe(_ => {
            s1.unsubscribe();
            this.isPoppedOut = false;
            this._cd.detectChanges();
            
            // No idea, but translated content was empty before this line
            setTimeout(() => this._cd.detectChanges());
        });

    }
    
    public async executeTs() {
        let tsSource = await firstValueFrom(this.translation$);
        
        let ts = window['ts'] as any;
        let options = {compilerOptions: {module: ts.ModuleKind.ES2015, target: ts.ScriptTarget.ES2017}};
        
        let jsSource = ts.transpileModule(tsSource, options).outputText as string;
        

        let matchedDefinitions = [];
        let balancedCurlyBRaces = XRegExp.matchRecursive(jsSource, '\\{', '\\}', 'g');
        balancedCurlyBRaces.map(e => new RegExp('class \\w+ \\{' + e + '\\}')).forEach(e => {
            let matchedDefinition = jsSource.match(e)?.[0];
            
            if (matchedDefinition) {
                jsSource = jsSource.replace(matchedDefinition, '');
                matchedDefinitions.push(matchedDefinition);
            }
        })
        
        // Put definition at the top of the page
        jsSource = matchedDefinitions.join("\n") + "\n" + jsSource;
        
        // remove export
        jsSource = jsSource.replace(/export \{.*?};/g, '');
        jsSource = jsSource.replace(/export (?=class|function|const|let)/g, '');
        
        let consoleOverride = `
let console = {};
let __result = [];
console.log = function () {__result = [...__result, ...arguments];};
`;
        
        jsSource = consoleOverride + jsSource;
        jsSource += 'return __result';

        console.log(jsSource);


        this.executionResults = null;
        this.executionException = null;
        
        try {
            this.executionResults = (Function(jsSource)() as any[]).map(e => JSON.stringify(e, null, '\t'));
        } catch (e) {
            this.executionException = e.toString();
        }
        
        this.consoleTrayTrigger.nativeElement.click();
    }

    /**
     * Save the contents of csharp textarea to localStorage.
     * @param name Name to use for sheet, and if not provided name of the current active sheet will be used.
     */
    public save(name?: string) {
        name = name ?? this.nameOfUserCodeSheet;
        if (!name) return this.saveAs();
        
        let currentCode = this.csharpTextArea.nativeElement.value;
        
        let savedUserSheets = JSON.parse(localStorage.getItem(USER_SHEETS_KEY));
        savedUserSheets = {...savedUserSheets, [name]: currentCode};
        
        localStorage.setItem(USER_SHEETS_KEY, JSON.stringify(savedUserSheets));
        spToast.positive('Saved successfully');
    }

    /**
     * Creates dialog for user the enter the name of the sheet, and after save it using that name.
     */
    public saveAs() {
        this.saveAsModalTrigger.nativeElement.click();

        let overlayTrigger = this.saveAsModalTrigger.nativeElement.parentElement;
        let dialogWrapper = overlayTrigger.clickContent;

        merge(
            fromEvent(dialogWrapper, 'confirm').pipe(tap(_ => this.save(this.nameOfUserCodeSheet))),
            fromEvent(dialogWrapper, 'close').pipe(tap(_ => this.nameOfUserCodeSheet = null))
        ).pipe(take(1)).subscribe();
    }

    /**
     * Remove sheet from localStorage, and clear the csharp textarea.
     */
    public deleteActiveSheet() {
        let savedUserSheets = JSON.parse(localStorage.getItem(USER_SHEETS_KEY));
        savedUserSheets = Object.fromEntries(Object.entries(savedUserSheets).filter(([key, _]) => key !== this.nameOfUserCodeSheet));
        localStorage.setItem(USER_SHEETS_KEY, JSON.stringify(savedUserSheets));
        
        this.csharpTextArea.nativeElement.value = '';
        this.nameOfUserCodeSheet = null;
    }

    /*========================== Private API ==========================*/

    ngOnDestroy() {
    }

    createEvent(eventName: string): Event {
        return new Event(eventName);
    }

}
