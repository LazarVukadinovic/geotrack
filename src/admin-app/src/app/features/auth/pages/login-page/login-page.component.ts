import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule, NgModel } from '@angular/forms';
import { Router } from '@angular/router';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './login-page.component.html',
  styleUrl: './login-page.component.css'
})
export class LoginComponent {
  readonly email        = signal('');
  readonly password     = signal('');
  readonly rememberMe   = signal(false);
  readonly showPassword = signal(false);
  readonly isLoading    = signal(false);
  /** Poruka greške od servera (npr. "Invalid credentials") */
  readonly authError    = signal<string | null>(null);

  constructor(private readonly router: Router) {}

  togglePassword(): void {
    this.showPassword.update(v => !v);
  }

  onFieldFocus(event: Event, control: NgModel): void {
    const el = event.target as HTMLInputElement;
    const isInvalid = control.invalid && control.touched;
    el.style.boxShadow = isInvalid
      ? '0 0 0 3px rgba(239,68,68,0.15)'
      : '0 0 0 3px rgba(21,101,216,0.12)';
    el.style.borderColor = isInvalid ? '#f87171' : '#1565d8';
  }

  onFieldBlur(event: Event): void {
    const el = event.target as HTMLInputElement;
    el.style.boxShadow = 'none';
  }

  onSubmit(): void {
    this.authError.set(null);
    if (!this.email() || !this.password()) return;
    this.isLoading.set(true);

    // zameniti sa pravim AuthService pozivom
    // this.authService.login(this.email(), this.password()).subscribe({
    //   next: () => this.router.navigate(['/dashboard']),
    //   error: () => {
    //     this.isLoading.set(false);
    //     this.authError.set('Invalid email or password. Please try again.');
    //   }
    // });

    setTimeout(() => {
      this.isLoading.set(false);
      this.router.navigate(['/dashboard']);
    }, 1200);
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