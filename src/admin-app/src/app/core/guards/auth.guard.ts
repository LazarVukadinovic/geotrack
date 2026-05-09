import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

export const authGuard: CanActivateFn = () => {
  const auth   = inject(AuthService);
  const router = inject(Router);

  if (auth.isLoggedIn()) return true;

  return router.parseUrl('/login');
};

//NATIONAL MANAGER GUARD
export const nationalManagerGuard: CanActivateFn = () => {
  const auth   = inject(AuthService);
  const router = inject(Router);

  if (auth.isNationalManager()) return true;

  return router.parseUrl(auth.getHomeRoute());
};

//REGIONAL MANAGER GUARD
 
export const regionalManagerGuard: CanActivateFn = () => {
  const auth   = inject(AuthService);
  const router = inject(Router);

  if (auth.isRegionalManager()) return true;

  return router.parseUrl(auth.getHomeRoute());
};