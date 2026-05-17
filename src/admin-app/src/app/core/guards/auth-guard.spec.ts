import { TestBed } from '@angular/core/testing';
import { CanActivateFn, UrlTree } from '@angular/router';
import { provideRouter, Router } from '@angular/router';
import { lastValueFrom, Observable, of } from 'rxjs';
import { vi } from 'vitest';

import { authGuard, regionalManagerGuard } from './auth.guard';
import { AuthService } from '../services/auth.service';

describe('authGuard', () => {
  const executeGuard: CanActivateFn = (...guardParameters) =>
    TestBed.runInInjectionContext(() => authGuard(...guardParameters));
  const executeRegionalGuard: CanActivateFn = (...guardParameters) =>
    TestBed.runInInjectionContext(() => regionalManagerGuard(...guardParameters));

  const authService = {
    isLoggedIn: vi.fn(),
    hasValidAccessToken: vi.fn(),
    getUser: vi.fn(),
    clearSession: vi.fn(),
    isRegionalManager: vi.fn(),
    isNationalManager: vi.fn(),
    restoreSession: vi.fn(),
    getHomeRoute: vi.fn(),
  };

  beforeEach(() => {
    vi.clearAllMocks();
    authService.isLoggedIn.mockReturnValue(false);
    authService.hasValidAccessToken.mockReturnValue(false);
    authService.getUser.mockReturnValue(null);
    authService.clearSession.mockReturnValue(undefined);
    authService.isRegionalManager.mockReturnValue(false);
    authService.isNationalManager.mockReturnValue(false);
    authService.restoreSession.mockReturnValue(of(null));
    authService.getHomeRoute.mockReturnValue('/login');
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: authService },
      ],
    });
  });

  it('should be created', () => {
    expect(executeGuard).toBeTruthy();
  });

  it('redirects unauthenticated users to login with a safe returnUrl', async () => {
    authService.isLoggedIn.mockReturnValue(false);
    authService.restoreSession.mockReturnValue(of(null));

    const router = TestBed.inject(Router);
    const result = await lastValueFrom(
      executeGuard({} as never, { url: '/dashboard' } as never) as Observable<boolean | UrlTree>,
    );

    expect(router.serializeUrl(result as UrlTree)).toBe('/login?returnUrl=%2Fdashboard');
  });

  it('clears expired local auth and redirects to login with session-expired state', () => {
    authService.isLoggedIn.mockReturnValue(true);
    authService.hasValidAccessToken.mockReturnValue(false);

    const router = TestBed.inject(Router);
    const result = executeGuard({} as never, { url: '/destinations' } as never) as UrlTree;

    expect(authService.clearSession).toHaveBeenCalledTimes(1);
    expect(router.serializeUrl(result)).toBe('/login?returnUrl=%2Fdestinations&sessionExpired=1');
  });

  it('allows authenticated regional managers', async () => {
    authService.isRegionalManager.mockReturnValue(true);
    authService.hasValidAccessToken.mockReturnValue(true);

    const result = executeRegionalGuard({} as never, { url: '/dashboard' } as never);

    expect(result).toBe(true);
  });

  it('blocks authenticated users without the regional manager role', async () => {
    authService.isRegionalManager.mockReturnValue(false);
    authService.restoreSession.mockReturnValue(of({ role: 'Viewer' }));
    authService.getHomeRoute.mockReturnValue('/login');

    const router = TestBed.inject(Router);
    const result = await lastValueFrom(
      executeRegionalGuard({} as never, { url: '/dashboard' } as never) as Observable<boolean | UrlTree>,
    );

    expect(router.serializeUrl(result as UrlTree)).toBe('/login');
  });
});
