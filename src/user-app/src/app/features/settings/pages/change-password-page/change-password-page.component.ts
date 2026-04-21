import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
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

  protected readonly currentPassword = signal('');
  protected readonly newPassword = signal('');
  protected readonly confirmPassword = signal('');
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
      },
      error: (error: { error?: { message?: string } }) => {
        this.isSaving.set(false);
        this.errorMessage.set(error.error?.message ?? 'We could not update your password right now.');
      },
    });
  }
}
