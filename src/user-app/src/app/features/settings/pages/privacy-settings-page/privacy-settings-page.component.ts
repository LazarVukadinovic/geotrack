import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { DestinationService } from '../../../../core/services/destination.service';
import { LocationService } from '../../../../core/services/location.service';
import { SettingsSubpageShellComponent } from '../../components/settings-subpage-shell/settings-subpage-shell.component';

@Component({
  selector: 'app-privacy-settings-page',
  standalone: true,
  imports: [CommonModule, SettingsSubpageShellComponent],
  templateUrl: './privacy-settings-page.component.html',
  styleUrl: './privacy-settings-page.component.css',
})
export class PrivacySettingsPageComponent {
  private readonly locationService = inject(LocationService);
  private readonly destinationService = inject(DestinationService);
  private readonly router = inject(Router);

  protected readonly location = this.locationService.location;
  protected readonly sourceLabel = this.locationService.sourceLabel;
  protected readonly hasCompletedLocationEntry = this.locationService.hasCompletedLocationEntry;
  protected readonly feedbackMessage = signal('');

  protected readonly locationStatus = computed(() => {
    const location = this.location();

    if (location) {
      return location.displayLabel;
    }

    return this.hasCompletedLocationEntry()
      ? 'Saved location cleared'
      : 'No saved location on this device';
  });

  protected clearSavedLocation(): void {
    this.locationService.clearSavedLocation();
    this.destinationService.clear();
    this.feedbackMessage.set('Location session data and destination history were cleared from this device.');
  }

  protected openInterests(): void {
    void this.router.navigate(['/settings/interests'], { queryParams: { from: 'settings' } });
  }

  protected openLegal(): void {
    void this.router.navigate(['/settings/terms-and-privacy']);
  }

  protected openSupport(): void {
    void this.router.navigate(['/settings/contact-support']);
  }
}
