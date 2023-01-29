import {Component, ContentChildren, Directive, ElementRef, Input, QueryList} from "@angular/core";

@Directive({
    selector: '[transition-group-item]'
})
export class TransitionGroupItemDirective {
    prevPos: any;
    prevIndex: number;

    newPos: any;
    newIndex: number;

    el: HTMLElement;

    moved: boolean;

    moveCallback: any;

    constructor(elRef: ElementRef) {
        this.el = elRef.nativeElement;
    }
}


@Component({
    selector: '[transition-group]',
    template: '<ng-content></ng-content>'
})
export class TransitionGroupComponent {
    @Input('transition-group') class;

    @ContentChildren(TransitionGroupItemDirective) items: QueryList<TransitionGroupItemDirective>;

    ngAfterContentInit() {
        this.refreshPosition('prevPos', 'prevIndex');
        this.items.changes.subscribe((items: TransitionGroupItemDirective[]) => {
            items.forEach(item => {
                item.prevPos = item.newPos || item.prevPos || item.el.getBoundingClientRect();
                item.prevIndex = item.newIndex != null ? item.newIndex : item.prevIndex != null ? item.prevIndex : Array.from(item.el.parentElement.children).indexOf(item.el);
            });

            items.forEach(this.runCallback);
            this.refreshPosition('newPos', 'newIndex');
            items.forEach(this.applyTranslation);

            // force reflow to put everything in position
            const offSet = document.body.offsetHeight;
            this.items.forEach(this.runTransition.bind(this));
        })
    }

    runCallback(item: TransitionGroupItemDirective) {
        if(item.moveCallback) {
            item.moveCallback();
        }
    }

    runTransition(item: TransitionGroupItemDirective) {
        if (!item.moved) {
            return;
        }
        let cssClass = this.class + '-move';
        if (Math.abs(item.newIndex - item.prevIndex) > 0)
            cssClass += Math.abs(item.newIndex - item.prevIndex);
        
        console.log(cssClass)
        
        let el = item.el;
        let style: any = el.style;
        el.classList.add(cssClass);
        style.transform = style.WebkitTransform = style.transitionDuration = '';
        el.addEventListener('transitionend', item.moveCallback = (e: any) => {
            if (!e || /transform$/.test(e.propertyName)) {
                el.removeEventListener('transitionend', item.moveCallback);
                item.moveCallback = null;
                el.classList.remove(cssClass);
            }
        });
    }

    refreshPosition(prop: string, index: string) {
        this.items.forEach(item => {
            item[prop] = item.el.getBoundingClientRect();
            item[index] =  Array.from(item.el.parentElement.children).indexOf(item.el);
        });
    }

    applyTranslation(item: TransitionGroupItemDirective) {
        item.moved = false;
        const dx = item.prevPos.left - item.newPos.left;
        const dy = item.prevPos.top - item.newPos.top;
        if (dx || dy) {
            item.moved = true;
            let style: any = item.el.style;
            style.transform = style.WebkitTransform = 'translate(' + dx + 'px,' + dy + 'px)';
            style.transitionDuration = '0s';
        }
    }
}