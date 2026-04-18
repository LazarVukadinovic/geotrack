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

  amenityMap: Record<string, { label: string; emoji: string }> = {
    pool: { label: 'Pool', emoji: '🏊' }, wifi: { label: 'High-speed Wi-Fi', emoji: '📶' },
    breakfast: { label: 'Breakfast Included', emoji: '☕' }, parking: { label: 'Free Parking', emoji: '🅿️' },
    spa: { label: 'Spa', emoji: '💆' }, gym: { label: 'Gym', emoji: '🏋️' },
    dining: { label: 'Dining', emoji: '🍽️' }, ac: { label: 'Air Conditioning', emoji: '❄️' },
  };

  partnerMap: Record<string, { label: string; emoji: string }> = {
    booking: { label: 'Booking.com', emoji: '🌐' },
    airbnb: { label: 'Airbnb', emoji: '🏠' },
    hotel: { label: 'Hotel Website', emoji: '📋' },
  };

  getAmenityLabel(id: string): string { return this.amenityMap[id]?.label ?? id; }
  getAmenityEmoji(id: string): string { return this.amenityMap[id]?.emoji ?? '✓'; }
  getPartnerLabel(id: string): string { return this.partnerMap[id]?.label ?? id; }
  getPartnerEmoji(id: string): string { return this.partnerMap[id]?.emoji ?? '🔗'; }
}