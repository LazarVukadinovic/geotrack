import { Component, computed, input, output, effect, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import {
  ContributionAmenityOption,
  normalizeContributionAmenityIds,
} from '../../../../services/contribution-amenities';

export interface FeaturesData {
  tags: string[];
  amenities: string[];
}

interface ContributionFeatureOption {
  id: string;
  label: string;
}

interface ContributionFeatureOptionsResponse {
  characteristics?: ContributionFeatureOption[] | null;
  amenities?: ContributionFeatureOption[] | null;
  stayAmenities?: ContributionFeatureOption[] | null;
  characteristicIdsByPlaceType?: Record<string, string[] | null> | null;
  amenityIdsByPlaceType?: Record<string, string[] | null> | null;
}

@Component({
  selector: 'app-step-features',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './step-features.component.html',
  styleUrl: './step-features.component.css',
})
export class StepFeaturesComponent implements OnInit {
  private readonly http = inject(HttpClient);

  data = input<FeaturesData | null>(null);
  placeType = input<string | null | undefined>(null);
  saved = output<FeaturesData>();
  back = output<void>();
  changed = output<FeaturesData>();

  private readonly backendAmenities = signal<ContributionAmenityOption[] | null>(null);
  private readonly backendAmenityIdsByPlaceType = signal<Record<string, string[]> | null>(null);
  private readonly backendCharacteristics = signal<string[]>([]);
  private readonly backendCharacteristicIdsByPlaceType = signal<Record<string, string[]> | null>(null);

  readonly availableCharacteristics = computed(() => {
    const characteristics = this.backendCharacteristics();
    const idsByType = this.backendCharacteristicIdsByPlaceType();
    const typeKey = characteristicKey(this.placeType() ?? '');
    const ids = idsByType?.[typeKey];

    if (!ids || ids.length === 0) {
      return characteristics;
    }

    const allowed = new Set(ids);
    return characteristics.filter(tag => allowed.has(characteristicKey(tag)));
  });

  readonly availableAmenities = computed(() => {
    const backendAmenities = this.backendAmenities();
    const backendIdsByType = this.backendAmenityIdsByPlaceType();

    if (!backendAmenities || backendAmenities.length === 0) {
      return [];
    }

    const typeKey = characteristicKey(this.placeType() ?? '');
    const ids = backendIdsByType?.[typeKey];
    if (!ids || ids.length === 0) {
      return backendAmenities;
    }

    const allowed = new Set(normalizeContributionAmenityIds(ids));
    return backendAmenities.filter(amenity => allowed.has(normalizeContributionAmenityIds([amenity.id])[0] ?? amenity.id));
  });

  selectedTags: string[] = [];
  selectedAmenities: string[] = [];

  ngOnInit(): void {
    this.loadFeatureOptions();
  }

  constructor() {
    effect(() => {
      const d = this.data();
      if (d) {
        this.selectedTags = normalizeCharacteristicTags(d.tags, this.availableCharacteristics());
        this.selectedAmenities = normalizeContributionAmenityIds(d.amenities);
      }
    });

    effect(() => {
      const options = this.availableCharacteristics();
      if (options.length === 0) return;

      const available = new Set(options.map(characteristicKey));
      this.selectedTags = this.selectedTags.filter(tag => available.has(characteristicKey(tag)));
    });

    effect(() => {
      const options = this.availableAmenities();
      if (options.length === 0) return;

      const available = new Set(normalizeContributionAmenityIds(options.map(a => a.id)));
      this.selectedAmenities = this.selectedAmenities.filter(id => available.has(normalizeContributionAmenityIds([id])[0] ?? id));
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
    const normalized = normalizeContributionAmenityIds([id])[0] ?? id;
    if (this.isAmenitySelected(normalized)) this.selectedAmenities = this.selectedAmenities.filter(a => a !== normalized);
    else this.selectedAmenities = [...this.selectedAmenities, normalized];
    this.changed.emit(this.currentData());
  }

  isAmenitySelected(id: string): boolean {
    const normalized = normalizeContributionAmenityIds([id])[0] ?? id;
    return this.selectedAmenities.includes(normalized);
  }

  onContinue(): void {
    this.saved.emit(this.currentData());
  }

  private currentData(): FeaturesData {
    const characteristics = this.availableCharacteristics();

    return {
      tags: characteristics.length > 0
        ? normalizeCharacteristicTags(this.selectedTags, characteristics)
        : this.selectedTags,
      amenities: normalizeContributionAmenityIds(this.selectedAmenities),
    };
  }

  private loadFeatureOptions(): void {
    this.http.get<ContributionFeatureOptionsResponse>('/api/contributions/feature-options').subscribe({
      next: (response) => {
        const characteristics = normalizeOptions(response.characteristics)
          .map(option => option.label)
          .filter(Boolean);
        this.backendCharacteristics.set(characteristics);

        const characteristicIdsByType: Record<string, string[]> = {};
        for (const [placeType, ids] of Object.entries(response.characteristicIdsByPlaceType ?? {})) {
          characteristicIdsByType[characteristicKey(placeType)] = (ids ?? []).map(characteristicKey).filter(Boolean);
        }
        this.backendCharacteristicIdsByPlaceType.set(
          Object.keys(characteristicIdsByType).length > 0 ? characteristicIdsByType : null,
        );
        this.selectedTags = normalizeCharacteristicTags(this.selectedTags, this.availableCharacteristics());

        const amenities = [
          ...normalizeOptions(response.amenities),
          ...normalizeOptions(response.stayAmenities),
        ];
        if (amenities.length > 0) {
          this.backendAmenities.set(dedupeOptions(amenities).map(option => ({
            id: normalizeContributionAmenityIds([option.id])[0] ?? option.id,
            label: option.label,
          })));
        }

        const byType: Record<string, string[]> = {};
        for (const [placeType, ids] of Object.entries(response.amenityIdsByPlaceType ?? {})) {
          byType[characteristicKey(placeType)] = normalizeContributionAmenityIds(ids ?? []);
        }
        if (Object.keys(byType).length > 0) {
          this.backendAmenityIdsByPlaceType.set(byType);
        }
      },
      error: () => {
        this.backendCharacteristics.set([]);
        this.backendCharacteristicIdsByPlaceType.set(null);
        this.backendAmenities.set(null);
        this.backendAmenityIdsByPlaceType.set(null);
      },
    });
  }
}

function normalizeOptions(values: readonly ContributionFeatureOption[] | null | undefined): ContributionFeatureOption[] {
  return (values ?? [])
    .map(option => ({
      id: option.id?.trim() ?? '',
      label: option.label?.trim() ?? '',
    }))
    .filter(option => option.id && option.label);
}

function dedupeOptions(values: readonly ContributionFeatureOption[]): ContributionFeatureOption[] {
  const seen = new Set<string>();
  const result: ContributionFeatureOption[] = [];
  for (const option of values) {
    const id = normalizeContributionAmenityIds([option.id])[0] ?? option.id;
    if (seen.has(id)) continue;
    seen.add(id);
    result.push({ ...option, id });
  }
  return result;
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
