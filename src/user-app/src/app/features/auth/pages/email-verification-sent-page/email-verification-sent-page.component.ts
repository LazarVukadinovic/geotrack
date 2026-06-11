import { CommonModule } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, OnDestroy } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { finalize } from 'rxjs';

interface AuthMessageResponse {
  message: string;
  retryAfterSeconds?: number | null;
}

@Component({
  selector: 'app-email-verification-sent-page',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <main class="min-h-full bg-white flex flex-col items-center px-6 py-10">
      <section class="w-full max-w-md bg-[#fafaf9] rounded-[2rem] px-7 py-8 shadow-sm border border-slate-100 text-center">
        <img src="images/logo.png" alt="GeoTrack" class="w-20 h-20 object-contain mx-auto mb-4" />
        <h1 class="text-2xl font-bold text-[#0f172a] tracking-tight mb-3">Check your email</h1>
        <p class="text-base text-slate-600 leading-relaxed mb-5">
          We sent a verification link to
          <strong class="text-slate-800 break-all">{{ email || 'your email address' }}</strong>.
          Open that link before signing in.
        </p>

        @if (message) {
          <div class="mb-4 px-4 py-3 bg-green-50 border border-green-200 rounded-2xl text-sm text-green-700">
            {{ message }}
          </div>
        }

        @if (errorMessage) {
          <div class="mb-4 px-4 py-3 bg-red-50 border border-red-200 rounded-2xl text-sm text-red-600">
            {{ errorMessage }}
          </div>
        }

        <button
          type="button"
          (click)="resend()"
          [disabled]="isLoading || !email || cooldownSeconds > 0"
          class="w-full py-3.5 bg-[#e8570a] hover:bg-[#d4621f] rounded-3xl text-white font-bold text-base transition-all disabled:opacity-60 disabled:pointer-events-none">
          @if (isLoading) {
            Sending…
          } @else if (cooldownSeconds > 0) {
            Resend in {{ cooldownSeconds }}s
          } @else {
            Resend verification email
          }
        </button>

        <button type="button" (click)="goToLogin()" class="mt-5 text-sm font-semibold text-slate-500 hover:text-[#e8570a]">
          Back to sign in
        </button>
      </section>
    </main>
  `,
})
export class EmailVerificationSentPageComponent implements OnDestroy {
  email = '';
  isLoading = false;
  message = '';
  errorMessage = '';
  cooldownSeconds = 0;

  private cooldownTimerId: number | null = null;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private http: HttpClient,
    private cdr: ChangeDetectorRef,
  ) {
    this.email = this.route.snapshot.queryParamMap.get('email') ?? '';
  }

  ngOnDestroy(): void {
    this.clearCooldownTimer();
  }

  resend(): void {
    if (!this.email || this.isLoading || this.cooldownSeconds > 0) return;

    this.isLoading = true;
    this.message = '';
    this.errorMessage = '';

    this.http
      .post<AuthMessageResponse>('/api/auth/resend-verification', { email: this.email })
      .pipe(
        finalize(() => {
          this.isLoading = false;
          this.cdr.markForCheck();
        }),
      )
      .subscribe({
        next: (response) => {
          this.message = response.message || 'Verification email sent.';
          this.startCooldown(response.retryAfterSeconds ?? 60);
          this.cdr.markForCheck();
        },
        error: (err: HttpErrorResponse) => {
          this.errorMessage = err.error?.message || 'Could not send a verification email. Please try again.';
          this.cdr.markForCheck();
        },
      });
  }

  goToLogin(): void {
    this.router.navigate(['/login']);
  }

  private startCooldown(seconds: number): void {
    this.clearCooldownTimer();
    this.cooldownSeconds = Math.max(0, Math.ceil(seconds));

    if (this.cooldownSeconds <= 0) return;

    this.cooldownTimerId = window.setInterval(() => {
      this.cooldownSeconds = Math.max(0, this.cooldownSeconds - 1);
      if (this.cooldownSeconds <= 0) {
        this.clearCooldownTimer();
      }
      this.cdr.markForCheck();
    }, 1000);
  }

  private clearCooldownTimer(): void {
    if (this.cooldownTimerId !== null) {
      window.clearInterval(this.cooldownTimerId);
      this.cooldownTimerId = null;
    }
  }
}
