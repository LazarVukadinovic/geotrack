import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { LoginPageComponent } from './login-page.component';
import { AuthService } from '../../../../core/services/auth.service';
import { UserService } from '../../../../core/services/user.service';
import { AnalyticsService } from '../../../../core/services/analytics.service';

describe('LoginPageComponent', () => {
  let component: LoginPageComponent;
  let fixture: ComponentFixture<LoginPageComponent>;
  let http: HttpTestingController;

  const router = {
    navigate: vi.fn(),
  };

  const authService = {
    login: vi.fn(),
  };

  const userService = {
    load: vi.fn(() => of({})),
  };

  const analytics = {
    track: vi.fn(),
  };

  beforeEach(async () => {
    vi.clearAllMocks();

    await TestBed.configureTestingModule({
      imports: [LoginPageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: Router, useValue: router },
        { provide: AuthService, useValue: authService },
        { provide: UserService, useValue: userService },
        { provide: AnalyticsService, useValue: analytics },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(LoginPageComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    await fixture.whenStable();
  });

  afterEach(() => {
    http.verify();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('posts credentials to user login, stores the access token, hydrates /me, and redirects', () => {
    component.email = 'marko.p@example.com';
    component.password = 'password123';
    component.rememberMe = true;

    component.onSignIn();

    const request = http.expectOne('/api/auth/login');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      email: 'marko.p@example.com',
      password: 'password123',
      rememberMe: true,
    });

    request.flush({ accessToken: 'access-token', expiresIn: 900, sessionPersistent: true });

    expect(authService.login).toHaveBeenCalledWith('access-token', true);
    expect(userService.load).toHaveBeenCalledTimes(1);
    expect(analytics.track).toHaveBeenCalledWith('login_completed');
    expect(router.navigate).toHaveBeenCalledWith(['/profile']);
    expect(component.isLoading).toBe(false);
    expect(component.errorMessage).toBe('');
  });

  it('submits login when the form is submitted', () => {
    component.email = 'marko.p@example.com';
    component.password = 'password123';
    fixture.detectChanges();

    const form = fixture.nativeElement.querySelector('form') as HTMLFormElement;
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));

    const request = http.expectOne('/api/auth/login');
    expect(request.request.method).toBe('POST');
    request.flush({ accessToken: 'access-token', expiresIn: 900, sessionPersistent: false });

    expect(authService.login).toHaveBeenCalledWith('access-token', false);
    expect(router.navigate).toHaveBeenCalledWith(['/profile']);
  });

  it('shows a safe error when user login returns 401', () => {
    component.email = 'marko.p@example.com';
    component.password = 'wrong-password';

    component.onSignIn();

    const request = http.expectOne('/api/auth/login');
    request.flush({ message: 'Invalid email or password.' }, { status: 401, statusText: 'Unauthorized' });

    expect(authService.login).not.toHaveBeenCalled();
    expect(userService.load).not.toHaveBeenCalled();
    expect(router.navigate).not.toHaveBeenCalled();
    expect(component.isLoading).toBe(false);
    expect(component.errorMessage).toBe('Invalid email or password.');
  });
});
