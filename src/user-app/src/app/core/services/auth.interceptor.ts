import { HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';
import { TokenStorageService } from './token-storage.service';
import { TokenRefreshService } from './token-refresh.service';
import { isApiAuthEndpoint, isConfiguredApiUrl } from './api-url';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const tokenStorage    = inject(TokenStorageService);
  const tokenRefreshSvc = inject(TokenRefreshService);
  const authService     = inject(AuthService);

  if (!isOwnApiRequest(req.url) || isAuthEndpoint(req.url)) {
    return next(req);
  }

  if (!tokenStorage.hasSession()) {
    return next(req);
  }

  const accessToken = tokenStorage.getAccessToken();
  if (!accessToken || tokenStorage.isAccessTokenNearExpiry()) {
    return tokenRefreshSvc.refresh().pipe(
      switchMap(newAccessToken =>
        next(attachBearer(req, newAccessToken))
      ),
      catchError((err) => {
        authService.clearSession();
        return throwError(() => err);
      }),
    );
  }

  return next(attachBearer(req, accessToken));
};

function attachBearer(req: HttpRequest<unknown>, token: string): HttpRequest<unknown> {
  return req.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
}

function isAuthEndpoint(url: string): boolean {
  return isApiAuthEndpoint(url);
}

function isOwnApiRequest(url: string): boolean {
  return isConfiguredApiUrl(url);
}
