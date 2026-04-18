import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { AuthService } from '../../../../core/services/auth.service';
import { UserService } from '../../../../core/services/user.service';

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
  errorMessage = '';
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
  ) {}

  get fullNameError(): string {
    if (!this.submitted) return '';
    if (!this.fullName.trim()) return 'Name is required.';
    return '';
  }

  get emailError(): string {
    if (!this.submitted) return '';
    if (!this.email) return 'Email address is required.';
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(this.email)) return 'Enter a valid email address.';
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
    this.errorMessage = '';
    if (!this.isValid) return;

    this.isLoading = true;

    this.http
      .post<{ accessToken: string; refreshToken: string; expiresIn: number }>('/api/auth/register', {
        fullName: this.fullName.trim(),
        email: this.email.trim(),
        password: this.password,
      })
      .subscribe({
        next: (res) => {
          if (res.accessToken) {
            // Registration always defaults to Remember Me = true so the new
            // session survives a browser restart (the user can adjust in settings).
            this.authService.login(res.accessToken, res.refreshToken, true);
          }
          // Hydrate UserService so /register/interests and downstream screens
          // read canonical current-user state without a hard refresh.
          this.userService.load().subscribe({
            next: () => {
              this.isLoading = false;
              this.router.navigate(['/register/interests']);
            },
            error: () => {
              this.isLoading = false;
              this.router.navigate(['/register/interests']);
            },
          });
        },
        error: (err: HttpErrorResponse) => {
          this.isLoading = false;

          if (err.status === 400) {
            this.errorMessage = err.error?.message || 'Registration failed. Please check your details.';
          } else if (err.status === 0) {
            this.errorMessage = 'Unable to reach the server. Please check your connection.';
          } else {
            this.errorMessage = err.error?.message || 'Something went wrong. Please try again.';
          }
        },
      });
  }

  goBack(): void {
    this.router.navigate(['/profile']);
  }

  goToLogin(): void {
    this.router.navigate(['/profile']);
  }
}
