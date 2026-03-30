import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';

@Component({
  selector: 'app-register',
  imports: [CommonModule, FormsModule],
  templateUrl: './register.html',
  styleUrl: './register.css',
})
export class Register {
  email = '';
  password = '';
  repeatPassword = '';
  showPassword = false;
  showRepeatPassword = false;

  req = {
    minLength: false,
    hasNumber: false,
    hasSpecial: false,
  };

  constructor(private router: Router) {}

  checkPasswordRequirements(): void {
    this.req.minLength = this.password.length >= 8;
    this.req.hasNumber = /\d/.test(this.password);
    this.req.hasSpecial = /[!@#$%^&*(),.?":{}|<>]/.test(this.password);
  }

  onContinue(): void {
    if (!this.email || !this.password || this.password !== this.repeatPassword) return;
    this.router.navigate(['/interests']);
  }

  goBack(): void {
    this.router.navigate(['/login']);
  }

  goToLogin(): void {
    this.router.navigate(['/login']);
  }
}