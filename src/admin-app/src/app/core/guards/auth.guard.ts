import { inject } from '@angular/core';
import { CanActivateFn, Router, RouterStateSnapshot } from '@angular/router';
import { map } from 'rxjs';
import { AuthService } from '../services/auth.service';

export const loginRedirectGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.adminAccessDenied()) {
    return router.createUrlTree(['/no-admin-access']);
  }

  if (auth.isLoggedIn() && auth.hasValidAccessToken()) {
    return router.parseUrl(auth.getHomeRoute());
  }

  return true;
};

export const authGuard: CanActivateFn = (_route, state) => {
  const auth   = inject(AuthService);
  const router = inject(Router);

  if (auth.isLoggedIn() && auth.hasValidAccessToken()) return true;
  if (auth.isLoggedIn()) {
    return auth.restoreSession({ forceRefresh: true }).pipe(
      map(user => user ? true : deniedOrLoginTree(auth, router, state, true)),
    );
  }

  return auth.restoreSession().pipe(
    map(user => user ? true : deniedOrLoginTree(auth, router, state)),
  );
};

//ADMINISTRATOR GUARD
export const administratorGuard: CanActivateFn = (_route, state) => {
  const auth   = inject(AuthService);
  const router = inject(Router);

  if (auth.isAdministrator() && auth.hasValidAccessToken()) return true;
  if (auth.getUser() && !auth.hasValidAccessToken()) {
    return auth.restoreSession({ forceRefresh: true }).pipe(
      map(user => {
        if (!user) return deniedOrLoginTree(auth, router, state, true);
        return auth.isAdministrator() ? true : router.parseUrl(auth.getHomeRoute());
      }),
    );
  }

  return auth.restoreSession().pipe(
    map(user => {
      if (!user) return deniedOrLoginTree(auth, router, state);
      return auth.isAdministrator() ? true : router.parseUrl(auth.getHomeRoute());
    }),
  );
};

export const contentModeratorGuard: CanActivateFn = (_route, state) => {
  const auth   = inject(AuthService);
  const router = inject(Router);

  if (auth.isContentModerator() && auth.hasValidAccessToken()) return true;
  if (auth.getUser() && !auth.hasValidAccessToken()) {
    return auth.restoreSession({ forceRefresh: true }).pipe(
      map(user => {
        if (!user) return deniedOrLoginTree(auth, router, state, true);
        return auth.isContentModerator() ? true : router.parseUrl(auth.getHomeRoute());
      }),
    );
  }

  return auth.restoreSession().pipe(
    map(user => {
      if (!user) return deniedOrLoginTree(auth, router, state);
      return auth.isContentModerator() ? true : router.parseUrl(auth.getHomeRoute());
    }),
  );
};

export const territoryManagerGuard: CanActivateFn = (_route, state) => {
  const auth   = inject(AuthService);
  const router = inject(Router);

  if (auth.canAccessTerritoryAdmin() && auth.hasValidAccessToken()) return true;
  if (auth.getUser() && !auth.hasValidAccessToken()) {
    return auth.restoreSession({ forceRefresh: true }).pipe(
      map(user => {
        if (!user) return deniedOrLoginTree(auth, router, state, true);
        return auth.canAccessTerritoryAdmin() ? true : router.parseUrl(auth.getHomeRoute());
      }),
    );
  }

  return auth.restoreSession().pipe(
    map(user => {
      if (!user) return deniedOrLoginTree(auth, router, state);
      return auth.canAccessTerritoryAdmin() ? true : router.parseUrl(auth.getHomeRoute());
    }),
  );
};

export const nationalManagerGuard = territoryManagerGuard;

export const regionalOrTerritoryManagerGuard: CanActivateFn = (_route, state) => {
  const auth   = inject(AuthService);
  const router = inject(Router);

  const canAccess = () => auth.isRegionalManager() || auth.canAccessTerritoryAdmin();
  if (canAccess() && auth.hasValidAccessToken()) return true;
  if (auth.getUser() && !auth.hasValidAccessToken()) {
    return auth.restoreSession({ forceRefresh: true }).pipe(
      map(user => {
        if (!user) return deniedOrLoginTree(auth, router, state, true);
        return canAccess() ? true : router.parseUrl(auth.getHomeRoute());
      }),
    );
  }

  return auth.restoreSession().pipe(
    map(user => {
      if (!user) return deniedOrLoginTree(auth, router, state);
      return canAccess() ? true : router.parseUrl(auth.getHomeRoute());
    }),
  );
};

//REGIONAL MANAGER GUARD
 
export const regionalManagerGuard: CanActivateFn = (_route, state) => {
  const auth   = inject(AuthService);
  const router = inject(Router);

  if (auth.isRegionalManager() && auth.hasValidAccessToken()) return true;
  if (auth.getUser() && !auth.hasValidAccessToken()) {
    return auth.restoreSession({ forceRefresh: true }).pipe(
      map(user => {
        if (!user) return deniedOrLoginTree(auth, router, state, true);
        return auth.isRegionalManager() ? true : router.parseUrl(auth.getHomeRoute());
      }),
    );
  }

  return auth.restoreSession().pipe(
    map(user => {
      if (!user) return deniedOrLoginTree(auth, router, state);
      return auth.isRegionalManager() ? true : router.parseUrl(auth.getHomeRoute());
    }),
  );
};

function deniedOrLoginTree(auth: AuthService, router: Router, state: RouterStateSnapshot, sessionExpired = false) {
  return auth.adminAccessDenied()
    ? router.createUrlTree(['/no-admin-access'])
    : loginTree(router, state, sessionExpired);
}

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
