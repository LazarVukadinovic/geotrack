import { Injectable, Injector } from '@angular/core';
import { HttpErrorResponse, HttpEvent, HttpHandler, HttpInterceptor, HttpRequest } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, catchError, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';
import { isAdminAuthEndpoint, isConfiguredApiUrl, resolveApiUrl } from './api-url';

@Injectable()
export class AuthInterceptor implements HttpInterceptor {
  private redirectingToLogin = false;

  constructor(private readonly injector: Injector) {}

  intercept(request: HttpRequest<unknown>, next: HttpHandler): Observable<HttpEvent<unknown>> {
    const url = resolveApiUrl(request.url);
    if (!this.shouldHandle(url)) {
      return next.handle(request);
    }

    const auth = this.injector.get(AuthService);
    const token = auth.getAccessToken();
    const headers = token && this.shouldAttachToken(url)
      ? request.headers.set('Authorization', `Bearer ${token}`)
      : request.headers;

    const authorizedRequest = request.clone({
      url,
      headers,
      withCredentials: true,
    });

    return next.handle(authorizedRequest).pipe(
      catchError(error => {
        if (error instanceof HttpErrorResponse && error.status === 401 && this.shouldRedirectOnUnauthorized(url)) {
          this.redirectToLogin(auth);
        }

        return throwError(() => error);
      }),
    );
  }

  private shouldHandle(url: string): boolean {
    return isConfiguredApiUrl(url);
  }

  private shouldAttachToken(url: string): boolean {
    return !isAdminAuthEndpoint(url);
  }

  private shouldRedirectOnUnauthorized(url: string): boolean {
    return !isAdminAuthEndpoint(url);
  }

  private redirectToLogin(auth: AuthService): void {
    auth.clearSession();

    if (this.redirectingToLogin) {
      return;
    }

    const router = this.injector.get(Router);
    const returnUrl = this.safeReturnUrl(router.url);
    if (!returnUrl && router.url.startsWith('/login')) {
      return;
    }

    this.redirectingToLogin = true;
    void router.navigate(['/login'], {
      queryParams: {
        sessionExpired: '1',
        ...(returnUrl ? { returnUrl } : {}),
      },
      replaceUrl: true,
    }).finally(() => {
      this.redirectingToLogin = false;
    });
  }

  private safeReturnUrl(value: string): string | null {
    if (!value || !value.startsWith('/') || value.startsWith('//') || value.startsWith('/login')) {
      return null;
    }

    return value;
  }
}
