import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './login-page.component.html',
  styleUrl: './login-page.component.css'
})
export class LoginComponent {
  // Using Angular signals — consistent with user-app patterns
  readonly email         = signal('');
  readonly password      = signal('');
  readonly rememberMe    = signal(false);
  readonly showPassword  = signal(false);
  readonly isLoading     = signal(false);

  constructor(private readonly router: Router) {}

  togglePassword(): void {
    this.showPassword.update(v => !v);
  }

  onSubmit(): void {
    if (!this.email() || !this.password()) return;
    this.isLoading.set(true);
    // TODO: replace with real AuthService call
    setTimeout(() => {
      this.isLoading.set(false);
      this.router.navigate(['/dashboard']);
    }, 1200);
  }

  setSubmitHover(event: Event, isHover: boolean): void {
    const btn = event.target as HTMLButtonElement;
    if (this.isLoading() || !this.email() || !this.password()) return;
    btn.style.background = isHover
      ? 'linear-gradient(135deg, #1250b0 0%, #1575d0 100%)'
      : 'linear-gradient(135deg, #1565d8 0%, #1a86e8 100%)';
    btn.style.boxShadow = isHover ? '0 4px 16px rgba(21,101,216,0.35)' : 'none';
  }
}