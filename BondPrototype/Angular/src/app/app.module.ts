import {NgModule, CUSTOM_ELEMENTS_SCHEMA} from '@angular/core';

import {AppComponent} from './app.component';
import './spectrum-imports';
import {CommonModule} from '@angular/common';
import {BrowserModule} from '@angular/platform-browser';
// @ts-ignore
import FilePondPluginGetFile from 'filepond-plugin-get-file';
import {RouterModule, Routes} from '@angular/router';
import {HttpClientModule, HttpClient} from '@angular/common/http';
import {ENVIRONMENT_PROVIDER} from '../core/services/enviroment.provider';
import {environment} from '../environments/environment';
import {ActorsComponent} from './actors/actors.component';
import {DirectingComponent} from './directing/directing.component';
import {MoviesComponent} from './movies/movies.component';
import {ActionDirective} from './common/directives/action.directive';
import {SidebarComponent} from './sidebar/sidebar.component';
import {SvgIconComponent} from './common/components/svg-icon/svg-icon.component';
import {SafeHtmlPipe} from './common/pipes/safe-html.pipe';
import {MovieListItemComponent} from './movies/components/movie-list-item/movie-list-item.component';
import {SafeUrlPipe} from './common/pipes/safe-url.pipe';
import {ActorShortProfileComponent} from './actors/components/actor-short-profile/actor-short-profile.component';
import {ConfigurationComponent} from './configuration/configuration.component';
import {ActorFullProfileComponent} from './actors/components/actor-full-profile/actor-full-profile.component';
import {ElipsisHoverComponent, ElipsisOnHoverDirective} from './actors/directives/elipsis-on-hover.directive';
import {ReactiveComponentModule} from '@ngrx/component';
import {BenchmarkComponent} from './benchmark/benchmark.component'
import {FormsModule, ReactiveFormsModule} from '@angular/forms';
import {TooltipDirective, TooltipWrapperComponent} from './common/directives/tooltip.directive';
import {EmptyIllustrationComponent} from './common/components/empty-illustration/empty-illustration.component'
import {DragDropModule} from '@angular/cdk/drag-drop';
import {ActorsResolverService} from './actors/services/actors-resolver.service';
import {MoviesResolverService} from './movies/services/movies-resolver.service';
import {TranslateDemoComponent} from './translate-demo/translate-demo.component';;
import { TranslatedContentComponent } from './translate-demo/components/translated-content/translated-content.component'

let routes: Routes = [
    {path: 'movies', component: MoviesComponent, resolve: {movies: MoviesResolverService}},
    {path: 'actors', component: ActorsComponent, resolve: {actors: ActorsResolverService}},
    {path: 'directing', component: DirectingComponent},
    {path: 'settings', component: ConfigurationComponent},
    {path: 'benchmark', component: BenchmarkComponent},
    {path: 'translate-demo', component: TranslateDemoComponent},
    {path: 'translate-content-fullscreen', component: TranslatedContentComponent},
    {path: '', redirectTo: 'movies', pathMatch: 'full'}
];

@NgModule({
    declarations: [
        AppComponent,
        MoviesComponent,
        ActionDirective,
        ActorsComponent,
        DirectingComponent,
        SidebarComponent,
        SvgIconComponent,
        SafeHtmlPipe,
        MovieListItemComponent,
        SafeUrlPipe,
        ActorShortProfileComponent,
        ConfigurationComponent,
        ActorFullProfileComponent,
        ElipsisOnHoverDirective,
        ElipsisHoverComponent,
        BenchmarkComponent,
        TooltipDirective,
        TooltipWrapperComponent,
        EmptyIllustrationComponent,
        TranslateDemoComponent,
        TranslatedContentComponent
    ],
    imports: [
        BrowserModule,
        CommonModule,
        HttpClientModule,
        RouterModule.forRoot(routes),
        ReactiveComponentModule,
        FormsModule,
        ReactiveFormsModule,
        DragDropModule
    ],
    providers: [
        {
            provide: ENVIRONMENT_PROVIDER,
            useValue: environment
        }
    ], bootstrap: [AppComponent],
    schemas: [CUSTOM_ELEMENTS_SCHEMA]
})
export class AppModule {
}
