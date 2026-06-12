import { ChangeDetectorRef, Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { AuthService } from '../../../../core/services/auth.service';
import { UserService } from '../../../../core/services/user.service';
import { AnalyticsService } from '../../../../core/services/analytics.service';

interface AuthResponse {
  accessToken: string;
  expiresIn: number;
  sessionPersistent: boolean;
}

@Component({
  selector: 'app-login-page',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './login-page.component.html',
  styleUrl: './login-page.component.css',
})
export class LoginPageComponent {
  email = '';
  password = '';
  showPassword = false;
  rememberMe = false;
  errorMessage = '';
  isLoading = false;

  constructor(
    private router: Router,
    private http: HttpClient,
    private authService: AuthService,
    private userService: UserService,
    private analytics: AnalyticsService,
    private cdr: ChangeDetectorRef,
  ) {}

  onSignIn(): void {
    this.errorMessage = '';

    if (!this.email.trim() || !this.password) {
      this.errorMessage = 'Please enter your email and password.';
      return;
    }

    this.isLoading = true;

    this.http
      .post<AuthResponse>('/api/auth/login', {
        email: this.email.trim(),
        password: this.password,
        rememberMe: this.rememberMe,
      })
      .subscribe({
        next: (response) => {
          this.authService.login(response.accessToken, response.sessionPersistent);
          this.analytics.track('login_completed');
          // Fetch the user profile immediately after login so that the
          // Profile page already has fullName/email on first render —
          // no manual refresh needed.
          this.userService.load().subscribe({
            next: (profile) => {
              this.isLoading = false;
              this.router.navigate([profile.needsInterestsOnboarding ? '/register/interests' : '/profile']);
            },
            error: () => {
              // Profile fetch failed but the user is logged in; navigate anyway.
              this.isLoading = false;
              this.router.navigate(['/profile']);
            },
          });
        },
        error: (err: HttpErrorResponse) => {
          this.isLoading = false;

          const serverMessage = err.error?.message || '';
          if (err.status === 401 && this.isEmailVerificationRequired(serverMessage)) {
            this.router.navigate(['/verify-email-sent'], { queryParams: { email: this.email.trim() } });
            return;
          }

          if (err.status === 401) {
            this.errorMessage = serverMessage || 'Invalid email or password.';
          } else if (err.status === 0) {
            this.errorMessage = 'Unable to reach the server. Please check your connection.';
          } else {
            this.errorMessage = err.error?.message || 'Something went wrong. Please try again.';
          }

          this.cdr.markForCheck();
        },
      });
  }

  goToRegister(): void {
    this.router.navigate(['/register']);
  }

  goToForgotPassword(): void {
    this.router.navigate(['/forgot-password']);
  }

  private isEmailVerificationRequired(message: string): boolean {
    return message.toLowerCase().includes('verify your email');
  }
}
