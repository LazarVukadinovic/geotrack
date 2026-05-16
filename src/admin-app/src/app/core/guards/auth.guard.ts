import { inject } from '@angular/core';
import { CanActivateFn, Router, RouterStateSnapshot } from '@angular/router';
import { map } from 'rxjs';
import { AuthService } from '../services/auth.service';

export const authGuard: CanActivateFn = (_route, state) => {
  const auth   = inject(AuthService);
  const router = inject(Router);

  if (auth.isLoggedIn()) return true;

  return auth.restoreSession().pipe(
    map(user => user ? true : loginTree(router, state)),
  );
};

//ADMINISTRATOR GUARD
export const administratorGuard: CanActivateFn = (_route, state) => {
  const auth   = inject(AuthService);
  const router = inject(Router);

  if (auth.isAdministrator()) return true;

  return auth.restoreSession().pipe(
    map(user => {
      if (!user) return loginTree(router, state);
      return auth.isAdministrator() ? true : router.parseUrl(auth.getHomeRoute());
    }),
  );
};

//NATIONAL MANAGER GUARD
export const nationalManagerGuard: CanActivateFn = (_route, state) => {
  const auth   = inject(AuthService);
  const router = inject(Router);

  if (auth.isNationalManager()) return true;

  return auth.restoreSession().pipe(
    map(user => {
      if (!user) return loginTree(router, state);
      return auth.isNationalManager() ? true : router.parseUrl(auth.getHomeRoute());
    }),
  );
};

//REGIONAL MANAGER GUARD
 
export const regionalManagerGuard: CanActivateFn = (_route, state) => {
  const auth   = inject(AuthService);
  const router = inject(Router);

  if (auth.isRegionalManager()) return true;

  return auth.restoreSession().pipe(
    map(user => {
      if (!user) return loginTree(router, state);
      return auth.isRegionalManager() ? true : router.parseUrl(auth.getHomeRoute());
    }),
  );
};

function loginTree(router: Router, state: RouterStateSnapshot) {
  const returnUrl = safeReturnUrl(state.url);
  return returnUrl
    ? router.createUrlTree(['/login'], { queryParams: { returnUrl } })
    : router.parseUrl('/login');
}

function safeReturnUrl(value: string): string | null {
  if (!value || !value.startsWith('/') || value.startsWith('//') || value.startsWith('/login')) {
    return null;
  }

  return value;
}
