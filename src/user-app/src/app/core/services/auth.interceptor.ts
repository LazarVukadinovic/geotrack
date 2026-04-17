import { HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { switchMap, catchError } from 'rxjs';
import { TokenStorageService } from './token-storage.service';
import { TokenRefreshService } from './token-refresh.service';

/**
 * Attaches the access token to every outgoing request.
 *
 * Proactive refresh: if the stored access token is within 2 minutes of
 * expiring (or already expired) and a refresh token is available, we
 * transparently refresh before forwarding the request.  This prevents
 * the user from ever experiencing a mid-request 401 due to token expiry.
 *
 * Auth/refresh endpoints themselves are skipped to avoid circular calls.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const tokenStorage    = inject(TokenStorageService);
  const tokenRefreshSvc = inject(TokenRefreshService);

  // Never touch auth headers on the auth endpoints themselves.
  if (isAuthEndpoint(req.url)) {
    return next(req);
  }

  const refreshToken = tokenStorage.getRefreshToken();

  // No session at all — forward without auth header.
  if (!refreshToken) {
    return next(req);
  }

  const accessToken = tokenStorage.getAccessToken();

  // Proactively refresh when the access token is absent or near expiry.
  if (!accessToken || tokenStorage.isAccessTokenNearExpiry()) {
    return tokenRefreshSvc.refresh().pipe(
      switchMap(newAccessToken =>
        next(attachBearer(req, newAccessToken))
      ),
      catchError(() =>
        // Refresh failed — still forward without auth so the error interceptor
        // can handle it (it will call clearSession() on 401).
        next(req)
      ),
    );
  }

  return next(attachBearer(req, accessToken));
};

// ── Helpers ───────────────────────────────────────────────────────────────────

function attachBearer(req: HttpRequest<unknown>, token: string): HttpRequest<unknown> {
  return req.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
}

function isAuthEndpoint(url: string): boolean {
  return url.includes('/api/auth/');
}
