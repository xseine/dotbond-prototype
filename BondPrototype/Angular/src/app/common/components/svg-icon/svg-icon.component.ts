import {Component, Input, OnInit} from '@angular/core';
// @ts-ignore
import bowTieSource from '/src/assets/icons/bow-tie.svg';
// @ts-ignore
import directingSource from '/src/assets/icons/directing.svg';
// @ts-ignore
import movieSource from '/src/assets/icons/movie.svg';
// @ts-ignore
import questionSource from '/src/assets/icons/question-mark.svg';
// @ts-ignore
import gearSource from '/src/assets/icons/gear.svg';
// @ts-ignore
import benchmarkSource from '/src/assets/icons/benchmark.svg';
// @ts-ignore
import emptyBoxSource from '/src/assets/icons/empty-box.svg';
// @ts-ignore
import translateSource from '/src/assets/icons/translate.svg';
// Note: these imports are possible because of "webpack.partial.js"

@Component({
    selector: 'svg-icon',
    template: `
        <div [innerHTML]="svgSource | safeHtml"></div>`
})
export class SvgIconComponent implements OnInit {

    static iconSource = {
        bowTie: bowTieSource,
        directing: directingSource,
        gear: gearSource,
        movie: movieSource,
        question: questionSource,
        benchmark: benchmarkSource,
        emptyBox: emptyBoxSource,
        translate: translateSource
    }

    @Input() public name: keyof typeof SvgIconComponent.iconSource;

    /**
     * Css unit for size: 1em, 10px, etc.
     */
    @Input() public size: string = '1em';
    public svgSource: string;

    constructor() {
    }

    ngOnInit(): void {
        this.svgSource = this.name ? SvgIconComponent.iconSource[this.name] : null;

        if (this.svgSource) {
            this.svgSource = this.svgSource.replace(/(?<=<svg.+?height=)".+?"/, `"${this.size}"`).replace(/(?<=<svg.+?width=)".+?"/, `"${this.size}"`);
        }


    }

}
