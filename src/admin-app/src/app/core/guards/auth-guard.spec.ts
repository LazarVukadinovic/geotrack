import { TestBed } from '@angular/core/testing';
import { CanActivateFn, UrlTree } from '@angular/router';
import { provideRouter, Router } from '@angular/router';
import { lastValueFrom, Observable, of } from 'rxjs';
import { vi } from 'vitest';

import { administratorGuard, authGuard, contentModeratorGuard, regionalManagerGuard, territoryManagerGuard } from './auth.guard';
import { AuthService } from '../services/auth.service';

describe('authGuard', () => {
  const executeGuard: CanActivateFn = (...guardParameters) =>
    TestBed.runInInjectionContext(() => authGuard(...guardParameters));
  const executeRegionalGuard: CanActivateFn = (...guardParameters) =>
    TestBed.runInInjectionContext(() => regionalManagerGuard(...guardParameters));
  const executeTerritoryGuard: CanActivateFn = (...guardParameters) =>
    TestBed.runInInjectionContext(() => territoryManagerGuard(...guardParameters));
  const executeAdministratorGuard: CanActivateFn = (...guardParameters) =>
    TestBed.runInInjectionContext(() => administratorGuard(...guardParameters));
  const executeContentModeratorGuard: CanActivateFn = (...guardParameters) =>
    TestBed.runInInjectionContext(() => contentModeratorGuard(...guardParameters));

  const authService = {
    isLoggedIn: vi.fn(),
    hasValidAccessToken: vi.fn(),
    getUser: vi.fn(),
    clearSession: vi.fn(),
    isAdministrator: vi.fn(),
    isContentModerator: vi.fn(),
    isRegionalManager: vi.fn(),
    isTerritoryManager: vi.fn(),
    canAccessTerritoryAdmin: vi.fn(),
    restoreSession: vi.fn(),
    getHomeRoute: vi.fn(),
  };

  beforeEach(() => {
    vi.clearAllMocks();
    authService.isLoggedIn.mockReturnValue(false);
    authService.hasValidAccessToken.mockReturnValue(false);
    authService.getUser.mockReturnValue(null);
    authService.clearSession.mockReturnValue(undefined);
    authService.isAdministrator.mockReturnValue(false);
    authService.isContentModerator.mockReturnValue(false);
    authService.isRegionalManager.mockReturnValue(false);
    authService.isTerritoryManager.mockReturnValue(false);
    authService.canAccessTerritoryAdmin.mockReturnValue(false);
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

  it('allows authenticated admins on administrator routes', () => {
    authService.isAdministrator.mockReturnValue(true);
    authService.hasValidAccessToken.mockReturnValue(true);

    const result = executeAdministratorGuard({} as never, { url: '/admin-dashboard' } as never);

    expect(result).toBe(true);
  });

  it('allows authenticated content moderators on moderation routes', () => {
    authService.isContentModerator.mockReturnValue(true);
    authService.hasValidAccessToken.mockReturnValue(true);

    const result = executeContentModeratorGuard({} as never, { url: '/content-moderator/dashboard' } as never);

    expect(result).toBe(true);
  });

  it('redirects authenticated non-moderators away from moderation routes', async () => {
    authService.isContentModerator.mockReturnValue(false);
    authService.restoreSession.mockReturnValue(of({ role: 'Administrator' }));
    authService.getHomeRoute.mockReturnValue('/admin-dashboard');

    const router = TestBed.inject(Router);
    const result = await lastValueFrom(
      executeContentModeratorGuard({} as never, { url: '/content-moderator/dashboard' } as never) as Observable<boolean | UrlTree>,
    );

    expect(router.serializeUrl(result as UrlTree)).toBe('/admin-dashboard');
  });

  it('redirects authenticated non-admin users away from administrator routes', async () => {
    authService.isAdministrator.mockReturnValue(false);
    authService.restoreSession.mockReturnValue(of({ role: 'Regional Manager' }));
    authService.getHomeRoute.mockReturnValue('/dashboard');

    const router = TestBed.inject(Router);
    const result = await lastValueFrom(
      executeAdministratorGuard({} as never, { url: '/admin-dashboard' } as never) as Observable<boolean | UrlTree>,
    );

    expect(router.serializeUrl(result as UrlTree)).toBe('/dashboard');
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

  it('allows authenticated territory managers', () => {
    authService.canAccessTerritoryAdmin.mockReturnValue(true);
    authService.hasValidAccessToken.mockReturnValue(true);

    const result = executeTerritoryGuard({} as never, { url: '/territory/overview' } as never);

    expect(result).toBe(true);
  });

  it('allows authenticated territory associates', () => {
    authService.canAccessTerritoryAdmin.mockReturnValue(true);
    authService.hasValidAccessToken.mockReturnValue(true);

    const result = executeTerritoryGuard({} as never, { url: '/territory/users' } as never);

    expect(result).toBe(true);
  });

  it('redirects regional users away from territory routes', async () => {
    authService.canAccessTerritoryAdmin.mockReturnValue(false);
    authService.restoreSession.mockReturnValue(of({ role: 'Regional Manager' }));
    authService.getHomeRoute.mockReturnValue('/dashboard');

    const router = TestBed.inject(Router);
    const result = await lastValueFrom(
      executeTerritoryGuard({} as never, { url: '/territory/overview' } as never) as Observable<boolean | UrlTree>,
    );

    expect(router.serializeUrl(result as UrlTree)).toBe('/dashboard');
  });
});
