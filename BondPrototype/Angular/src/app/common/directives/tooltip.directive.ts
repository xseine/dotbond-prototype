import {AfterViewInit, Component, Directive, ElementRef, EventEmitter, HostListener, Input, Output, TemplateRef, ViewChild, ViewContainerRef} from '@angular/core';
import {Tooltip} from '@spectrum-web-components/bundle';
import {SpectrumElement} from '@spectrum-web-components/base';
import {firstValueFrom, fromEvent, switchMap, takeUntil, tap, timer} from 'rxjs';

@Directive({
    selector: '[tooltip]'
})
export class TooltipDirective implements AfterViewInit {

    @Input('tooltip') content: string | TemplateRef<Tooltip>;
    private referenceElement: HTMLElement;
    private tooltipElement: Tooltip;
    private readonly OPEN_DELAY: number = 500;

    constructor(public viewContainerRef: ViewContainerRef, ref: ElementRef) {
        this.referenceElement = ref.nativeElement;
    }

    async ngAfterViewInit() {

        // Don't make changes to the view while in change detection.
        await timer(1);
        
        let element: Tooltip;

        if (typeof this.content === 'string') {
            let componentRef = this.viewContainerRef.createComponent<TooltipWrapperComponent>(TooltipWrapperComponent);
            setTimeout(_ => {
                componentRef.instance.content = this.content as string;    
            })
            
            element = componentRef.instance.tooltipRef.nativeElement as Tooltip;
        } else {
            let componentRef = this.viewContainerRef.createEmbeddedView(this.content);
            element = componentRef.rootNodes[0] as Tooltip;
        }

        this.tooltipElement = element;
        this.referenceElement.style.pointerEvents = 'all';
        this.tooltipElement.style.position = 'absolute';
        this.tooltipElement.style.textTransform = 'none';
        
        fromEvent(this.referenceElement, 'mouseenter').pipe(
            switchMap(_ => timer(this.OPEN_DELAY).pipe(
                    takeUntil(fromEvent(this.referenceElement, 'mouseleave'))
                )
            )
        ).subscribe(_ => {
            this.tooltipElement.style.left = this.referenceElement.offsetLeft + Math.round(this.referenceElement.offsetWidth / 2) - this.tooltipElement.offsetWidth / 2 + 'px';
            this.tooltipElement.style.bottom = this.referenceElement.offsetHeight + 7 + 'px';
            this.tooltipElement.style.top = 'unset';
            this.tooltipElement.open = true;
        })

        fromEvent(this.referenceElement, 'mouseleave').subscribe(_ => this.tooltipElement.open = false);

        // element.updateComplete is litelement promise when element renders
        // element.updateComplete.then(_ => {
        //
        // });

    }

}

@Component({
    template: `
        <sp-tooltip #tooltip placement="{{placement}}" variant="{{variant}}">{{content}}</sp-tooltip>
    `
})
export class TooltipWrapperComponent {
    @Input() content: string;
    @Input() placement: 'auto' | 'auto-start' | 'auto-end' | 'top' | 'bottom' | 'right' | 'left'
        | 'top-start' | 'top-end' | 'bottom-start' | 'bottom-end' | 'right-start' | 'right-end' | 'left-start' | 'left-end' | 'none' = 'top';
    @Input() variant: 'info' | 'positive' | 'negative' | null = null;
    
    @ViewChild('tooltip', {static: true}) tooltipRef: ElementRef;
}
