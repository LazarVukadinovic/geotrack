import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';

@Component({
  selector: 'app-register-page',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './register-page.component.html',
  styleUrl: './register-page.component.css',
})
export class RegisterPageComponent {
  email = '';
  password = '';
  repeatPassword = '';
  showPassword = false;
  showRepeatPassword = false;
  submitted = false;

  req = {
    minLength: false,
    hasNumber: false,
    hasSpecial: false,
  };

  constructor(private router: Router) {}

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
    if (!this.isValid) return;
    this.router.navigate(['/profile/interests']);
  }

  goBack(): void {
    this.router.navigate(['/profile']);
  }

  goToLogin(): void {
    this.router.navigate(['/profile']);
  }
}
