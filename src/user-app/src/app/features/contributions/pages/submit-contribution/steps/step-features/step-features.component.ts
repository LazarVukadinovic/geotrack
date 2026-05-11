import { Component, input, output, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  CONTRIBUTION_AMENITY_OPTIONS,
  normalizeContributionAmenityIds,
} from '../../../../services/contribution-amenities';

export interface FeaturesData {
  tags: string[];
  amenities: string[];
}

@Component({
  selector: 'app-step-features',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './step-features.component.html',
})
export class StepFeaturesComponent {
  data = input<FeaturesData | null>(null);
  saved = output<FeaturesData>();
  back = output<void>();
  changed = output<FeaturesData>();

  characteristicTags = [
  'Luxury', 'Budget-Friendly',
  'Family Friendly', 'Adults Only', 'Pet Friendly',
  'Beachfront','Romantic', 'Mountain View', 'City Center', 'Countryside',
  'Eco-Friendly', 'Business',
  'Lakefront', 'Historic'
];

  amenities = CONTRIBUTION_AMENITY_OPTIONS;

  selectedTags: string[] = [];
  selectedAmenities: string[] = [];

    constructor() {
    effect(() => {
      const d = this.data();
      if (d) {
        this.selectedTags = [...(d.tags ?? [])];
        this.selectedAmenities = normalizeContributionAmenityIds(d.amenities);
      }
    });
  }

  toggleTag(tag: string): void {
    if (this.isTagSelected(tag)) this.selectedTags = this.selectedTags.filter(t => t !== tag);
    else this.selectedTags = [...this.selectedTags, tag];
    this.changed.emit(this.currentData());
  }

  isTagSelected(tag: string): boolean { return this.selectedTags.includes(tag); }

  toggleAmenity(id: string): void {
    if (this.isAmenitySelected(id)) this.selectedAmenities = this.selectedAmenities.filter(a => a !== id);
    else this.selectedAmenities = [...this.selectedAmenities, id];
    this.changed.emit(this.currentData());
  }

  isAmenitySelected(id: string): boolean { return this.selectedAmenities.includes(id); }

  onContinue(): void {
    this.saved.emit(this.currentData());
  }

  private currentData(): FeaturesData {
    return {
      tags: this.selectedTags,
      amenities: normalizeContributionAmenityIds(this.selectedAmenities),
    };
  }
}
