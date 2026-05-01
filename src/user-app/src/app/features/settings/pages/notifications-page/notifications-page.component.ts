import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { SettingsSubpageShellComponent } from '../../components/settings-subpage-shell/settings-subpage-shell.component';
import { NotificationPreferences } from '../../models/user-settings.model';
import { UserSettingsService } from '../../services/user-settings.service';

interface NotificationToggle {
  key: keyof NotificationPreferences;
  title: string;
  description: string;
}

const DEFAULT_PREFERENCES: NotificationPreferences = {
  generalNotifications: true,
  travelRecommendations: true,
  nearbyEvents: true,
  collectionUpdates: true,
  emailNotifications: false,
};

const NOTIFICATION_TOGGLES: NotificationToggle[] = [
  {
    key: 'generalNotifications',
    title: 'General notifications',
    description: 'Master switch for the travel updates GeoTrack prepares for your account.',
  },
  {
    key: 'travelRecommendations',
    title: 'Travel recommendations',
    description: 'Fresh destination ideas based on your interests, collections, and current city.',
  },
  {
    key: 'nearbyEvents',
    title: 'Nearby events',
    description: 'Highlights about concerts, festivals, and local happenings around the places you browse.',
  },
  {
    key: 'collectionUpdates',
    title: 'Favorites and collections',
    description: 'Useful updates tied to saved places, curated lists, and trip planning progress.',
  },
  {
    key: 'emailNotifications',
    title: 'Email summaries',
    description: 'Save your preference now so trip emails can follow the categories you actually care about.',
  },
];

@Component({
  selector: 'app-notifications-page',
  standalone: true,
  imports: [CommonModule, SettingsSubpageShellComponent],
  templateUrl: './notifications-page.component.html',
  styleUrl: './notifications-page.component.css',
})
export class NotificationsPageComponent {
  private readonly settingsService = inject(UserSettingsService);

  protected readonly toggles = NOTIFICATION_TOGGLES;
  protected readonly draft = signal<NotificationPreferences>(DEFAULT_PREFERENCES);
  protected readonly saved = signal<NotificationPreferences>(DEFAULT_PREFERENCES);
  protected readonly isLoading = signal(true);
  protected readonly isSaving = signal(false);
  protected readonly feedbackMessage = signal('');
  protected readonly errorMessage = signal('');

  protected readonly isDirty = computed(() => {
    const draft = this.draft();
    const saved = this.saved();

    return draft.generalNotifications !== saved.generalNotifications ||
      draft.travelRecommendations !== saved.travelRecommendations ||
      draft.nearbyEvents !== saved.nearbyEvents ||
      draft.collectionUpdates !== saved.collectionUpdates ||
      draft.emailNotifications !== saved.emailNotifications;
  });

  constructor() {
    this.settingsService.getNotificationPreferences().subscribe({
      next: (preferences) => {
        this.draft.set(preferences);
        this.saved.set(preferences);
        this.isLoading.set(false);
      },
      error: () => {
        this.isLoading.set(false);
        this.errorMessage.set('We could not load your notification preferences right now.');
      },
    });
  }

  protected toggle(key: keyof NotificationPreferences): void {
    this.feedbackMessage.set('');
    this.errorMessage.set('');
    this.draft.update((current) => ({
      ...current,
      [key]: !current[key],
    }));
  }

  protected save(): void {
    if (!this.isDirty() || this.isSaving()) {
      return;
    }

    this.isSaving.set(true);
    this.feedbackMessage.set('');
    this.errorMessage.set('');

    this.settingsService.updateNotificationPreferences(this.draft()).subscribe({
      next: (preferences) => {
        this.isSaving.set(false);
        this.saved.set(preferences);
        this.draft.set(preferences);
        this.feedbackMessage.set('Notification preferences saved.');
      },
      error: (error: { error?: { message?: string } }) => {
        this.isSaving.set(false);
        this.errorMessage.set(error.error?.message ?? 'We could not save your preferences right now.');
      },
    });
  }

  protected isEnabled(key: keyof NotificationPreferences): boolean {
    return this.draft()[key];
  }
}
