import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

/**
 * Clears auth state whenever the backend returns 401 on a request that
 * carried an Authorization header (i.e. a request made while the app
 * believed the user was authenticated).
 *
 * This handles the "remembered session token expired" case: the token was
 * restored from localStorage on startup, the app considered the user
 * logged in, but the backend rejects it as expired. Calling logout() here
 * clears the token signal, which causes ProfileTabComponent (and any other
 * auth-conditional view) to flip back to the unauthenticated state via
 * Angular's signal reactivity.
 *
 * NOTE: intentionally does NOT call router.navigate() here because:
 *  - On startup the router has not yet begun initial navigation; a forced
 *    navigate() call would conflict with the URL the user actually typed.
 *  - On the Profile page, clearing isLoggedIn() is enough — ProfileTabComponent
 *    rerenders to the login form automatically via signal change detection.
 *  - On other pages, unauthenticated fallback UI is already handled per-page.
 */
export const authErrorInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);

  return next(req).pipe(
    catchError((err: unknown) => {
      if (
        err instanceof HttpErrorResponse &&
        err.status === 401 &&
        req.headers.has('Authorization')
      ) {
        authService.logout();
      }
      return throwError(() => err);
    })
  );
};
