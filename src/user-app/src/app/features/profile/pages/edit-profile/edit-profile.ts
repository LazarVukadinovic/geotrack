import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';

@Component({
  selector: 'app-edit-profile',
  imports: [CommonModule, FormsModule],
  templateUrl: './edit-profile.html',
  styleUrl: './edit-profile.css',
})
export class EditProfile {
  fullName  = 'Marko Petrović';
  email     = 'marko.p@example.com';
  phone     = '';
  location  = 'San Francisco, CA';
  isSaving  = false;

  constructor(private router: Router) {
    const saved = localStorage.getItem('userProfile');
    if (saved) {
      const p = JSON.parse(saved);
      this.fullName = p.fullName ?? this.fullName;
      this.email    = p.email    ?? this.email;
      this.phone    = p.phone    ?? this.phone;
      this.location = p.location ?? this.location;
    }
  }

  saveChanges(): void {
    if (this.isSaving) return;
    this.isSaving = true;

    // Simulacija API poziva
    setTimeout(() => {
      localStorage.setItem('userProfile', JSON.stringify({
        fullName:  this.fullName,
        email:     this.email,
        phone:     this.phone,
        location:  this.location,
      }));
      this.isSaving = false;
      this.router.navigate(['/profile']);
    }, 1000);
  }

  goBack(): void {
    this.router.navigate(['/profile']);
  }
}