import { ChangeDetectorRef, Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { catchError, finalize, of, switchMap } from 'rxjs';
import { AuthService } from '../../../../core/services/auth.service';
import { UserService } from '../../../../core/services/user.service';

interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
}

@Component({
  selector: 'app-register-page',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './register-page.component.html',
  styleUrl: './register-page.component.css',
})
export class RegisterPageComponent {
  fullName = '';
  email = '';
  password = '';
  repeatPassword = '';
  showPassword = false;
  showRepeatPassword = false;
  submitted = false;
  emailServerError = '';
  submitErrorMessage = '';
  isLoading = false;

  req = {
    minLength: false,
    hasNumber: false,
    hasSpecial: false,
  };

  constructor(
    private router: Router,
    private http: HttpClient,
    private authService: AuthService,
    private userService: UserService,
    private cdr: ChangeDetectorRef,
  ) {}

  get fullNameError(): string {
    if (!this.submitted) return '';
    if (!this.fullName.trim()) return 'Name is required.';
    return '';
  }

  get emailError(): string {
    if (!this.submitted && !this.emailServerError) return '';
    if (!this.email) return 'Email address is required.';
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(this.email)) return 'Enter a valid email address.';
    if (this.emailServerError) return this.emailServerError;
    return '';
  }

  get passwordError(): string {
    if (!this.submitted) return '';
    if (!this.password) return 'Password is required.';
    return '';
  }

  get repeatPasswordError(): string {
    if (!this.submitted) return '';
    if (!this.repeatPassword) return 'Please repeat your password.';
    if (this.password !== this.repeatPassword) return 'Passwords do not match.';
    return '';
  }

  get isValid(): boolean {
    return (
      !!this.fullName.trim() &&
      !!this.email &&
      /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(this.email) &&
      !!this.password &&
      this.password === this.repeatPassword
    );
  }

  checkPasswordRequirements(): void {
    this.req.minLength = this.password.length >= 8;
    this.req.hasNumber = /\d/.test(this.password);
    this.req.hasSpecial = /[!@#$%^&*(),.?":{}|<>]/.test(this.password);
  }

  onContinue(): void {
    this.submitted = true;
    this.clearServerErrors();
    if (!this.isValid) return;

    this.isLoading = true;

    this.http
      .post<AuthResponse>('/api/auth/register', {
        fullName: this.fullName.trim(),
        email: this.email.trim(),
        password: this.password,
      })
      .pipe(
        switchMap((res) => {
          if (res.accessToken) {
            // Registration always defaults to Remember Me = true so the new
            // session survives a browser restart (the user can adjust in settings).
            this.authService.login(res.accessToken, res.refreshToken, true);
          }

          // Hydrate UserService so /register/interests and downstream screens
          // read canonical current-user state without a hard refresh.
          return this.userService.load().pipe(
            catchError(() => of(null)),
          );
        }),
        finalize(() => {
          this.isLoading = false;
          this.cdr.markForCheck();
        }),
      )
      .subscribe({
        next: () => {
          this.router.navigate(['/register/interests']);
        },
        error: (err: HttpErrorResponse) => {
          this.applyServerError(err);
          this.cdr.markForCheck();
        },
      });
  }

  onFieldInput(): void {
    this.clearServerErrors();
  }

  goBack(): void {
    this.router.navigate(['/profile']);
  }

  goToLogin(): void {
    this.router.navigate(['/profile']);
  }

  private clearServerErrors(): void {
    this.emailServerError = '';
    this.submitErrorMessage = '';
  }

  private applyServerError(err: HttpErrorResponse): void {
    const messages = this.extractServerMessages(err);
    const hasDuplicateEmailError = messages.some(message => this.isDuplicateEmailError(message));

    if (hasDuplicateEmailError) {
      this.emailServerError = 'An account with this email already exists.';
      this.submitErrorMessage = 'An account with this email already exists. Try signing in or use a different email.';
      return;
    }

    if (err.status === 400) {
      this.submitErrorMessage = 'We couldn\'t create your account. Please check your details and try again.';
      return;
    }

    if (err.status === 0) {
      this.submitErrorMessage = 'Unable to reach the server. Please check your connection.';
      return;
    }

    this.submitErrorMessage = 'Something went wrong. Please try again.';
  }

  private extractServerMessages(err: HttpErrorResponse): string[] {
    const messages: string[] = [];
    const errorBody = err.error;

    if (typeof errorBody === 'string' && errorBody.trim()) {
      messages.push(errorBody.trim());
    }

    if (errorBody && typeof errorBody === 'object') {
      const message = errorBody['message'];
      const title = errorBody['title'];
      const error = errorBody['error'];
      const validationErrors = errorBody['errors'];

      if (typeof message === 'string' && message.trim()) {
        messages.push(message.trim());
      }

      if (typeof title === 'string' && title.trim()) {
        messages.push(title.trim());
      }

      if (typeof error === 'string' && error.trim()) {
        messages.push(error.trim());
      }

      if (validationErrors && typeof validationErrors === 'object') {
        Object.values(validationErrors)
          .flatMap(value => Array.isArray(value) ? value : [value])
          .forEach(value => {
            if (typeof value === 'string' && value.trim()) {
              messages.push(value.trim());
            }
          });
      }
    }

    return messages;
  }

  private isDuplicateEmailError(message: string): boolean {
    const normalized = message.trim().toLowerCase();
    return normalized.includes('already exists')
      || normalized.includes('already in use')
      || normalized.includes('email is taken')
      || normalized.includes('email already')
      || normalized.includes('user already exists');
  }
}
