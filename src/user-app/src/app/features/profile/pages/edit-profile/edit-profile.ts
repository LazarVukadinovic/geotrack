import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';

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
  private returnTo = '/profile';

  constructor(private router: Router, private route: ActivatedRoute) {
    this.route.queryParams.subscribe(p => {
      if (p['from']) this.returnTo = '/' + p['from'];
    });
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
    setTimeout(() => {
      localStorage.setItem('userProfile', JSON.stringify({
        fullName:  this.fullName,
        email:     this.email,
        phone:     this.phone,
        location:  this.location,
      }));
      this.isSaving = false;
      this.router.navigate([this.returnTo]);
    }, 1000);
  }

  goBack(): void {
    this.router.navigate([this.returnTo]);
  }
}