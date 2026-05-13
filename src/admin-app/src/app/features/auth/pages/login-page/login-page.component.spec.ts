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

    request.flush(adminLoginResponse());

    expect(component.isLoading()).toBe(false);
    expect(component.authError()).toBeNull();
    expect(navigateSpy).toHaveBeenCalledWith('/dashboard');
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
      },
    };
  }
});
