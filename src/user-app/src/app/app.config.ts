import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideRouter, withEnabledBlockingInitialNavigation } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { catchError, firstValueFrom, of, switchMap } from 'rxjs';

import { routes } from './app.routes';
import { authInterceptor } from './core/services/auth.interceptor';
import { authErrorInterceptor } from './core/services/auth-error.interceptor';
import { TokenStorageService } from './core/services/token-storage.service';
import { TokenRefreshService } from './core/services/token-refresh.service';
import { AuthService } from './core/services/auth.service';
import { UserService } from './core/services/user.service';
import { AnalyticsService } from './core/services/analytics.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),

    // withEnabledBlockingInitialNavigation ensures no component renders before
    // the startup initializer below has resolved.  That means isLoggedIn() and
    // the user profile are both settled before any route guard or page runs.
    provideRouter(routes, withEnabledBlockingInitialNavigation()),

    // authInterceptor first (attaches Authorization header / proactive refresh);
    // authErrorInterceptor second (retries with a fresh token on 401).
    provideHttpClient(withInterceptors([authInterceptor, authErrorInterceptor])),

    // ── Startup session restore ─────────────────────────────────────────────
    // On every cold start:
    //  1. If a refresh token is present in storage (Remember Me or session),
    //     exchange it for a new access token via POST /api/auth/refresh.
    //  2. On success, load the user profile so every page has fullName/email
    //     ready on first render without an additional round-trip.
    //  3. On refresh failure (token expired / revoked), clear all local state
    //     so isLoggedIn() is false before the router begins navigation.
    provideAppInitializer(() => {
      const tokenStorage    = inject(TokenStorageService);
      const tokenRefreshSvc = inject(TokenRefreshService);
      const authService     = inject(AuthService);
      const userService     = inject(UserService);

      if (!tokenStorage.hasSession()) return;

      return firstValueFrom(
        tokenRefreshSvc.refresh().pipe(
          catchError(() => {
            authService.clearSession();
            return of(null);
          }),
          switchMap((token) => {
            if (!token) return of(null);
            return userService.load().pipe(
              catchError(() => of(null)),
            );
          }),
        )
      );
    }),

    // Eagerly construct AnalyticsService so its visibilitychange/beforeunload
    // listeners and 5-second flush timer are running before the first
    // NavigationEnd fires for the page_view event in AppShellComponent.
    provideAppInitializer(() => {
      inject(AnalyticsService);
    }),
  ]
};
