import {Component, Directive, ElementRef, HostBinding, HostListener, Input, ViewContainerRef} from '@angular/core';

@Directive({selector: '[elipsisOnHover]'})
export class ElipsisOnHoverDirective {

    private _nativeElement: HTMLElement;

    constructor(elementRef: ElementRef, public viewContainerRef: ViewContainerRef) {
        this._nativeElement = elementRef.nativeElement;
    }

    @HostListener('mouseenter')
    addDropzone(): void {
        let component = this.viewContainerRef.createComponent<ElipsisHoverComponent>(ElipsisHoverComponent);
        component.instance.offset = [this._nativeElement.offsetTop + this._nativeElement.offsetHeight, this._nativeElement.offsetLeft + this._nativeElement.offsetWidth / 2];
    }

    @HostListener('mouseleave')
    removeDropzone(): void {
        this.viewContainerRef.clear();
    }

}


@Component({
    selector: 'elipsis-hover',
    template: `
        <div class="elipsis-hover">
            <span>.</span>
            <span>.</span>
            <span>.</span>
        </div>
    `,
    styles: [`
        :host {
            position: absolute;    
            transform: translateX(-50%) translateY(-1.2em);
        }
        
        span {
            font-size: 2em;
            letter-spacing: 1px;
        }
        span:nth-child(2) {
            animation: fadeIn .5s forwards;
        }

        span:nth-child(3) {
            animation: fadeIn .5s forwards;
        }

        @keyframes fadeIn {
            from {
                opacity: 0;
            }
            to {
                opacity: 1;
            }
        }
    `]
})
export class ElipsisHoverComponent {
    @Input() set offset(value: [number, number]) {
        this.top = value[0] + 'px';
        this.left = value[1] + 'px';
    };
    
    @HostBinding('style.top') top: string;
    @HostBinding('style.left') left: string;
}