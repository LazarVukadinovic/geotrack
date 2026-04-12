import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { AuthService } from '../../../../core/services/auth.service';

interface AuthResponse {
  token: string;
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
        username: this.email.trim(),
        password: this.password,
      })
      .subscribe({
        next: (response) => {
          this.isLoading = false;
          this.authService.login(response.token);
          this.router.navigate(['/explore']);
        },
        error: (err: HttpErrorResponse) => {
          this.isLoading = false;

          if (err.status === 401) {
            this.errorMessage = err.error?.message || 'Invalid username or password.';
          } else if (err.status === 0) {
            this.errorMessage = 'Unable to reach the server. Please check your connection.';
          } else {
            this.errorMessage = err.error?.message || 'Something went wrong. Please try again.';
          }
        },
      });
  }

  goToRegister(): void {
    this.router.navigate(['/profile/register']);
  }
}
