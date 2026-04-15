import { Component, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { HttpClient } from '@angular/common/http';

interface MeResponse {
  id: number;
  username: string;
  email: string;
  firstName: string;
  lastName: string;
  phoneNumber: string;
  avatarUrl?: string;
  location?: string;
}

@Component({
  selector: 'app-edit-profile',
  imports: [CommonModule, FormsModule],
  templateUrl: './edit-profile.component.html',
  styleUrl: './edit-profile.component.css',
})
export class EditProfileComponent {
  fullName  = '';
  email     = '';
  phone     = '';
  location  = '';
  isSaving  = false;
  isLoading = true;
  saveError = '';
  saveSuccess = false;
  private returnTo = '/profile';

  constructor(
    private router: Router,
    private route: ActivatedRoute,
    private http: HttpClient,
    private cdr: ChangeDetectorRef,
  ) {
    this.route.queryParams.subscribe(p => {
      if (p['from']) this.returnTo = '/' + p['from'];
    });
    // Load profile from backend
    this.http.get<MeResponse>('/api/user/me').subscribe({
      next: (res) => {
        const name = `${res.firstName} ${res.lastName}`.trim();
        this.fullName = name || res.email;
        this.email = res.email;
        this.phone = res.phoneNumber ?? '';
        this.location = res.location ?? '';
        this.isLoading = false;
        this.cdr.detectChanges();
      },
      error: () => {
        const saved = localStorage.getItem('userProfile');
        if (saved) {
          const p = JSON.parse(saved);
          this.fullName = p.fullName ?? '';
          this.email = p.email ?? '';
          this.phone = p.phone ?? '';
          this.location = p.location ?? '';
        }
        this.isLoading = false;
        this.cdr.detectChanges();
      },
    });
  }

  saveChanges(): void {
    if (this.isSaving) return;
    this.isSaving = true;
    this.saveError = '';
    this.saveSuccess = false;
    this.cdr.detectChanges();

    // Split fullName into firstName + lastName for backend
    const parts = this.fullName.trim().split(/\s+/);
    const firstName = parts[0] ?? '';
    const lastName = parts.slice(1).join(' ');

    this.http.put<MeResponse>('/api/user/me', {
      firstName,
      lastName,
      phoneNumber: this.phone,
      location: this.location,
    }).subscribe({
      next: () => {
        // Also persist to localStorage for fallback
        localStorage.setItem('userProfile', JSON.stringify({
          fullName: this.fullName,
          email: this.email,
          phone: this.phone,
          location: this.location,
        }));
        this.isSaving = false;
        this.saveSuccess = true;
        this.cdr.detectChanges();
        // Brief delay to show success before navigating
        setTimeout(() => this.router.navigate([this.returnTo]), 600);
      },
      error: () => {
        this.isSaving = false;
        this.saveError = 'Failed to save changes. Please try again.';
        this.cdr.detectChanges();
      },
    });
  }

  goBack(): void {
    this.router.navigate([this.returnTo]);
  }
}