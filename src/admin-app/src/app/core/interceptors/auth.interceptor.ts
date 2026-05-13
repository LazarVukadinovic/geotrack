import { Injectable, Injector } from '@angular/core';
import { HttpErrorResponse, HttpEvent, HttpHandler, HttpInterceptor, HttpRequest } from '@angular/common/http';
import { Observable, catchError, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';

@Injectable()
export class AuthInterceptor implements HttpInterceptor {
  constructor(private readonly injector: Injector) {}

  intercept(request: HttpRequest<unknown>, next: HttpHandler): Observable<HttpEvent<unknown>> {
    if (!this.shouldHandle(request.url)) {
      return next.handle(request);
    }

    const auth = this.injector.get(AuthService);
    const token = auth.getAccessToken();
    const headers = token && this.shouldAttachToken(request.url)
      ? request.headers.set('Authorization', `Bearer ${token}`)
      : request.headers;

    const authorizedRequest = request.clone({
      headers,
      withCredentials: true,
    });

    return next.handle(authorizedRequest).pipe(
      catchError(error => {
        if (error instanceof HttpErrorResponse && error.status === 401 && !this.isAuthEndpoint(request.url)) {
          auth.clearSession();
        }

        return throwError(() => error);
      }),
    );
  }

  private shouldHandle(url: string): boolean {
    return url.startsWith('/api/');
  }

  private shouldAttachToken(url: string): boolean {
    return !url.startsWith('/api/auth/') &&
      !url.startsWith('/api/admin/auth/login') &&
      !url.startsWith('/api/admin/auth/refresh') &&
      !url.startsWith('/api/admin/auth/logout');
  }

  private isAuthEndpoint(url: string): boolean {
    return url.startsWith('/api/admin/auth/') || url.startsWith('/api/auth/');
  }
}
