import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { vi } from 'vitest';
import { RegisterPageComponent } from './register-page.component';

describe('RegisterPageComponent', () => {
  let component: RegisterPageComponent;
  let fixture: ComponentFixture<RegisterPageComponent>;
  let httpMock: HttpTestingController;

  const router = {
    navigate: vi.fn(),
  };

  beforeEach(async () => {
    vi.clearAllMocks();

    await TestBed.configureTestingModule({
      imports: [RegisterPageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: Router, useValue: router },
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
    expect(router.navigate).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('An account with this email already exists.');
    expect(fixture.nativeElement.textContent).toContain('Continue');
  });

  it('navigates to email verification sent page after successful registration', () => {
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
      message: 'Registration successful. Please check your email to verify your account before signing in.',
    });

    expect(router.navigate).toHaveBeenCalledWith(['/verify-email-sent'], {
      queryParams: { email: 'new@example.com' },
    });
    expect(component.isLoading).toBe(false);
  });
});
