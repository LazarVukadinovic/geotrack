import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideRouter, withEnabledBlockingInitialNavigation } from '@angular/router';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { catchError, firstValueFrom, of } from 'rxjs';

import { routes } from './app.routes';
import { authInterceptor } from './core/services/auth.interceptor';
import { authErrorInterceptor } from './core/services/auth-error.interceptor';
import { TokenStorageService } from './core/services/token-storage.service';
import { AuthService } from './core/services/auth.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),

    // withEnabledBlockingInitialNavigation ensures the router does not begin
    // its first navigation until all APP_INITIALIZER factories have resolved.
    // This means ProfileTabComponent is never rendered with a stale "logged in"
    // state from an expired token — by the time the route activates, the
    // startup token validation below has already corrected isLoggedIn.
    provideRouter(routes, withEnabledBlockingInitialNavigation()),

    // authInterceptor must be first so it attaches the Authorization header;
    // authErrorInterceptor runs next and can therefore detect that a 401
    // came back on an authenticated request (req.headers.has('Authorization')).
    provideHttpClient(withInterceptors([authInterceptor, authErrorInterceptor])),

    // On every cold start, if a token is present in storage (Remember Me or
    // current-session), verify it against the backend before any route renders.
    // If the backend rejects it (401 — e.g. expired JWT), log the user out so
    // that isLoggedIn() is false before the router starts navigation.
    provideAppInitializer(() => {
      const tokenStorage = inject(TokenStorageService);
      const authService  = inject(AuthService);
      const http         = inject(HttpClient);

      if (!tokenStorage.hasToken()) return;

      return firstValueFrom(
        http.get<unknown>('/api/user/me').pipe(
          catchError(() => {
            authService.logout();
            return of(null);
          })
        )
      );
    }),
  ]
};
