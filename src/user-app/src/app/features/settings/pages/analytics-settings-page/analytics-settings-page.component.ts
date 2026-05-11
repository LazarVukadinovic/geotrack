import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { UserService } from '../../../../core/services/user.service';
import { SettingsSubpageShellComponent } from '../../components/settings-subpage-shell/settings-subpage-shell.component';
import { AnalyticsPreferences } from '../../models/user-settings.model';
import { UserSettingsService } from '../../services/user-settings.service';

@Component({
  selector: 'app-analytics-settings-page',
  standalone: true,
  imports: [CommonModule, SettingsSubpageShellComponent],
  templateUrl: './analytics-settings-page.component.html',
  styleUrl: './analytics-settings-page.component.css',
})
export class AnalyticsSettingsPageComponent {
  private readonly settingsService = inject(UserSettingsService);
  private readonly userService = inject(UserService);
  private readonly router = inject(Router);

  protected readonly preference = signal<AnalyticsPreferences>({ analyticsEnabled: true });
  protected readonly isLoading = signal(true);
  protected readonly isSaving = signal(false);
  protected readonly feedbackMessage = signal('');
  protected readonly errorMessage = signal('');

  constructor() {
    this.settingsService.getAnalyticsPreferences().subscribe({
      next: (preference) => {
        this.applyPreference(preference);
        this.isLoading.set(false);
      },
      error: () => {
        this.isLoading.set(false);
        this.errorMessage.set('We could not load your analytics preference right now.');
      },
    });
  }

  protected toggleAnalytics(): void {
    if (this.isLoading() || this.isSaving()) {
      return;
    }

    const previous = this.preference();
    const next = { analyticsEnabled: !previous.analyticsEnabled };

    this.preference.set(next);
    this.userService.setAnalyticsEnabled(next.analyticsEnabled);
    this.isSaving.set(true);
    this.feedbackMessage.set('');
    this.errorMessage.set('');

    this.settingsService.updateAnalyticsPreferences(next).subscribe({
      next: (savedPreference) => {
        this.applyPreference(savedPreference);
        this.isSaving.set(false);
        this.feedbackMessage.set(
          savedPreference.analyticsEnabled
            ? 'Analytics is now enabled.'
            : 'Analytics is now disabled.',
        );
      },
      error: (error: { error?: { message?: string } }) => {
        this.applyPreference(previous);
        this.isSaving.set(false);
        this.errorMessage.set(error.error?.message ?? 'We could not save your analytics preference right now.');
      },
    });
  }

  protected openPrivacyPolicy(): void {
    void this.router.navigate(['/settings/terms-and-privacy']);
  }

  private applyPreference(preference: AnalyticsPreferences): void {
    this.preference.set(preference);
    this.userService.setAnalyticsEnabled(preference.analyticsEnabled);
  }
}
