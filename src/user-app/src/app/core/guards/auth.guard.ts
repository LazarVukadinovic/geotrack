import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

/**
 * Protects routes that require an authenticated session.
 * If the user has no valid token (not logged in, or token was cleared after
 * a startup 401 by provideAppInitializer), they are redirected to /profile
 * where the login form is shown.
 */
export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (!auth.isLoggedIn()) {
    return router.createUrlTree(['/profile']);
  }

  return true;
};
