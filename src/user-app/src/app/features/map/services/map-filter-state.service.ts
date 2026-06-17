import { Injectable, computed, signal } from '@angular/core';
import {
  MapEntityMarker,
  MapFilterOptionResponse,
} from '../../../core/services/map-api.service';

export interface MapFilterOption {
  id: string;
  valueId: number;
  label: string;
  count: number;
  score?: number;
}

export interface StoredMapFilters {
  categories?: string[];
  categoryLabels?: string[];
}

export interface MapFilterSelection {
  categories: string[];
  categoryLabelIds: string[];
  categoryLabels: string[];
}

export const MAP_FILTERS_STORAGE_KEY = 'mapFilters';
const MAP_AVAILABLE_PLACE_TYPES_STORAGE_KEY = 'mapAvailablePlaceTypes';
const MAP_AVAILABLE_FILTERS_STORAGE_KEY = 'mapAvailableFilters';
const PLACE_TYPE_PREFIX = 'place-type:';
const TAG_PREFIX = 'tag:';

@Injectable({ providedIn: 'root' })
export class MapFilterStateService {
  private readonly labelById = new Map<string, string>();
  private readonly _availablePlaceTypes = signal<MapFilterOption[]>(this.readOptions(MAP_AVAILABLE_PLACE_TYPES_STORAGE_KEY));
  private readonly _availableMoreFilters = signal<MapFilterOption[]>(this.readOptions(MAP_AVAILABLE_FILTERS_STORAGE_KEY));

  readonly availablePlaceTypes = computed(() => this._availablePlaceTypes());
  readonly availableMoreFilters = computed(() => this._availableMoreFilters());

  readSelection(): MapFilterSelection {
    const saved = this.readJson<StoredMapFilters>(MAP_FILTERS_STORAGE_KEY);
    const savedCategories = Array.isArray(saved?.categories)
      ? saved.categories.filter((value): value is string => typeof value === 'string')
      : [];
    const savedTags = Array.isArray(saved?.categoryLabels)
      ? saved.categoryLabels.filter((value): value is string => typeof value === 'string')
      : [];
    const categories = this.normalizeCategories(savedCategories);
    const categoryLabelIds = this.normalizeCategoryLabelIds(savedTags);

    return {
      categories,
      categoryLabelIds,
      categoryLabels: this.labelsForCategoryLabelIds(categoryLabelIds),
    };
  }

  persistSelection(categories: string[], categoryLabelIds: string[]): void {
    const normalizedCategories = this.normalizeCategories(categories).slice(0, 1);
    const normalizedTagIds = this.normalizeCategoryLabelIds(categoryLabelIds);

    if (normalizedCategories.length === 0 && normalizedTagIds.length === 0) {
      this.removeStorageItem(MAP_FILTERS_STORAGE_KEY);
      return;
    }

    this.setStorageJson(MAP_FILTERS_STORAGE_KEY, {
      categories: normalizedCategories,
      categoryLabels: normalizedTagIds,
    } satisfies StoredMapFilters);
  }

  clearSelection(): void {
    this.removeStorageItem(MAP_FILTERS_STORAGE_KEY);
  }

  rebuildAvailablePlaceTypes(markers: MapEntityMarker[], selectedCategoryIds: string[]): MapFilterOption[] {
    const byId = new Map<string, MapFilterOption>();

    for (const marker of markers) {
      if (marker.entityType === 'destination' || typeof marker.placeTypeId !== 'number') continue;

      const label = this.cleanLabel(marker.placeTypeLabel ?? marker.categoryLabel);
      if (!label) continue;

      const id = this.placeTypeFilterId(marker.placeTypeId);
      this.labelById.set(id, label);
      const existing = byId.get(id);
      if (existing) {
        existing.count += 1;
      } else {
        byId.set(id, {
          id,
          valueId: marker.placeTypeId,
          label,
          count: 1,
        });
      }
    }

    this.restoreSelectedOptions(byId, selectedCategoryIds);
    const options = this.sortOptions([...byId.values()]);
    this._availablePlaceTypes.set(options);
    this.setStorageJson(MAP_AVAILABLE_PLACE_TYPES_STORAGE_KEY, options);
    return options;
  }

  setRecommendedTagFilters(
    recommendedTags: MapFilterOptionResponse[] | undefined,
    selectedTagIds: string[],
  ): MapFilterOption[] {
    const byId = new Map<string, MapFilterOption>();

    for (const tag of recommendedTags ?? []) {
      if (!Number.isInteger(tag.id) || tag.id <= 0) continue;
      const label = this.cleanLabel(tag.label);
      if (!label) continue;

      const id = this.tagFilterId(tag.id);
      this.labelById.set(id, label);
      byId.set(id, {
        id,
        valueId: tag.id,
        label,
        count: Math.max(0, Math.floor(tag.count ?? 0)),
        score: typeof tag.score === 'number' && Number.isFinite(tag.score) ? tag.score : 0,
      });
    }

    this.restoreSelectedOptions(byId, selectedTagIds);
    const options = [...byId.values()]
      .sort((left, right) => (right.score ?? 0) - (left.score ?? 0) || right.count - left.count || left.label.localeCompare(right.label));
    this._availableMoreFilters.set(options);
    this.setStorageJson(MAP_AVAILABLE_FILTERS_STORAGE_KEY, options);
    return options;
  }

