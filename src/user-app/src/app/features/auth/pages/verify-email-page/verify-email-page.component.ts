import { CommonModule } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../../../core/services/auth.service';
import { UserService } from '../../../../core/services/user.service';

interface AuthActionResponse {
  message: string;
  accessToken?: string;
  expiresIn?: number;
  sessionPersistent?: boolean;
}

@Component({
  selector: 'app-verify-email-page',
  standalone: true,
  imports: [CommonModule],
  template: `
    <main class="min-h-full bg-white flex flex-col items-center px-6 py-10">
      <section class="w-full max-w-md bg-[#fafaf9] rounded-[2rem] px-7 py-8 shadow-sm border border-slate-100 text-center">
        <img src="images/logo.png" alt="GeoTrack" class="w-20 h-20 object-contain mx-auto mb-4" />
        <h1 class="text-2xl font-bold text-[#0f172a] tracking-tight mb-3">Email verification</h1>

        @if (isLoading) {
          <p class="text-base text-slate-600 leading-relaxed mb-5">Verifying your email address…</p>
        }

        @if (!isLoading && successMessage) {
          <div class="mb-5 px-4 py-3 bg-green-50 border border-green-200 rounded-2xl text-sm text-green-700">
            {{ successMessage }}
          </div>
        }

        @if (!isLoading && errorMessage) {
          <div class="mb-5 px-4 py-3 bg-red-50 border border-red-200 rounded-2xl text-sm text-red-600">
            {{ errorMessage }}
          </div>
        }

        <button
          type="button"
          (click)="goToLogin()"
          class="w-full py-3.5 bg-[#e8570a] hover:bg-[#d4621f] rounded-3xl text-white font-bold text-base transition-all">
          Go to sign in
        </button>
      </section>
    </main>
  `,
})
export class VerifyEmailPageComponent implements OnInit {
  isLoading = true;
  successMessage = '';
  errorMessage = '';

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private http: HttpClient,
    private authService: AuthService,
    private userService: UserService,
    private cdr: ChangeDetectorRef,
  ) {}

  ngOnInit(): void {
    const email = this.route.snapshot.queryParamMap.get('email') ?? '';
    const token = this.route.snapshot.queryParamMap.get('token') ?? '';

    if (!email || !token) {
      this.isLoading = false;
      this.errorMessage = 'Verification link is missing required information.';
      this.cdr.markForCheck();
      return;
    }

    this.http.post<AuthActionResponse>('/api/auth/verify-email', { email, token }).subscribe({
      next: (response) => {
        this.isLoading = false;
        this.successMessage = response.message || 'Email verified successfully.';
        this.cdr.markForCheck();

        if (response.accessToken) {
          this.signInAndContinue(response);
        }
      },
      error: (err: HttpErrorResponse) => {
        this.isLoading = false;
        this.errorMessage = err.error?.message || 'Verification link is invalid or expired.';
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
}
