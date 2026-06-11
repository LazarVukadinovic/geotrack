import { CommonModule } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../../../core/services/auth.service';
import { UserService } from '../../../../core/services/user.service';

interface AuthActionResponse {
  message: string;
  accessToken?: string;
  expiresIn?: number;
  sessionPersistent?: boolean;
}

@Component({
  selector: 'app-reset-password-page',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <main class="min-h-full bg-white flex flex-col items-center px-6 py-10">
      <form class="w-full max-w-md bg-[#fafaf9] rounded-[2rem] px-7 py-8 shadow-sm border border-slate-100" (ngSubmit)="submit()" novalidate>
        <img src="images/logo.png" alt="GeoTrack" class="w-20 h-20 object-contain mx-auto mb-4" />
        <h1 class="text-2xl font-bold text-[#0f172a] tracking-tight mb-2 text-center">Choose a new password</h1>
        <p class="text-base text-slate-600 leading-relaxed mb-6 text-center">
          Use at least 8 characters, a number, and a special character.
        </p>

        <label for="reset-password" class="ml-2 text-[12px] font-bold tracking-[0.15em] text-slate-700 uppercase mb-2 block">New password</label>
        <input
          id="reset-password"
          name="password"
          [type]="showPassword ? 'text' : 'password'"
          [(ngModel)]="password"
          autocomplete="new-password"
          placeholder="Enter new password"
          class="w-full px-4 py-4 bg-white border border-slate-200 rounded-3xl text-base text-slate-700 placeholder-slate-300 outline-none focus:border-[#e8570a] focus:ring-2 focus:ring-orange-100 transition-all duration-200 mb-4"
        />

        <label for="reset-repeat" class="ml-2 text-[12px] font-bold tracking-[0.15em] text-slate-700 uppercase mb-2 block">Repeat password</label>
        <input
          id="reset-repeat"
          name="repeatPassword"
          [type]="showPassword ? 'text' : 'password'"
          [(ngModel)]="repeatPassword"
          autocomplete="new-password"
          placeholder="Repeat new password"
          class="w-full px-4 py-4 bg-white border border-slate-200 rounded-3xl text-base text-slate-700 placeholder-slate-300 outline-none focus:border-[#e8570a] focus:ring-2 focus:ring-orange-100 transition-all duration-200 mb-3"
        />

        <button type="button" (click)="showPassword = !showPassword" class="mb-4 ml-2 text-sm font-semibold text-[#e8570a]">
          {{ showPassword ? 'Hide passwords' : 'Show passwords' }}
        </button>

        @if (successMessage) {
          <div class="mb-4 px-4 py-3 bg-green-50 border border-green-200 rounded-2xl text-sm text-green-700 text-center">
            {{ successMessage }}
          </div>
        }

        @if (errorMessage) {
          <div class="mb-4 px-4 py-3 bg-red-50 border border-red-200 rounded-2xl text-sm text-red-600 text-center">
            {{ errorMessage }}
          </div>
        }

        <button
          type="submit"
          [disabled]="isLoading || !!successMessage"
          class="w-full py-3.5 bg-[#e8570a] hover:bg-[#d4621f] rounded-3xl text-white font-bold text-base transition-all disabled:opacity-60 disabled:pointer-events-none">
          {{ isLoading ? 'Saving…' : 'Reset password' }}
        </button>

        <button type="button" (click)="goToLogin()" class="mt-5 block mx-auto text-sm font-semibold text-slate-500 hover:text-[#e8570a]">
          Back to sign in
        </button>
      </form>
    </main>
  `,
})
export class ResetPasswordPageComponent {
  email = '';
  token = '';
  password = '';
  repeatPassword = '';
  showPassword = false;
  isLoading = false;
  successMessage = '';
  errorMessage = '';

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private http: HttpClient,
    private authService: AuthService,
    private userService: UserService,
    private cdr: ChangeDetectorRef,
  ) {
    this.email = this.route.snapshot.queryParamMap.get('email') ?? '';
    this.token = this.route.snapshot.queryParamMap.get('token') ?? '';
  }

  submit(): void {
    this.errorMessage = '';

    if (!this.email || !this.token) {
      this.errorMessage = 'Password reset link is missing required information.';
      this.cdr.markForCheck();
      return;
    }

    if (!this.isStrongPassword(this.password)) {
      this.errorMessage = 'Use at least 8 characters, a number, and a special character.';
      this.cdr.markForCheck();
      return;
    }

    if (this.password !== this.repeatPassword) {
      this.errorMessage = 'Passwords do not match.';
      this.cdr.markForCheck();
      return;
    }

    this.isLoading = true;

    this.http
      .post<AuthActionResponse>('/api/auth/reset-password', {
        email: this.email,
        token: this.token,
        newPassword: this.password,
      })
      .pipe(
        finalize(() => {
          this.isLoading = false;
          this.cdr.markForCheck();
        }),
      )
      .subscribe({
        next: (response) => {
          this.successMessage = response.message || 'Password has been reset successfully.';
          this.cdr.markForCheck();

          if (response.accessToken) {
            this.signInAndContinue(response);
          }
        },
        error: (err: HttpErrorResponse) => {
          this.errorMessage = err.error?.message || 'Could not reset password. The link may be invalid or expired.';
          this.cdr.markForCheck();
        },
      });
  }

  goToLogin(): void {
    this.router.navigate(['/login']);
  }

  private signInAndContinue(response: AuthActionResponse): void {
    this.authService.login(response.accessToken!, response.sessionPersistent ?? false);

    this.userService.load().subscribe({
      next: (profile) => {
        this.router.navigate([profile.needsInterestsOnboarding ? '/register/interests' : '/profile']);
      },
      error: () => {
        this.router.navigate(['/profile']);
      },
    });
  }

  private isStrongPassword(value: string): boolean {
    return value.length >= 8 && /\d/.test(value) && /[!@#$%^&*(),.?":{}|<>]/.test(value);
  }
}
