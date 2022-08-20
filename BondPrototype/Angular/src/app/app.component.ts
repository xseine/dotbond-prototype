import {Component, OnInit} from '@angular/core';
import {interval} from "rxjs";
import {HttpClient} from "@angular/common/http";




@Component({
    selector: 'app-root',
    templateUrl: './app.component.html',
    styleUrls: ['./app.component.scss']
})
export class AppComponent implements OnInit {

    activatedComponentHeaderText: string;
    isFullscreen = false;

    constructor() {
        // prevents browser error messages when [routerLink] sets tabIndex of unconnected sp-sidenav-item
        window.onerror = (message, _, __) => message === "Uncaught TypeError: Cannot read properties of null (reading 'querySelector')" 
            || message === "TypeError: this.shadowRoot is null";
    }

    ngOnInit(): void {
    }
    
    afterSubmit(result: any): void {
        console.log(result);
    }
    
    onRouteActivate(component: any): void {
        if (componentWantsFullscreen(component)) {
            this.isFullscreen = component.enableFullscreen;
            return;
        }

        if (!componentHasDefinedHeaderText(component)) {
            this.activatedComponentHeaderText = 'ERROR';    
            console.error('Component in this router-outlet must implement IComponentPageHeader, i.e. provide header text for the top of the page.');
        }
        
        this.activatedComponentHeaderText = (component as IComponentHeaderText).headerText;
    }

}

export interface IComponentHeaderText {
    readonly headerText: string;
}

export interface IWantsFullscreen {
    readonly enableFullscreen: boolean;
}

/**
 * Header text is what is shown on top of the page and looks the same for all components.
 * @param component
 */
function componentHasDefinedHeaderText(component: any): component is IComponentHeaderText {
    return (component as IComponentHeaderText).headerText !== undefined;
}

function componentWantsFullscreen(component: any): component is IWantsFullscreen {
    return (component as IWantsFullscreen).enableFullscreen !== undefined;
}