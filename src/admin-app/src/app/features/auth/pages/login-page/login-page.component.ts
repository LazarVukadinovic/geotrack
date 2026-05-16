import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule, NgModel } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../../../core/services/auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './login-page.component.html',
  styleUrl: './login-page.component.css',
})
export class LoginComponent {
  readonly email        = signal('');
  readonly password     = signal('');
  readonly rememberMe   = signal(false);
  readonly showPassword = signal(false);
  readonly isLoading    = signal(false);
  readonly authError    = signal<string | null>(null);

  private focusedElement: HTMLInputElement | null = null;
  private userInitiatedFocus = false;

  constructor(
    private readonly router: Router,
    private readonly route: ActivatedRoute,
    private readonly authService: AuthService,
  ) {}

  togglePassword(): void {
    this.showPassword.update(v => !v);
  }

  onValueChange(_value: unknown, control: NgModel): void {
    Promise.resolve().then(() => {
      const el = control.valueAccessor
        ? (this.focusedElement ?? this.getElementByName(control.name))
        : null;
      if (!el) return;
      this.applyFieldStyle(el, control);
    });
  }

  onFieldFocus(event: Event, control: NgModel): void {
    const el = event.target as HTMLInputElement;
    this.focusedElement = el;
    this.userInitiatedFocus = true;
    this.applyFieldStyle(el, control);
  }

  onFieldBlur(event: Event, control: NgModel): void {
    const el = event.target as HTMLInputElement;
    this.focusedElement = null;
    this.userInitiatedFocus = false;
    el.style.boxShadow = 'none';
    el.style.borderColor = '';
  }

  private applyFieldStyle(el: HTMLInputElement, control: NgModel): void {
    if (!this.userInitiatedFocus) return;
    const isInvalid = control.invalid && control.touched;
    el.style.boxShadow = isInvalid
      ? '0 0 0 3px rgba(239,68,68,0.15)'
      : '0 0 0 3px rgba(21,101,216,0.12)';
    el.style.borderColor = isInvalid ? '#f87171' : '#1565d8';
  }

  private getElementByName(name: string): HTMLInputElement | null {
    return document.querySelector<HTMLInputElement>(`input[name="${name}"]`);
  }

  onSubmit(): void {
    this.authError.set(null);
    if (!this.email() || !this.password()) return;
    this.isLoading.set(true);

    this.authService.login(this.email(), this.password(), this.rememberMe()).subscribe({
      next: () => {
        this.isLoading.set(false);
        this.router.navigateByUrl(this.safeReturnUrl() ?? this.authService.getHomeRoute());
      },
      error: (err: Error) => {
        this.isLoading.set(false);
        this.authError.set(err.message ?? 'Invalid email or password. Please try again.');
      },
    });
  }

  private safeReturnUrl(): string | null {
    const value = this.route.snapshot.queryParamMap.get('returnUrl');
    if (!value || !value.startsWith('/') || value.startsWith('//') || value.startsWith('/login')) {
      return null;
    }

    return value;
  }
  //temporarily
  devLoginAs(role: 'Administrator' | 'National Manager' | 'Regional Manager'): void {
    this.authService.devLoginAs(role);
    this.router.navigateByUrl(this.authService.getHomeRoute());
  }

  setSubmitHover(event: Event, isHover: boolean): void {
    const btn = event.target as HTMLButtonElement;
    if (this.isLoading()) return;
    btn.style.background = isHover
      ? 'linear-gradient(135deg, #1250b0 0%, #1575d0 100%)'
      : 'linear-gradient(135deg, #1565d8 0%, #1a86e8 100%)';
    btn.style.boxShadow = isHover ? '0 4px 16px rgba(21,101,216,0.35)' : 'none';
  }
}
