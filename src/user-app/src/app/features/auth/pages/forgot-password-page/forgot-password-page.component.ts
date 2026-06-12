import { CommonModule } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, OnDestroy } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';

interface AuthMessageResponse {
  message: string;
  retryAfterSeconds?: number | null;
}

@Component({
  selector: 'app-forgot-password-page',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <main class="min-h-full bg-white flex flex-col items-center px-6 py-10">
      <form class="w-full max-w-md bg-[#fafaf9] rounded-[2rem] px-7 py-8 shadow-sm border border-slate-100" (ngSubmit)="submit()" novalidate>
        <img src="images/logo.png" alt="GeoTrack" class="w-20 h-20 object-contain mx-auto mb-4" />
        <h1 class="text-2xl font-bold text-[#0f172a] tracking-tight mb-2 text-center">Reset password</h1>
        <p class="text-base text-slate-600 leading-relaxed mb-6 text-center">
          Enter your email address and we'll send you a password reset link.
        </p>

        <label for="forgot-email" class="ml-2 text-[12px] font-bold tracking-[0.15em] text-slate-700 uppercase mb-2 block">Email Address</label>
        <input
          id="forgot-email"
          name="email"
          type="email"
          [(ngModel)]="email"
          autocomplete="email"
          inputmode="email"
          placeholder="name@example.com"
          class="w-full px-4 py-4 bg-white border border-slate-200 rounded-3xl text-base text-slate-700 placeholder-slate-300 outline-none focus:border-[#e8570a] focus:ring-2 focus:ring-orange-100 transition-all duration-200 mb-4"
        />

        @if (message) {
          <div class="mb-4 px-4 py-3 bg-green-50 border border-green-200 rounded-2xl text-sm text-green-700 text-center">
            {{ message }}
          </div>
        }

        @if (errorMessage) {
          <div class="mb-4 px-4 py-3 bg-red-50 border border-red-200 rounded-2xl text-sm text-red-600 text-center">
            {{ errorMessage }}
          </div>
        }

        <button
          type="submit"
          [disabled]="isLoading || cooldownSeconds > 0"
          class="w-full py-3.5 bg-[#e8570a] hover:bg-[#d4621f] rounded-3xl text-white font-bold text-base transition-all disabled:opacity-60 disabled:pointer-events-none">
          @if (isLoading) {
            Sending…
          } @else if (cooldownSeconds > 0) {
            Send again in {{ cooldownSeconds }}s
          } @else {
            Send reset link
          }
        </button>

        <button type="button" (click)="goToLogin()" class="mt-5 block mx-auto text-sm font-semibold text-slate-500 hover:text-[#e8570a]">
          Back to sign in
        </button>
      </form>
    </main>
  `,
})
export class ForgotPasswordPageComponent implements OnDestroy {
  email = '';
  isLoading = false;
  message = '';
  errorMessage = '';
  cooldownSeconds = 0;

  private cooldownTimerId: number | null = null;

  constructor(
    private router: Router,
    private http: HttpClient,
    private cdr: ChangeDetectorRef,
  ) {}

  ngOnDestroy(): void {
    this.clearCooldownTimer();
  }

  submit(): void {
    if (this.cooldownSeconds > 0) return;

    if (!this.email.trim() || this.isLoading) {
      this.errorMessage = 'Please enter your email address.';
      this.cdr.markForCheck();
      return;
    }

    this.isLoading = true;
    this.message = '';
    this.errorMessage = '';

    this.http
      .post<AuthMessageResponse>('/api/auth/forgot-password', { email: this.email.trim() })
      .pipe(
        finalize(() => {
          this.isLoading = false;
          this.cdr.markForCheck();
        }),
      )
      .subscribe({
        next: (response) => {
          this.message = response.message || 'If an account exists, a password reset link has been sent.';
          this.startCooldown(response.retryAfterSeconds ?? 60);
          this.cdr.markForCheck();
        },
        error: (err: HttpErrorResponse) => {
          this.errorMessage = err.error?.message || 'Could not request a password reset. Please try again.';
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
