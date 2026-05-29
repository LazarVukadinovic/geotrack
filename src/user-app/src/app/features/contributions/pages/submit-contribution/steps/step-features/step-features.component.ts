import { Component, computed, input, output, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  getAmenitiesForPlaceType,
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
  placeType = input<string | null | undefined>(null);
  saved = output<FeaturesData>();
  back = output<void>();
  changed = output<FeaturesData>();

  characteristicTags = [
    'Luxury', 'Budget-Friendly',
    'Family Friendly', 'Adults Only', 'Pet Friendly',
    'Beachfront', 'Romantic', 'Mountain View', 'City Center', 'Countryside',
    'Eco-Friendly', 'Business',
    'Lakefront', 'Historic',
  ];

  readonly availableAmenities = computed(() => getAmenitiesForPlaceType(this.placeType()));

  selectedTags: string[] = [];
  selectedAmenities: string[] = [];

  constructor() {
    effect(() => {
      const d = this.data();
      if (d) {
        this.selectedTags = normalizeCharacteristicTags(d.tags, this.characteristicTags);
        this.selectedAmenities = normalizeContributionAmenityIds(d.amenities);
      }
    });

    effect(() => {
      const available = this.availableAmenities().map(a => a.id);
      this.selectedAmenities = this.selectedAmenities.filter(id => available.includes(id));
    });
  }

  toggleTag(tag: string): void {
    const key = characteristicKey(tag);
    if (this.isTagSelected(tag)) this.selectedTags = this.selectedTags.filter(t => characteristicKey(t) !== key);
    else this.selectedTags = [...this.selectedTags, tag];
    this.changed.emit(this.currentData());
  }

  isTagSelected(tag: string): boolean {
    const key = characteristicKey(tag);
    return this.selectedTags.some(selected => characteristicKey(selected) === key);
  }

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
      tags: normalizeCharacteristicTags(this.selectedTags, this.characteristicTags),
      amenities: normalizeContributionAmenityIds(this.selectedAmenities),
    };
  }
}

function normalizeCharacteristicTags(
  values: readonly string[] | null | undefined,
  options: readonly string[],
): string[] {
  const optionByKey = new Map(options.map(option => [characteristicKey(option), option]));
  const normalized: string[] = [];
  const seen = new Set<string>();

  for (const value of values ?? []) {
    const key = characteristicKey(value);
    if (!key || seen.has(key)) continue;

    seen.add(key);
    normalized.push(optionByKey.get(key) ?? value.trim());
  }

  return normalized;
}

function characteristicKey(value: string): string {
  return value
    .trim()
    .normalize('NFKD')
    .replace(/[̀-ͯ]/g, '')
    .replace(/&/g, ' and ')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');
}