  filterMarkers(
    markers: MapEntityMarker[],
    selectedCategoryIds: string[],
    selectedTagIds: string[],
  ): MapEntityMarker[] {
    const selectedPlaceTypeIds = new Set(
      this.normalizeCategories(selectedCategoryIds)
        .map((id) => this.parseId(id, PLACE_TYPE_PREFIX))
        .filter((id): id is number => id !== null),
    );
    const selectedTags = new Set(
      this.normalizeCategoryLabelIds(selectedTagIds)
        .map((id) => this.parseId(id, TAG_PREFIX))
        .filter((id): id is number => id !== null),
    );

    if (selectedPlaceTypeIds.size === 0 && selectedTags.size === 0) return markers;

    return markers.filter((marker) => {
      if (marker.entityType === 'destination') return true;

      if (selectedPlaceTypeIds.size > 0 &&
        (typeof marker.placeTypeId !== 'number' || !selectedPlaceTypeIds.has(marker.placeTypeId))) {
        return false;
      }

      if (selectedTags.size === 0) return true;
      const markerTagIds = new Set(marker.tagIds ?? []);
      return [...selectedTags].every((tagId) => markerTagIds.has(tagId));
    });
  }

  toggleCategoryLabelFilter(currentIds: string[], filter: MapFilterOption): string[] {
    this.labelById.set(filter.id, filter.label);
    const next = new Set(this.normalizeCategoryLabelIds(currentIds));

    if (next.has(filter.id)) {
      next.delete(filter.id);
    } else {
      next.add(filter.id);
    }

    return this.normalizeCategoryLabelIds([...next]);
  }

  labelsForCategoryLabelIds(ids: string[]): string[] {
    return this.normalizeCategoryLabelIds(ids)
      .map((id) => this.labelById.get(id))
      .filter((label): label is string => typeof label === 'string' && label.length > 0);
  }

  normalizeCategories(categories: string[]): string[] {
    const ids = categories
      .map((category) => category.trim().toLowerCase())
      .filter((category) => this.parseId(category, PLACE_TYPE_PREFIX) !== null);
    return [...new Set(ids)].slice(0, 1);
  }

  normalizeCategoryLabelIds(ids: string[]): string[] {
    return [...new Set(
      ids
        .map((id) => id.trim().toLowerCase())
        .filter((id) => this.parseId(id, TAG_PREFIX) !== null),
    )].sort((left, right) => {
      const leftLabel = this.labelById.get(left) ?? left;
      const rightLabel = this.labelById.get(right) ?? right;
      return leftLabel.localeCompare(rightLabel);
    });
  }

  private restoreSelectedOptions(target: Map<string, MapFilterOption>, selectedIds: string[]): void {
    for (const id of selectedIds) {
      if (target.has(id)) continue;
      const valueId = this.parseId(id, id.startsWith(PLACE_TYPE_PREFIX) ? PLACE_TYPE_PREFIX : TAG_PREFIX);
      const label = this.labelById.get(id);
      if (valueId !== null && label) {
        target.set(id, { id, valueId, label, count: 0 });
      }
    }
  }

  private readOptions(key: string): MapFilterOption[] {
    const saved = this.readJson<unknown>(key);
    if (!Array.isArray(saved)) return [];

    const result: MapFilterOption[] = [];
    for (const item of saved) {
      if (!item || typeof item !== 'object') continue;
      const option = item as Partial<MapFilterOption>;
      if (typeof option.id !== 'string' || typeof option.valueId !== 'number') continue;
      const label = this.cleanLabel(option.label);
      if (!label) continue;

      const normalized: MapFilterOption = {
        id: option.id,
        valueId: option.valueId,
        label,
        count: typeof option.count === 'number' && Number.isFinite(option.count) ? Math.max(0, Math.floor(option.count)) : 0,
        score: typeof option.score === 'number' && Number.isFinite(option.score) ? option.score : 0,
      };
      this.labelById.set(normalized.id, normalized.label);
      result.push(normalized);
    }

    return result;
  }

  private sortOptions(options: MapFilterOption[]): MapFilterOption[] {
    const stayFirst = (label: string): number => label.toLowerCase() === 'stays' ? 0 : 1;
    return options.sort((left, right) =>
      stayFirst(left.label) - stayFirst(right.label) ||
      right.count - left.count ||
      left.label.localeCompare(right.label));
  }

  private placeTypeFilterId(id: number): string {
    return `${PLACE_TYPE_PREFIX}${id}`;
  }

  private tagFilterId(id: number): string {
    return `${TAG_PREFIX}${id}`;
  }

  private parseId(value: string, prefix: string): number | null {
    if (!value.startsWith(prefix)) return null;
    const parsed = Number(value.slice(prefix.length));
    return Number.isInteger(parsed) && parsed > 0 ? parsed : null;
  }

  private cleanLabel(value: string | null | undefined): string {
    return (value ?? '').trim().replace(/\s+/g, ' ');
  }

  private readJson<T>(key: string): T | null {
    try {
      const raw = sessionStorage.getItem(key);
      return raw ? JSON.parse(raw) as T : null;
    } catch {
      this.removeStorageItem(key);
      return null;
    }
  }

  private setStorageJson(key: string, value: unknown): void {
    try {
      sessionStorage.setItem(key, JSON.stringify(value));
    } catch {
      // Storage may be unavailable; in-memory signal state still supports this navigation.
    }
  }

  private removeStorageItem(key: string): void {
    try {
      sessionStorage.removeItem(key);
    } catch {
      // Storage may be unavailable; ignore.
    }
  }
}
