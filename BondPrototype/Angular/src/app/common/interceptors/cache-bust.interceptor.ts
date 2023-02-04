import { Injectable } from '@angular/core';
import {
    HttpRequest,
    HttpHandler,
    HttpEvent,
    HttpInterceptor, HttpContextToken, HttpContext
} from '@angular/common/http';
import {Observable, Subject} from 'rxjs';

@Injectable()
export class CacheBustInterceptor implements HttpInterceptor {
    
    intercept(request: HttpRequest<unknown>, next: HttpHandler): Observable<HttpEvent<unknown>> {
        
        if (request.method === 'GET')
            request = request.clone({headers: request.headers.set('Cache-Control', ['no-cache', 'no-store'])});
        
        return next.handle(request);
        
        // return next.handle(request).pipe(catchError((error: HttpError) => {
        //
        //     console.log(request)
        //
        //     if (error.status === 401) {
        //         this.router.navigate(['prijava']);
        //         return NEVER;
        //     }
        //
        //     let toastMessage = `${request.url}: ${error.error?.title ?? 'Greška na serveru.'}`;
        //     notificationSubject.next(toastMessage)
        //     console.log(error);
        //
        //     return request.context.get(IGNORE_EXCEPTION) ? NEVER : throwError(error);
        // }));
    }
}