import { Component, input, output } from '@angular/core';
import { CommonModule } from '@angular/common';

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

  characteristicTags = ['Hotel', 'Luxury', 'Spa', 'Family Friendly', 'Beachfront', 'Boutique', 'City Center', 'Eco-Friendly', 'Adults Only', 'Pet Friendly'];

  amenities = [
    { id: 'pool', label: 'POOL' },
    { id: 'wifi', label: 'WIFI' },
    { id: 'breakfast', label: 'BREAKFAST' },
    { id: 'parking', label: 'PARKING' },
    { id: 'spa', label: 'SPA' },
    { id: 'gym', label: 'GYM' },
    { id: 'dining', label: 'DINING' },
    { id: 'ac', label: 'A/C' },
  ];

  selectedTags: string[] = [];
  selectedAmenities: string[] = [];

  ngOnInit() {
    if (this.data()) {
      this.selectedTags = [...(this.data()?.tags ?? [])];
      this.selectedAmenities = [...(this.data()?.amenities ?? [])];
    }
  }

  toggleTag(tag: string): void {
    if (this.isTagSelected(tag)) this.selectedTags = this.selectedTags.filter(t => t !== tag);
    else this.selectedTags = [...this.selectedTags, tag];
  }

  isTagSelected(tag: string): boolean { return this.selectedTags.includes(tag); }

  toggleAmenity(id: string): void {
    if (this.isAmenitySelected(id)) this.selectedAmenities = this.selectedAmenities.filter(a => a !== id);
    else this.selectedAmenities = [...this.selectedAmenities, id];
  }

  isAmenitySelected(id: string): boolean { return this.selectedAmenities.includes(id); }

  onContinue(): void {
    this.saved.emit({ tags: this.selectedTags, amenities: this.selectedAmenities });
  }
}