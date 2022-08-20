import {Component, OnInit} from '@angular/core';
import {DomSanitizer} from '@angular/platform-browser';
import {Location} from '@angular/common';
import {ActivatedRoute, NavigationEnd, NavigationStart, Router} from '@angular/router';
import {filter, map, Observable, share, tap} from 'rxjs';

@Component({
    selector: 'sidebar',
    templateUrl: './sidebar.component.html',
    styleUrls: ['./sidebar.component.scss']
})
export class SidebarComponent implements OnInit {

    route$: Observable<string>;

    constructor(router: Router) {
        this.route$ = router.events.pipe(filter(event => event instanceof NavigationEnd), map(e =>(<NavigationEnd>e).urlAfterRedirects), share());
    }

    ngOnInit(): void {
    }
}
