import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { RegisterPageComponent } from './register-page.component';
import { AuthService } from '../../../../core/services/auth.service';
import { UserService } from '../../../../core/services/user.service';

describe('RegisterPageComponent', () => {
  let component: RegisterPageComponent;
  let fixture: ComponentFixture<RegisterPageComponent>;
  let httpMock: HttpTestingController;

  const router = {
    navigate: vi.fn(),
  };

  const authService = {
    login: vi.fn(),
  };

  const userService = {
    load: vi.fn(() => of({})),
  };

  beforeEach(async () => {
    vi.clearAllMocks();

    await TestBed.configureTestingModule({
      imports: [RegisterPageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: Router, useValue: router },
        { provide: AuthService, useValue: authService },
        { provide: UserService, useValue: userService },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(RegisterPageComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
    await fixture.whenStable();
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('clears loading and shows a friendly duplicate-email error after a 400 response', () => {
    component.fullName = 'Test Traveler';
    component.email = 'taken@example.com';
    component.password = 'Password123!';
    component.repeatPassword = 'Password123!';

    component.onContinue();

    expect(component.isLoading).toBe(true);

    const req = httpMock.expectOne('/api/auth/register');
    expect(req.request.method).toBe('POST');
    req.flush(
      { message: 'An account with this email already exists.' },
      { status: 400, statusText: 'Bad Request' },
    );

    fixture.detectChanges();

    expect(component.isLoading).toBe(false);
    expect(component.emailError).toBe('An account with this email already exists.');
    expect(component.submitErrorMessage).toBe(
      'An account with this email already exists. Try signing in or use a different email.',
    );
    expect(authService.login).not.toHaveBeenCalled();
    expect(userService.load).not.toHaveBeenCalled();
    expect(router.navigate).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('An account with this email already exists.');
    expect(fixture.nativeElement.textContent).toContain('Continue');
  });

  it('navigates to interests after a successful registration and profile load', () => {
    component.fullName = 'Test Traveler';
    component.email = 'new@example.com';
    component.password = 'Password123!';
    component.repeatPassword = 'Password123!';

    component.onContinue();

    const req = httpMock.expectOne('/api/auth/register');
    expect(req.request.body).toEqual({
      fullName: 'Test Traveler',
      email: 'new@example.com',
      password: 'Password123!',
    });

    req.flush({
      accessToken: 'access-token',
      refreshToken: 'refresh-token',
      expiresIn: 900,
    });

    expect(authService.login).toHaveBeenCalledWith('access-token', 'refresh-token', true);
    expect(userService.load).toHaveBeenCalled();
    expect(router.navigate).toHaveBeenCalledWith(['/register/interests']);
    expect(component.isLoading).toBe(false);
  });
});
