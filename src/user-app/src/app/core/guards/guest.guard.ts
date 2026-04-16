import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

/**
 * Blocks routes that should only be accessible to guests (unauthenticated users).
 * If a logged-in user tries to access a guarded route, they are redirected to /explore.
 * Token state is read synchronously from storage, so this works correctly for
 * remembered sessions restored on app startup.
 */
export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isLoggedIn()) {
    return router.createUrlTree(['/explore']);
  }

  return true;
};
