import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { AuthService } from '../services/auth.service';
import { UserService } from '../services/user.service';

/**
 * Protects the post-registration interests step.
 * Access is granted only while the authenticated user's backend state says
 * they still need to complete interests onboarding.
 */
export const interestsOnboardingGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const userService = inject(UserService);

  if (!auth.isLoggedIn()) {
    return router.createUrlTree(['/register']);
  }

  const profile = userService.profile();
  if (profile) {
    return profile.needsInterestsOnboarding
      ? true
      : router.createUrlTree(['/profile']);
  }

  return userService.ensureLoaded().pipe(
    map(loadedProfile =>
      loadedProfile.needsInterestsOnboarding
        ? true
        : router.createUrlTree(['/profile'])
    ),
    catchError(() => of(router.createUrlTree(['/profile']))),
  );
};
