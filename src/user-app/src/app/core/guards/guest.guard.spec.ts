import { TestBed } from '@angular/core/testing';
import { Router, UrlTree, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { guestGuard } from './guest.guard';
import { AuthService } from '../services/auth.service';

describe('guestGuard', () => {
  const authService = {
    isLoggedIn: vi.fn<() => boolean>(),
  };

  beforeEach(() => {
    authService.isLoggedIn.mockReset();

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: authService },
      ],
    });
  });

  function runGuard() {
    return TestBed.runInInjectionContext(() => guestGuard({} as never, {} as never));
  }

  it('allows guests to access guest-only routes', () => {
    authService.isLoggedIn.mockReturnValue(false);

    expect(runGuard()).toBe(true);
  });

  it('redirects authenticated users to /profile', () => {
    authService.isLoggedIn.mockReturnValue(true);

    const result = runGuard() as UrlTree;

    expect(TestBed.inject(Router).serializeUrl(result)).toBe('/profile');
  });
});
