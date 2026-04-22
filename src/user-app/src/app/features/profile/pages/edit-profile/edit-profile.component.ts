import { Component, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { UserService } from '../../../../core/services/user.service';

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

interface UpdateAvatarResponse {
  avatarUrl: string;
  updatedAt?: string;
}

const MAX_AVATAR_FILE_BYTES = 5 * 1024 * 1024;

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
  avatarUrl = '';
  isSaving  = false;
  isLoading = true;
  isUploadingAvatar = false;
  saveError = '';
  avatarError = '';
  saveSuccess = false;
  readonly acceptedAvatarTypes = 'image/jpeg,image/png,image/webp,image/gif';
  private returnTo = '/profile';

  constructor(
    private router: Router,
    private route: ActivatedRoute,
    private http: HttpClient,
    private cdr: ChangeDetectorRef,
    private userService: UserService,
  ) {
    this.route.queryParams.subscribe(p => {
      if (p['from']) this.returnTo = '/' + p['from'];
    });
    this.http.get<MeResponse>('/api/user/me').subscribe({
      next: (res) => {
        const name = `${res.firstName} ${res.lastName}`.trim();
        this.fullName = name || res.email;
        this.email = res.email;
        this.phone = res.phoneNumber ?? '';
        this.location = res.location ?? '';
        this.avatarUrl = res.avatarUrl ?? '';
        this.isLoading = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.isLoading = false;
        this.saveError = 'Could not load your profile. Please try again.';
        this.cdr.detectChanges();
      },
    });
  }

  onAvatarSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';

    if (!file || this.isUploadingAvatar) return;

    this.avatarError = '';

    if (file.size > MAX_AVATAR_FILE_BYTES) {
      this.avatarError = 'Profile photo must be 5 MB or smaller.';
      this.cdr.detectChanges();
      return;
    }

    if (file.type && !file.type.startsWith('image/')) {
      this.avatarError = 'Please choose a valid image file.';
      this.cdr.detectChanges();
      return;
    }

    const formData = new FormData();
    formData.append('avatar', file);

    this.isUploadingAvatar = true;
    this.cdr.detectChanges();

    this.http.put<UpdateAvatarResponse>('/api/user/me/avatar', formData).subscribe({
      next: (response) => {
        this.avatarUrl = response.avatarUrl ?? '';
        this.isUploadingAvatar = false;
        this.avatarError = '';
        this.userService.patchProfile({ avatarUrl: this.avatarUrl });
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.isUploadingAvatar = false;
        this.avatarError = err?.error?.message || 'Failed to upload profile photo. Please try again.';
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
        // Refresh the canonical UserService so Profile/Settings reflect the
        // change immediately without a page reload. Navigate once the
        // profile reload completes (success or failure).
        this.userService.load().subscribe({
          next: () => this.finishSaveSuccess(),
          error: () => this.finishSaveSuccess(),
        });
      },
      error: () => {
        this.isSaving = false;
        this.saveError = 'Failed to save changes. Please try again.';
        this.cdr.detectChanges();
      },
    });
  }

  private finishSaveSuccess(): void {
    this.isSaving = false;
    this.saveSuccess = true;
    this.cdr.detectChanges();
    setTimeout(() => this.router.navigate([this.returnTo]), 600);
  }

  goBack(): void {
    this.router.navigate([this.returnTo]);
  }
}
