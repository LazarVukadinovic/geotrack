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
        roles: ['RegionalManager'],
        canAccessRegionalAdmin: true,
        canAccessTerritoryAdmin: false,
      },
    };
  }
});
