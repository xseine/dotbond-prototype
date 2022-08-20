import {AfterViewInit, ChangeDetectionStrategy, ChangeDetectorRef, Component, HostBinding, Inject, Input, OnInit} from '@angular/core';
import Prism from 'prismjs';
import {htmlToElement} from '../../../common/other/misc';
import {DOCUMENT} from '@angular/common';
import {IWantsFullscreen} from '../../../app.component';
import {ActivatedRoute} from '@angular/router';
import {TRANSLATION_STORAGE_KEY} from '../../translate-demo.component';
import {BehaviorSubject, firstValueFrom, forkJoin, from, fromEvent, map, Observable, shareReplay, startWith, Subject, take, tap} from 'rxjs';

@Component({
    selector: 'translated-content',
    template: `
        <div id="output-div" [innerHTML]="translation$ | async"></div>
    `,
    styleUrls: ['./translated-content.component.scss']
})
export class TranslatedContentComponent implements OnInit, AfterViewInit, IWantsFullscreen {

    readonly enableFullscreen = true;

    @Input() set translation(value: string) {
        this.translationInput.next(value)
    };

    translationInput = new BehaviorSubject<string>(null);
    translation$: Observable<string>;

    @HostBinding('class.main') readonly isMainComponent: boolean;

    constructor(@Inject(DOCUMENT) private document: Document, private route: ActivatedRoute, private _cd: ChangeDetectorRef) {
        this.isMainComponent = (route.component as any).name === 'TranslatedContentComponent';
    }

    ngOnInit(): void {
    }

    async ngAfterViewInit() {
        // Load Prism
        await firstValueFrom(forkJoin(
            from(import('prismjs/components/prism-css')),
            from(import('prismjs/components/prism-javascript')),
            from(import('prismjs/components/prism-typescript')),
            from(import('prismjs/components/prism-scss'))));

        let styles = ['/assets/css/prism/prism.css'];
        let head = this.document.getElementsByTagName('head')[0];
        for (let style of styles)
            head.appendChild(htmlToElement(
                `<link rel="preload" as="style" href="${style}" onload="this.rel='stylesheet'"/>`,
                document));
        
        this.translation$ = (this.isMainComponent
            ? fromEvent(window, 'storage').pipe(map(_ => window.localStorage.getItem(TRANSLATION_STORAGE_KEY)), startWith(window.localStorage.getItem(TRANSLATION_STORAGE_KEY)))
            : this.translationInput.asObservable()).pipe(
            map(translation => translation ? Prism.highlight(translation, Prism.languages['typescript'], 'typescript') : null)
        );
        
        // Used to 
        // this._cd.detectChanges();
    }
}
