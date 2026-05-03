import { Component, inject } from '@angular/core';
import { LocationService } from '../../../core/services/location.service';

@Component({
  selector: 'app-approximate-location-sheet',
  standalone: true,
  templateUrl: './approximate-location-sheet.component.html',
  styleUrl: './approximate-location-sheet.component.scss',
})
export class ApproximateLocationSheetComponent {
  private readonly locationService = inject(LocationService);

  protected readonly visible = this.locationService.approximateDisclosureVisible;

  protected accept(): void {
    this.locationService.acceptApproximateFallback();
  }

  protected decline(): void {
    this.locationService.declineApproximateFallback();
  }
}
