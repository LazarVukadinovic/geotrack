import { inject } from '@angular/core';
import { CanActivateFn, Router, RouterStateSnapshot } from '@angular/router';
import { map } from 'rxjs';
import { AuthService } from '../services/auth.service';

export const authGuard: CanActivateFn = (_route, state) => {
  const auth   = inject(AuthService);
  const router = inject(Router);

  if (auth.isLoggedIn() && auth.hasValidAccessToken()) return true;
  if (auth.isLoggedIn()) {
    auth.clearSession();
    return loginTree(router, state, true);
  }

  return auth.restoreSession().pipe(
    map(user => user ? true : loginTree(router, state)),
  );
};

//ADMINISTRATOR GUARD
export const administratorGuard: CanActivateFn = (_route, state) => {
  const auth   = inject(AuthService);
  const router = inject(Router);

  if (auth.isAdministrator() && auth.hasValidAccessToken()) return true;
  if (auth.getUser() && !auth.hasValidAccessToken()) {
    auth.clearSession();
    return loginTree(router, state, true);
  }

  return auth.restoreSession().pipe(
    map(user => {
      if (!user) return loginTree(router, state);
      return auth.isAdministrator() ? true : router.parseUrl(auth.getHomeRoute());
    }),
  );
};

export const contentModeratorGuard: CanActivateFn = (_route, state) => {
  const auth   = inject(AuthService);
  const router = inject(Router);

  if (auth.isContentModerator() && auth.hasValidAccessToken()) return true;
  if (auth.getUser() && !auth.hasValidAccessToken()) {
    auth.clearSession();
    return loginTree(router, state, true);
  }

  return auth.restoreSession().pipe(
    map(user => {
      if (!user) return loginTree(router, state);
      return auth.isContentModerator() ? true : router.parseUrl(auth.getHomeRoute());
    }),
  );
};

export const territoryManagerGuard: CanActivateFn = (_route, state) => {
  const auth   = inject(AuthService);
  const router = inject(Router);

  if (auth.canAccessTerritoryAdmin() && auth.hasValidAccessToken()) return true;
  if (auth.getUser() && !auth.hasValidAccessToken()) {
    auth.clearSession();
    return loginTree(router, state, true);
  }

  return auth.restoreSession().pipe(
    map(user => {
      if (!user) return loginTree(router, state);
      return auth.canAccessTerritoryAdmin() ? true : router.parseUrl(auth.getHomeRoute());
    }),
  );
};

export const nationalManagerGuard = territoryManagerGuard;

//REGIONAL MANAGER GUARD
 
export const regionalManagerGuard: CanActivateFn = (_route, state) => {
  const auth   = inject(AuthService);
  const router = inject(Router);

  if (auth.isRegionalManager() && auth.hasValidAccessToken()) return true;
  if (auth.getUser() && !auth.hasValidAccessToken()) {
    auth.clearSession();
    return loginTree(router, state, true);
  }

  return auth.restoreSession().pipe(
    map(user => {
      if (!user) return loginTree(router, state);
      return auth.isRegionalManager() ? true : router.parseUrl(auth.getHomeRoute());
    }),
  );
};

function loginTree(router: Router, state: RouterStateSnapshot, sessionExpired = false) {
  const returnUrl = safeReturnUrl(state.url);
  return returnUrl
    ? router.createUrlTree(['/login'], { queryParams: { returnUrl, ...(sessionExpired ? { sessionExpired: '1' } : {}) } })
    : router.createUrlTree(['/login'], { queryParams: sessionExpired ? { sessionExpired: '1' } : undefined });
}

function safeReturnUrl(value: string): string | null {
  if (!value || !value.startsWith('/') || value.startsWith('//') || value.startsWith('/login')) {
    return null;
  }

  return value;
}
