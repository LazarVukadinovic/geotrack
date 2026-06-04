import { Injectable, Injector } from '@angular/core';
import {
  HttpContextToken,
  HttpErrorResponse,
  HttpEvent,
  HttpHandler,
  HttpInterceptor,
  HttpRequest,
} from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, catchError, of, switchMap, throwError } from 'rxjs';
import { AdminFormDraftService } from '../services/admin-form-draft.service';
import { AuthService } from '../services/auth.service';
import { isAdminAuthEndpoint, isConfiguredApiUrl, resolveApiUrl } from './api-url';

const ADMIN_AUTH_REFRESH_ATTEMPTED = new HttpContextToken<boolean>(() => false);

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
      catchError(error => this.handleAuthError(error, authorizedRequest, next, auth)),
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

  private handleAuthError(
    error: unknown,
    request: HttpRequest<unknown>,
    next: HttpHandler,
    auth: AuthService,
  ): Observable<HttpEvent<unknown>> {
    if (
      !(error instanceof HttpErrorResponse) ||
      error.status !== 401 ||
      !this.shouldRedirectOnUnauthorized(request.url) ||
      request.context.get(ADMIN_AUTH_REFRESH_ATTEMPTED)
    ) {
      return throwError(() => error);
    }

    return auth.restoreSession({ forceRefresh: true }).pipe(
      catchError(() => of(null)),
      switchMap(user => {
        const token = auth.getAccessToken();
        if (!user || !token) {
          this.redirectToLogin(auth);
          return throwError(() => error);
        }

        const retryRequest = request.clone({
          setHeaders: { Authorization: `Bearer ${token}` },
          context: request.context.set(ADMIN_AUTH_REFRESH_ATTEMPTED, true),
        });

        return next.handle(retryRequest).pipe(
          catchError(retryError => {
            if (retryError instanceof HttpErrorResponse && retryError.status === 401) {
              this.redirectToLogin(auth);
            }

            return throwError(() => retryError);
          }),
        );
      }),
    );
  }

  private redirectToLogin(auth: AuthService): void {
    const draftSaved = this.injector.get(AdminFormDraftService).saveActiveDraftOnSessionExpiry();

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
        ...(draftSaved ? { draftSaved: '1' } : {}),
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
