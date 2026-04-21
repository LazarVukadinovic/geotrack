import { TestBed } from '@angular/core/testing';
import { Router, UrlTree, provideRouter } from '@angular/router';
import { firstValueFrom, of } from 'rxjs';
import { vi } from 'vitest';
import { interestsOnboardingGuard } from './interests-onboarding.guard';
import { AuthService } from '../services/auth.service';
import { UserService } from '../services/user.service';

describe('interestsOnboardingGuard', () => {
  const authService = {
    isLoggedIn: vi.fn<() => boolean>(),
  };

  const userService = {
    profile: vi.fn<() => { needsInterestsOnboarding: boolean } | null>(),
    ensureLoaded: vi.fn(() => of({ needsInterestsOnboarding: false })),
  };

  beforeEach(() => {
    authService.isLoggedIn.mockReset();
    userService.profile.mockReset();
    userService.ensureLoaded.mockReset();

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: authService },
        { provide: UserService, useValue: userService },
      ],
    });
  });

  function runGuard() {
    return TestBed.runInInjectionContext(() => interestsOnboardingGuard({} as never, {} as never));
  }

  it('redirects guests to /register', () => {
    authService.isLoggedIn.mockReturnValue(false);
    userService.profile.mockReturnValue(null);

    const result = runGuard() as UrlTree;

    expect(TestBed.inject(Router).serializeUrl(result)).toBe('/register');
  });

  it('allows authenticated users who still need interests onboarding', () => {
    authService.isLoggedIn.mockReturnValue(true);
    userService.profile.mockReturnValue({ needsInterestsOnboarding: true });

    expect(runGuard()).toBe(true);
    expect(userService.ensureLoaded).not.toHaveBeenCalled();
  });

  it('redirects authenticated users who no longer have onboarding access', async () => {
    authService.isLoggedIn.mockReturnValue(true);
    userService.profile.mockReturnValue(null);
    userService.ensureLoaded.mockReturnValue(of({ needsInterestsOnboarding: false }));

    const result = await firstValueFrom(runGuard() as any);

    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/profile');
  });
});
