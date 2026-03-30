import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';

@Component({
  selector: 'app-login',
  imports: [CommonModule, FormsModule],
  templateUrl: './login.html',
  styleUrl: './login.css',
})
export class Login {
  email = '';
  password = '';
  showPassword = false;
  rememberMe = false;

  constructor(private router: Router) {}

  onSignIn(): void {
    // Add authentication logic here
    console.log('Sign in:', this.email);
  }

  goToRegister(): void {
    this.router.navigate(['/register']);
  }
}