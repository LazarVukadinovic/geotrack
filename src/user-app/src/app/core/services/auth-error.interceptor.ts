import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';
import { TokenRefreshService } from './token-refresh.service';

/**
 * Handles 401 responses on authenticated requests.
 *
 * Flow:
 *  1. Request returns 401 with an Authorization header present.
 *  2. We attempt a silent token refresh (unless this is already an auth endpoint).
 *  3. On refresh success → retry the original request with the new access token.
 *  4. On refresh failure → call authService.clearSession() so isLoggedIn() drops
 *     to false, which the AppShell effect picks up and redirects protected pages.
 *
 * This catches the "access token expired mid-session" case that the proactive
 * refresh in authInterceptor might miss in race conditions.
 */
export const authErrorInterceptor: HttpInterceptorFn = (req, next) => {
  const authService     = inject(AuthService);
  const tokenRefreshSvc = inject(TokenRefreshService);

  return next(req).pipe(
    catchError((err: unknown) => {
      if (
        err instanceof HttpErrorResponse &&
        err.status === 401 &&
        req.headers.has('Authorization') &&
        !isAuthEndpoint(req.url)
      ) {
        return tokenRefreshSvc.refresh().pipe(
          switchMap(newToken =>
            next(req.clone({ setHeaders: { Authorization: `Bearer ${newToken}` } }))
          ),
          catchError(() => {
            // Refresh failed — session is unrecoverable.
            // Clear local state; the AppShell effect will redirect if needed.
            authService.clearSession();
            return throwError(() => err);
          }),
        );
      }

      return throwError(() => err);
    }),
  );
};

function isAuthEndpoint(url: string): boolean {
  return url.includes('/api/auth/');
}
