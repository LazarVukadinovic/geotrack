import { Injectable, computed, signal } from '@angular/core';
import { MapEntityMarker } from '../../../core/services/map-api.service';

export interface MapFilterOption {
  id: string;
  label: string;
  count: number;
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
const MAP_AVAILABLE_FILTERS_STORAGE_KEY = 'mapAvailableFilters';
const ALLOWED_CATEGORY_IDS = new Set(['places', 'stays', 'events']);

@Injectable({ providedIn: 'root' })
export class MapFilterStateService {
  private readonly categoryLabelById = new Map<string, string>();
  private readonly _availableMoreFilters = signal<MapFilterOption[]>(this.readAvailableMoreFilters());

  readonly availableMoreFilters = computed(() => this._availableMoreFilters());

  readSelection(): MapFilterSelection {
    const saved = this.readJson<StoredMapFilters>(MAP_FILTERS_STORAGE_KEY);
    const savedCategories = Array.isArray(saved?.categories)
      ? saved.categories.filter((value): value is string => typeof value === 'string')
      : [];
    const savedCategoryLabels = Array.isArray(saved?.categoryLabels)
      ? saved.categoryLabels.filter((value): value is string => typeof value === 'string')
      : [];
    const categories = this.normalizeCategories(savedCategories);
    const categoryLabelIds = this.normalizeCategoryLabelFilters(savedCategoryLabels);

    return {
      categories,
      categoryLabelIds,
      categoryLabels: this.labelsForCategoryLabelIds(categoryLabelIds),
    };
  }

  persistSelection(categories: string[], categoryLabelIds: string[]): void {
    const normalizedCategories = this.normalizeCategories(categories);
    const normalizedCategoryLabelIds = this.normalizeCategoryLabelIds(categoryLabelIds);
    const categoryLabels = this.labelsForCategoryLabelIds(normalizedCategoryLabelIds);

    if (normalizedCategories.length === 0 && categoryLabels.length === 0) {
      this.removeStorageItem(MAP_FILTERS_STORAGE_KEY);
      return;
    }

    this.setStorageJson(MAP_FILTERS_STORAGE_KEY, {
      categories: normalizedCategories,
      categoryLabels,
    } satisfies StoredMapFilters);
  }

  clearSelection(): void {
    this.removeStorageItem(MAP_FILTERS_STORAGE_KEY);
  }

  rebuildAvailableMoreFilters(markers: MapEntityMarker[], selectedCategoryLabelIds: string[]): MapFilterOption[] {
    const filtersById = new Map<string, MapFilterOption>();

    for (const marker of markers) {
      if (marker.entityType === 'destination') continue;

      const label = this.cleanCategoryLabel(marker.categoryLabel);
      if (!label) continue;

      const id = this.toCategoryLabelFilterId(label);
      if (!id) continue;

      this.categoryLabelById.set(id, label);
      const existing = filtersById.get(id);
      if (existing) {
        existing.count += 1;
      } else {
        filtersById.set(id, { id, label, count: 1 });
      }
    }

    for (const id of selectedCategoryLabelIds) {
      if (filtersById.has(id)) continue;

      const label = this.categoryLabelById.get(id);
      if (label) {
        filtersById.set(id, { id, label, count: 0 });
      }
    }

    const filters = this.sortFilterOptions([...filtersById.values()]);
    this._availableMoreFilters.set(filters);
    this.setStorageJson(MAP_AVAILABLE_FILTERS_STORAGE_KEY, filters);
    return filters;
  }

  filterMarkersByCategoryLabels(markers: MapEntityMarker[], selectedCategoryLabelIds: string[]): MapEntityMarker[] {
    if (selectedCategoryLabelIds.length === 0) {
      return markers;
    }

    const selected = new Set(selectedCategoryLabelIds);
    return markers.filter((marker) => {
      if (marker.entityType === 'destination') return true;

      const label = this.cleanCategoryLabel(marker.categoryLabel);
      return label ? selected.has(this.toCategoryLabelFilterId(label)) : false;
    });
  }

  toggleCategoryLabelFilter(currentIds: string[], filter: MapFilterOption): string[] {
    this.categoryLabelById.set(filter.id, filter.label);
    const next = new Set(currentIds);

    if (next.has(filter.id)) {
      next.delete(filter.id);
    } else {
      next.add(filter.id);
    }

    return this.normalizeCategoryLabelIds([...next]);
  }

  labelsForCategoryLabelIds(ids: string[]): string[] {
    return this.normalizeCategoryLabelIds(ids)
      .map((id) => this.categoryLabelById.get(id))
      .filter((label): label is string => typeof label === 'string' && label.length > 0);
  }

  normalizeCategories(categories: string[]): string[] {
    return [...new Set(
      categories
        .map((category) => category.trim().toLowerCase())
        .filter((category) => ALLOWED_CATEGORY_IDS.has(category)),
    )].sort();
  }

  normalizeCategoryLabelFilters(labels: string[]): string[] {
    const ids: string[] = [];

    for (const value of labels) {
      const label = this.cleanCategoryLabel(value);
      if (!label) continue;

      const id = this.toCategoryLabelFilterId(label);
      if (!id || ids.includes(id)) continue;

      this.categoryLabelById.set(id, label);
      ids.push(id);
    }

    return this.normalizeCategoryLabelIds(ids);
  }

  normalizeCategoryLabelIds(ids: string[]): string[] {
    return [...new Set(ids.map((id) => id.trim().toLowerCase()).filter(Boolean))]
      .sort((left, right) => {
        const leftLabel = this.categoryLabelById.get(left) ?? left;
        const rightLabel = this.categoryLabelById.get(right) ?? right;
        return leftLabel.localeCompare(rightLabel);
      });
  }

  private readAvailableMoreFilters(): MapFilterOption[] {
    const saved = this.readJson<unknown>(MAP_AVAILABLE_FILTERS_STORAGE_KEY);
    if (!Array.isArray(saved)) return [];

    const filtersById = new Map<string, MapFilterOption>();

    for (const value of saved) {
      if (!value || typeof value !== 'object') continue;

      const item = value as Partial<MapFilterOption>;
      const label = this.cleanCategoryLabel(item.label);
      if (!label) continue;

      const id = this.toCategoryLabelFilterId(label);
      if (!id) continue;

      const count = typeof item.count === 'number' && Number.isFinite(item.count) && item.count > 0
        ? Math.floor(item.count)
        : 0;

      this.categoryLabelById.set(id, label);
      filtersById.set(id, { id, label, count });
    }

    return this.sortFilterOptions([...filtersById.values()]);
  }

  private sortFilterOptions(filters: MapFilterOption[]): MapFilterOption[] {
    return filters.sort((left, right) => right.count - left.count || left.label.localeCompare(right.label));
  }

  private cleanCategoryLabel(value: string | null | undefined): string {
    return (value ?? '').trim().replace(/\s+/g, ' ');
  }

  private toCategoryLabelFilterId(label: string): string {
    return label.trim().toLowerCase();
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
