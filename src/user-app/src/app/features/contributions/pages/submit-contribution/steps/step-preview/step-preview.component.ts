import { Component, input, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ContributionDraft } from '../../submit-contribution.component';

@Component({
  selector: 'app-step-preview',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './step-preview.component.html',
})
export class StepPreviewComponent {
  draft = input.required<ContributionDraft>();
  confirmed = output<void>();
  back = output<void>();

  amenityMap: Record<string, { label: string }> = {
    pool:      { label: 'Swimming Pool' },
    wifi:      { label: 'High-speed Wi-Fi' },
    breakfast: { label: 'Breakfast Included' },
    parking:   { label: 'Free Parking' },
    spa:       { label: 'Spa & Wellness' },
    gym:       { label: 'Fitness Center' },
    dining:    { label: 'On-site Dining' },
    ac:        { label: 'Air Conditioning' },
  };

  partnerMap: Record<string, { label: string }> = {
    booking: { label: 'Booking.com' },
    airbnb:  { label: 'Airbnb' },
    hotel:   { label: 'Hotel Website' },
  };

getAmenityLabel(id: string): string { return this.amenityMap[id]?.label ?? id; }
getPartnerLabel(id: string): string { return this.partnerMap[id]?.label ?? id; }
 
}