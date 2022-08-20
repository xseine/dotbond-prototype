import {Component, OnInit} from '@angular/core';
import {IComponentHeaderText} from '../app.component';

@Component({
    selector: 'app-settings',
    template: `
        <p class="spectrum-Body spectrum-Body--sizeM" style="margin-top: 1em"><em>Planned feature to generate the configuration file, when one becomes required.</em></p>
    `,
    styleUrls: ['./configuration.component.scss']
})
export class ConfigurationComponent implements OnInit, IComponentHeaderText {
    readonly headerText = "Configurations";
    
    constructor() {
    }

    ngOnInit(): void {
    }

}
