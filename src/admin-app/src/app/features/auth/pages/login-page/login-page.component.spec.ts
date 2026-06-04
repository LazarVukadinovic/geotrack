import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { vi } from 'vitest';

import { LoginComponent } from './login-page.component';

describe('LoginComponent', () => {
  let component: LoginComponent;
  let fixture: ComponentFixture<LoginComponent>;
  let http: HttpTestingController;

  beforeEach(async () => {
    sessionStorage.clear();
    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    })
    .compileComponents();

    fixture = TestBed.createComponent(LoginComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    await fixture.whenStable();
  });

  afterEach(() => {
    http.verify();
    sessionStorage.clear();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('posts email and password to admin login and redirects to dashboard', () => {
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

    component.email.set('regional@example.com');
    component.password.set('Password123!');
    component.rememberMe.set(true);
    component.onSubmit();

    const request = http.expectOne('/api/admin/auth/login');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      email: 'regional@example.com',
      password: 'Password123!',
      rememberMe: true,
    });

    const response = adminLoginResponse();
    request.flush(response);
    flushCurrentUser(response.user);

    expect(component.isLoading()).toBe(false);
    expect(component.authError()).toBeNull();
    expect(navigateSpy).toHaveBeenCalledWith('/dashboard');
  });

  it('redirects territory managers to the territory dashboard after login', () => {
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

    component.email.set('mika@gmail.com');
    component.password.set('Proba123!');
    component.onSubmit();

    const response = {
      accessToken: 'territory-token',
      expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
      expiresIn: 3600,
      sessionPersistent: false,
      user: {
        id: 42,
        email: 'mika@gmail.com',
        displayName: 'Mika',
        roles: ['TerritoryManager'],
        canAccessRegionalAdmin: false,
        canAccessTerritoryAdmin: true,
      },
    };

    http.expectOne('/api/admin/auth/login').flush(response);
    flushCurrentUser(response.user);
    flushAssignedTerritories([{ id: 10, name: 'Croatia', displayName: 'Croatia', statusLabel: 'Assigned' }]);

    expect(navigateSpy).toHaveBeenCalledWith('/territory/overview');
  });

  it('redirects admin users to the existing administrator dashboard and not admin overview', () => {
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

    component.email.set('zika@gmail.com');
    component.password.set('Proba123!');
    component.onSubmit();

    const response = {
      accessToken: 'admin-token',
      expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
      expiresIn: 3600,
      sessionPersistent: false,
      user: {
        id: 46,
        email: 'zika@gmail.com',
        displayName: 'Zika',
        roles: ['Admin'],
        canAccessRegionalAdmin: false,
        canAccessTerritoryAdmin: false,
      },
    };

    http.expectOne('/api/admin/auth/login').flush(response);
    flushCurrentUser(response.user);

    expect(navigateSpy).toHaveBeenCalledWith('/admin-dashboard');
    expect(navigateSpy).not.toHaveBeenCalledWith('/admin/overview');
  });

  it('shows a safe error when login fails', () => {
    component.email.set('regional@example.com');
    component.password.set('wrong-password');
    component.onSubmit();

    const request = http.expectOne('/api/admin/auth/login');
    request.flush({ message: 'Invalid email or password.' }, { status: 401, statusText: 'Unauthorized' });

    expect(component.isLoading()).toBe(false);
    expect(component.authError()).toBe('Invalid email or password.');
  });

  it('routes normal user admin-login attempts to the no-access page', () => {
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    component.email.set('marko.p@example.com');
    component.password.set('password123');
    component.onSubmit();

    const request = http.expectOne('/api/admin/auth/login');
    request.flush(
      { message: 'You do not have access to the admin app.' },
      { status: 403, statusText: 'Forbidden' },
    );

    expect(component.isLoading()).toBe(false);
    expect(component.authError()).toBeNull();
    expect(navigateSpy).toHaveBeenCalledWith(['/no-admin-access'], { replaceUrl: true });
  });

  function adminLoginResponse() {
    return {
      accessToken: 'access-token',
      expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
      expiresIn: 3600,
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

  function flushCurrentUser(user: object): void {
    const request = http.expectOne('/api/admin/auth/me');
    expect(request.request.method).toBe('GET');
    request.flush(user);
  }

  function flushAssignedTerritories(territories: unknown[]): void {
    const request = http.expectOne('/api/admin/territory/territories');
    expect(request.request.method).toBe('GET');
    request.flush(territories);
  }
});
