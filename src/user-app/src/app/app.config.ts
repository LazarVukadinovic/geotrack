import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideRouter, withEnabledBlockingInitialNavigation } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { catchError, firstValueFrom, of } from 'rxjs';

import { routes } from './app.routes';
import { authInterceptor } from './core/services/auth.interceptor';
import { authErrorInterceptor } from './core/services/auth-error.interceptor';
import { TokenStorageService } from './core/services/token-storage.service';
import { AuthService } from './core/services/auth.service';
import { UserService } from './core/services/user.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),

    // withEnabledBlockingInitialNavigation ensures the router does not begin
    // its first navigation until all APP_INITIALIZER factories have resolved.
    // This means no component ever renders before the startup token validation
    // and user-profile fetch below have completed.
    provideRouter(routes, withEnabledBlockingInitialNavigation()),

    // authInterceptor must be first so it attaches the Authorization header;
    // authErrorInterceptor runs next and can therefore detect that a 401
    // came back on an authenticated request (req.headers.has('Authorization')).
    provideHttpClient(withInterceptors([authInterceptor, authErrorInterceptor])),

    // On every cold start, if a token is present in storage (Remember Me or
    // current-session):
    //  1. Call /api/user/me — this validates the token AND populates UserService
    //     with the user's profile data before any route/component renders.
    //  2. If the backend rejects the token (401 — expired JWT), log out so that
    //     isLoggedIn() is false before the router starts navigation.
    provideAppInitializer(() => {
      const tokenStorage = inject(TokenStorageService);
      const authService  = inject(AuthService);
      const userService  = inject(UserService);

      if (!tokenStorage.hasToken()) return;

      return firstValueFrom(
        userService.load().pipe(
          catchError(() => {
            authService.logout();
            return of(null);
          })
        )
      );
    }),
  ]
};
