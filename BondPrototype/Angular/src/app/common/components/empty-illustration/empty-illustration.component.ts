import {Component, Input, OnInit} from '@angular/core';
// @ts-ignore
import emptyBoxSource from '/src/assets/icons/empty-box.svg';

@Component({
    selector: 'empty-illustration',
    template: `
        <sp-illustrated-message
                heading="{{heading}}"
                description="{{description}}"
                [innerHTML]="emptyBoxSource | safeHtml"
        >
        </sp-illustrated-message>`,
    styles: [`
        :host {
            height: 20em;
        }
    `]
})
export class EmptyIllustrationComponent implements OnInit {

    @Input() heading: string;
    @Input() description: string;
    @Input() public size: string = '7em';

    emptyBoxSource = emptyBoxSource;
    
    constructor() {
    }

    ngOnInit(): void {
        this.emptyBoxSource = this.emptyBoxSource.replace(/(?<=<svg.+?height=)".+?"/, `"${this.size}"`).replace(/(?<=<svg.+?width=)".+?"/, `"${this.size}"`);
    }

}
