import {ChangeDetectionStrategy, Component, OnInit} from '@angular/core';
import {NavigationEnd, Router} from '@angular/router';
import {filter, map, Observable, share} from 'rxjs';

@Component({
    selector: 'sidebar',
    templateUrl: './sidebar.component.html',
    styleUrls: ['./sidebar.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class SidebarComponent implements OnInit {

    route$: Observable<string>;

    constructor(router: Router) {
        this.route$ = router.events.pipe(filter(event => event instanceof NavigationEnd), map(e =>(<NavigationEnd>e).urlAfterRedirects), share());
    }

    ngOnInit(): void {
    }
}
