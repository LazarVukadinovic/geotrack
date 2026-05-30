import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { SettingsSubpageShellComponent } from '../../components/settings-subpage-shell/settings-subpage-shell.component';
import { UserSettingsService } from '../../services/user-settings.service';

@Component({
  selector: 'app-notifications-page',
  standalone: true,
  imports: [CommonModule, SettingsSubpageShellComponent],
  templateUrl: './notifications-page.component.html',
  styleUrl: './notifications-page.component.css',
})
export class NotificationsPageComponent {
  private readonly settingsService = inject(UserSettingsService);

  protected readonly isLoading = signal(true);
  protected readonly errorMessage = signal('');

  constructor() {
    this.load();
  }

  protected load(): void {
    this.isLoading.set(true);
    this.errorMessage.set('');

    this.settingsService.getNotificationPreferences().subscribe({
      next: () => {
        this.isLoading.set(false);
      },
      error: () => {
        this.isLoading.set(false);
        this.errorMessage.set('We could not load your notification preferences right now.');
      },
    });
  }
}
