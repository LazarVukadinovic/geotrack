import { CommonModule, Location } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../../../../core/services/auth.service';
import { SettingsSubpageShellComponent } from '../../components/settings-subpage-shell/settings-subpage-shell.component';
import { UserSettingsService } from '../../services/user-settings.service';

@Component({
  selector: 'app-change-password-page',
  standalone: true,
  imports: [CommonModule, SettingsSubpageShellComponent],
  templateUrl: './change-password-page.component.html',
  styleUrl: './change-password-page.component.css',
})
export class ChangePasswordPageComponent {
  private readonly settingsService = inject(UserSettingsService);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly location = inject(Location);

  protected readonly currentPassword = signal('');
  protected readonly newPassword = signal('');
  protected readonly confirmPassword = signal('');
  protected readonly showCurrentPassword = signal(false);
  protected readonly showNewPassword = signal(false);
  protected readonly showConfirmPassword = signal(false);
  protected readonly isSaving = signal(false);
  protected readonly hasSubmitted = signal(false);
  protected readonly feedbackMessage = signal('');
  protected readonly errorMessage = signal('');

  protected readonly currentPasswordError = computed(() => {
    if (!this.hasSubmitted() && !this.currentPassword()) return '';
    return this.currentPassword().trim().length === 0
      ? 'Enter your current password.'
      : '';
  });

  protected readonly newPasswordError = computed(() => {
    if (!this.hasSubmitted() && !this.newPassword()) return '';

    if (this.newPassword().trim().length === 0) {
      return 'Enter a new password.';
    }

    if (this.newPassword().length < 8) {
      return 'Use at least 8 characters.';
    }

    if (!/\d/.test(this.newPassword())) {
      return 'Include at least one number.';
    }

    if (!/[!@#$%^&*(),.?":{}|<>]/.test(this.newPassword())) {
      return 'Include at least one special character.';
    }

    if (this.newPassword() === this.currentPassword() && this.currentPassword().length > 0) {
      return 'Choose a password you are not already using.';
    }

    return '';
  });

  protected readonly confirmPasswordError = computed(() => {
    if (!this.hasSubmitted() && !this.confirmPassword()) return '';

    if (this.confirmPassword().trim().length === 0) {
      return 'Confirm your new password.';
    }

    return this.confirmPassword() !== this.newPassword()
      ? 'Passwords do not match.'
      : '';
  });

  protected readonly canSubmit = computed(() =>
    !this.isSaving() &&
    this.currentPasswordError() === '' &&
    this.newPasswordError() === '' &&
    this.confirmPasswordError() === '' &&
    this.currentPassword().trim().length > 0 &&
    this.newPassword().trim().length > 0 &&
    this.confirmPassword().trim().length > 0,
  );

  protected save(): void {
    this.hasSubmitted.set(true);
    this.feedbackMessage.set('');
    this.errorMessage.set('');

    if (!this.canSubmit()) {
      return;
    }

    this.isSaving.set(true);

    this.settingsService.changePassword({
      currentPassword: this.currentPassword(),
      newPassword: this.newPassword(),
    }).subscribe({
      next: (response) => {
        this.isSaving.set(false);
        this.feedbackMessage.set(response.message || 'Password updated successfully.');
        this.currentPassword.set('');
        this.newPassword.set('');
        this.confirmPassword.set('');
        this.hasSubmitted.set(false);
        window.setTimeout(() => {
          this.authService.clearSession();
          void this.router.navigate(['/login']);
        }, 1200);
      },
      error: (error: { error?: { message?: string } }) => {
        this.isSaving.set(false);
        this.errorMessage.set(error.error?.message ?? 'We could not update your password right now.');
      },
    });
  }

  protected cancel(): void {
    const navigationId = Number(globalThis.history?.state?.navigationId ?? 0);

    if (navigationId > 1) {
      this.location.back();
      return;
    }

    void this.router.navigate(['/settings']);
  }

  protected toggleCurrentPasswordVisibility(): void {
    this.showCurrentPassword.update(value => !value);
  }

  protected toggleNewPasswordVisibility(): void {
    this.showNewPassword.update(value => !value);
  }

  protected toggleConfirmPasswordVisibility(): void {
    this.showConfirmPassword.update(value => !value);
  }
}
