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
  readonly authError    = signal<string | null>(null);

  // Keeps track of which input element is currently focused
  private focusedElement: HTMLInputElement | null = null;
  private userInitiatedFocus = false;
  constructor(private readonly router: Router) {}

  togglePassword(): void {
    this.showPassword.update(v => !v);
  }

  /**
   * FIX 1: Called on every keystroke via (ngModelChange).
   * Re-applies the correct border/shadow immediately without waiting
   * for the user to click in/out of the field.
   */
  onValueChange(_value: unknown, control: NgModel): void {
    // Wait one microtask so Angular has time to update control.valid/invalid
    // after processing the new value (validators run synchronously but
    // NgModel status updates on the next cycle).
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
    this.userInitiatedFocus = true; // ← DODAJ OVO
    this.applyFieldStyle(el, control);
  }

  onFieldBlur(event: Event, control: NgModel): void {
    const el = event.target as HTMLInputElement;
    this.focusedElement = null;
    this.userInitiatedFocus = false; // ← DODAJ OVO
    el.style.boxShadow = 'none';
    el.style.borderColor = '';
  }

  private applyFieldStyle(el: HTMLInputElement, control: NgModel): void {
    if (!this.userInitiatedFocus) return; // ← DODAJ OVO — autofill ignorišemo
    const isInvalid = control.invalid && control.touched;
    el.style.boxShadow = isInvalid
      ? '0 0 0 3px rgba(239,68,68,0.15)'
      : '0 0 0 3px rgba(21,101,216,0.12)';
    el.style.borderColor = isInvalid ? '#f87171' : '#1565d8';
  }

  /** Fallback: find the native input by name attribute when direct ref is unavailable */
  private getElementByName(name: string): HTMLInputElement | null {
    return document.querySelector<HTMLInputElement>(`input[name="${name}"]`);
  }

  onSubmit(): void {
    this.authError.set(null);
    if (!this.email() || !this.password()) return;
    this.isLoading.set(true);

    // Replace with real AuthService call:
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