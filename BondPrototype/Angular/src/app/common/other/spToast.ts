import {fromEvent, merge, Observable} from 'rxjs';
import {map, tap} from 'rxjs/operators';
import {Toast} from '@spectrum-web-components/bundle';
import {htmlToElement} from './misc';


type variant = 'positive' | 'negative' | 'info' | 'error' | 'warning';
type toastFunction = ((message: string) => void) & (<T extends string | null>(message: string, actionLabel?: T, timeout?: number) => [Observable<'close' | T>, Toast]);

type ISpToast = {
    [K in variant]: toastFunction
}

export const spToast: ISpToast = {
    positive: displayToast('positive'),
    negative: displayToast('negative'),
    info: displayToast('info'),
    error: displayToast('error'),
    warning: displayToast('warning')
};

function displayToast(variant: variant) {

    return ((message: string, actionLabel?: string, timeout = 6000) => {

        let element = document.createElement('sp-toast');
        element.open = true;
        element.timeout = timeout;
        element.variant = variant;

        element.innerHTML = actionLabel == null
            ? message
            : `message
            <sp-button slot="action" variant="overBackground" quiet>
                ${actionLabel}
            </sp-button>`;

        if (document.body.querySelector('sp-theme .toast-container') == null)
            document.body.querySelector('sp-theme').appendChild(htmlToElement(`<div class="toast-container"></div>`, document));

        let container = document.body.querySelector('sp-theme .toast-container');

        container.appendChild(element);

        fromEvent(element, 'close').subscribe(_ => element.remove());
        
        if (actionLabel != null)
            return [merge(
                fromEvent(element, 'close'),
                fromEvent(element.querySelector('[slot=action]'), 'click')
            ).pipe(map(event => event.type == 'close' ? 'close' : actionLabel)), element];
        else 
            return null;
        
    }) as toastFunction;
}