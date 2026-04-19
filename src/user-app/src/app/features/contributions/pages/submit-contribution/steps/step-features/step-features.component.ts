import { Component, input, output, OnInit, effect, OnDestroy } from '@angular/core';
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
  changed = output<FeaturesData>();

  characteristicTags = [
  'Luxury', 'Boutique', 'Budget-Friendly',
  'Family Friendly', 'Adults Only', 'Pet Friendly',
  'Beachfront','Romantic', 'Mountain View', 'City Center', 'Countryside',
  'Eco-Friendly', 'Business',
  'Lakefront', 'Historic'
];

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

    constructor() {
    effect(() => {
      const d = this.data();
      if (d) {
        this.selectedTags = [...(d.tags ?? [])];
        this.selectedAmenities = [...(d.amenities ?? [])];
      }
    });
  }
  // ngOnInit() {
  //   if (this.data()) {
  //     this.selectedTags = [...(this.data()?.tags ?? [])];
  //     this.selectedAmenities = [...(this.data()?.amenities ?? [])];
  //   }
  // }

  toggleTag(tag: string): void {
    if (this.isTagSelected(tag)) this.selectedTags = this.selectedTags.filter(t => t !== tag);
    else this.selectedTags = [...this.selectedTags, tag];
    this.changed.emit({ tags: this.selectedTags, amenities: this.selectedAmenities });
  }

  isTagSelected(tag: string): boolean { return this.selectedTags.includes(tag); }

  toggleAmenity(id: string): void {
    if (this.isAmenitySelected(id)) this.selectedAmenities = this.selectedAmenities.filter(a => a !== id);
    else this.selectedAmenities = [...this.selectedAmenities, id];
    this.changed.emit({ tags: this.selectedTags, amenities: this.selectedAmenities });
  }

  isAmenitySelected(id: string): boolean { return this.selectedAmenities.includes(id); }

  onContinue(): void {
    this.saved.emit({ tags: this.selectedTags, amenities: this.selectedAmenities });
  }
}