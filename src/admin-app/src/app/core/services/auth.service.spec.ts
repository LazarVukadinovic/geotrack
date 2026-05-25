import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { AuthService } from './auth.service';

describe('AuthService', () => {
  let service: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    sessionStorage.clear();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('logs in through the admin auth endpoint and stores the regional user in memory', () => {
    let resolved = false;

    service.login('regional@example.com', 'Password123!', true).subscribe(user => {
      resolved = true;
      expect(user.email).toBe('regional@example.com');
      expect(user.role).toBe('Regional Manager');
      expect(user.avatarUrl).toBe('/media/uploads/avatars/regional.png');
      expect(user.avatarVersion).toBe(101);
    });

    const request = http.expectOne('/api/admin/auth/login');
    expect(request.request.method).toBe('POST');
    expect(request.request.withCredentials).toBe(true);
    expect(request.request.body).toEqual({
      email: 'regional@example.com',
      password: 'Password123!',
      rememberMe: true,
    });

    request.flush(adminLoginResponse());

    expect(resolved).toBe(true);
    expect(service.isLoggedIn()).toBe(true);
    expect(service.getAccessToken()).toBe('access-token');
  });

  it('maps territory manager roles to the territory dashboard route', () => {
    service.login('mika@gmail.com', 'Proba123!').subscribe(user => {
      expect(user.email).toBe('mika@gmail.com');
      expect(user.role).toBe('Territory Manager');
    });

    http.expectOne('/api/admin/auth/login').flush({
      accessToken: 'territory-token',
      expiresAt: new Date(Date.now() + 900_000).toISOString(),
      expiresIn: 900,
      sessionPersistent: false,
      user: {
        id: 42,
        email: 'mika@gmail.com',
        displayName: 'Mika',
        roles: ['TerritoryManager'],
        canAccessRegionalAdmin: false,
        canAccessTerritoryAdmin: true,
      },
    });

    expect(service.isTerritoryManager()).toBe(true);
    expect(service.isRegionalManager()).toBe(false);
    expect(service.getHomeRoute()).toBe('/territory/overview');
  });

  it('maps territory associate roles to territory admin access without territory manager identity', () => {
    service.login('associate@example.com', 'Proba123!').subscribe(user => {
      expect(user.email).toBe('associate@example.com');
      expect(user.role).toBe('Territory Associate');
    });

    http.expectOne('/api/admin/auth/login').flush({
      accessToken: 'associate-token',
      expiresAt: new Date(Date.now() + 900_000).toISOString(),
      expiresIn: 900,
      sessionPersistent: false,
      user: {
        id: 43,
        email: 'associate@example.com',
        displayName: 'Associate User',
        roles: ['TerritoryAssociate'],
        canAccessRegionalAdmin: false,
        canAccessTerritoryAdmin: true,
      },
    });

    expect(service.isTerritoryAssociate()).toBe(true);
    expect(service.isTerritoryManager()).toBe(false);
    expect(service.canAccessTerritoryAdmin()).toBe(true);
    expect(service.getHomeRoute()).toBe('/territory/overview');
  });

  it('maps admin roles to the existing administrator dashboard route', () => {
    service.login('zika@gmail.com', 'Proba123!').subscribe(user => {
      expect(user.email).toBe('zika@gmail.com');
      expect(user.role).toBe('Administrator');
      expect(user.activeRoleCode).toBe('Admin');
      expect(user.roles).toEqual(['Admin']);
    });

    http.expectOne('/api/admin/auth/login').flush({
      accessToken: 'admin-token',
      expiresAt: new Date(Date.now() + 900_000).toISOString(),
      expiresIn: 900,
      sessionPersistent: false,
      user: {
        id: 46,
        email: 'zika@gmail.com',
        displayName: 'Zika',
        roles: ['Admin'],
        canAccessRegionalAdmin: false,
        canAccessTerritoryAdmin: false,
      },
    });

    expect(service.isAdministrator()).toBe(true);
    expect(service.isRegionalManager()).toBe(false);
    expect(service.canAccessTerritoryAdmin()).toBe(false);
    expect(service.getHomeRoute()).toBe('/admin-dashboard');
  });

  it('gives admin role priority over a stored non-admin view during login', () => {
    sessionStorage.setItem('geotrack_admin_active_view', 'RegionalManager');

    service.login('zika@gmail.com', 'Proba123!').subscribe(user => {
      expect(user.activeRoleCode).toBe('Admin');
      expect(user.roles).toEqual(['Admin', 'RegionalManager']);
    });

    http.expectOne('/api/admin/auth/login').flush({
      accessToken: 'admin-token',
      expiresAt: new Date(Date.now() + 900_000).toISOString(),
      expiresIn: 900,
      sessionPersistent: false,
      user: {
        id: 47,
        email: 'zika@gmail.com',
        displayName: 'Zika',
        roles: ['RegionalManager', 'Admin'],
        canAccessRegionalAdmin: true,
        canAccessTerritoryAdmin: false,
      },
    });

    expect(service.getHomeRoute()).toBe('/admin-dashboard');
    expect(sessionStorage.getItem('geotrack_admin_active_view')).toBe('Admin');
  });

  it('uses the existing territory-first redirect priority for multi-role admin users', () => {
    service.login('multi@example.com', 'Proba123!').subscribe(user => {
      expect(user.email).toBe('multi@example.com');
      expect(user.role).toBe('Territory Manager');
      expect(user.roles).toEqual(['TerritoryManager', 'RegionalManager']);
    });

    http.expectOne('/api/admin/auth/login').flush({
      accessToken: 'multi-token',
      expiresAt: new Date(Date.now() + 900_000).toISOString(),
      expiresIn: 900,
      sessionPersistent: false,
      user: {
        id: 44,
        email: 'multi@example.com',
        displayName: 'Multi Role',
        roles: ['RegionalManager', 'TerritoryManager'],
        canAccessRegionalAdmin: true,
        canAccessTerritoryAdmin: true,
      },
    });

    expect(service.getHomeRoute()).toBe('/territory/overview');
  });

  it('switches the active admin view only to a role owned by the current user', () => {
    service.login('multi@example.com', 'Proba123!').subscribe();

    http.expectOne('/api/admin/auth/login').flush({
      accessToken: 'multi-token',
      expiresAt: new Date(Date.now() + 900_000).toISOString(),
      expiresIn: 900,
      sessionPersistent: false,
      user: {
        id: 44,
        email: 'multi@example.com',
        displayName: 'Multi Role',
        roles: ['TerritoryManager', 'RegionalManager'],
        canAccessRegionalAdmin: true,
        canAccessTerritoryAdmin: true,
      },
    });

    expect(service.switchAdminView('RegionalManager')).toBe(true);
    expect(service.getUser()?.role).toBe('Regional Manager');
    expect(service.getHomeRoute()).toBe('/dashboard');
    expect(sessionStorage.getItem('geotrack_admin_active_view')).toBe('RegionalManager');

    expect(service.switchAdminView('Admin')).toBe(false);
    expect(service.getUser()?.role).toBe('Regional Manager');
  });

  it('falls back from an invalid stored admin view during session restore', () => {
    sessionStorage.setItem('geotrack_admin_session', '1');
    sessionStorage.setItem('geotrack_admin_active_view', 'RegionalManager');

    service.restoreSession().subscribe(user => {
      expect(user?.role).toBe('Territory Associate');
      expect(user?.activeRoleCode).toBe('TerritoryAssociate');
    });

    const request = http.expectOne('/api/admin/auth/refresh');
    expect(request.request.method).toBe('POST');
    request.flush({
      accessToken: 'associate-token',
      expiresAt: new Date(Date.now() + 900_000).toISOString(),
      expiresIn: 900,
      sessionPersistent: false,
      user: {
        id: 45,
        email: 'associate@example.com',
        displayName: 'Associate User',
        roles: ['TerritoryAssociate'],
        canAccessRegionalAdmin: false,
        canAccessTerritoryAdmin: true,
      },
    });

    expect(sessionStorage.getItem('geotrack_admin_active_view')).toBe('TerritoryAssociate');
    expect(service.getHomeRoute()).toBe('/territory/overview');
  });

  it('uses a safe message for invalid credentials', () => {
    let message = '';

    service.login('missing@example.com', 'wrong').subscribe({
      error: error => {
        message = error.message;
      },
    });

    const request = http.expectOne('/api/admin/auth/login');
    request.flush({ message: 'Invalid email or password.' }, { status: 401, statusText: 'Unauthorized' });

    expect(message).toBe('Invalid email or password.');
    expect(service.isLoggedIn()).toBe(false);
  });

  it('clears local auth state on logout', () => {
    service.login('regional@example.com', 'Password123!').subscribe();
    http.expectOne('/api/admin/auth/login').flush(adminLoginResponse());

    service.logout();

    const logout = http.expectOne('/api/admin/auth/logout');
    expect(logout.request.method).toBe('POST');
    expect(logout.request.withCredentials).toBe(true);
    logout.flush(null, { status: 204, statusText: 'No Content' });

    expect(service.isLoggedIn()).toBe(false);
    expect(service.getAccessToken()).toBeNull();
  });

  function adminLoginResponse() {
    return {
      accessToken: 'access-token',
      expiresAt: new Date(Date.now() + 900_000).toISOString(),
      expiresIn: 900,
      sessionPersistent: false,
      user: {
        id: 7,
        email: 'regional@example.com',
        displayName: 'Regional Manager',
        avatarUrl: '/media/uploads/avatars/regional.png',
        avatarVersion: 101,
        roles: ['RegionalManager'],
        canAccessRegionalAdmin: true,
        canAccessTerritoryAdmin: false,
      },
    };
  }
});
