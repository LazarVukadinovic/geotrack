import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';

@Component({
  selector: 'app-login-page',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './login-page.component.html',
  styleUrl: './login-page.component.css',
})
export class LoginPageComponent {
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
    this.router.navigate(['/profile/register']);
  }
}
