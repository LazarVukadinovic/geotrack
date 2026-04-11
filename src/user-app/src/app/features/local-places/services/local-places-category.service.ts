import { Injectable, computed, signal } from '@angular/core';
import { delay, Observable, of } from 'rxjs';
import type { PlaceCategoryItem } from '../models/local-places.model';
import { MOCK_CATEGORY_ITEMS } from '../data/mock-category-items.data';

/**
 * Per-category place items data access.
 * Replace mock observable with HttpClient call when the API is ready.
 */
@Injectable({ providedIn: 'root' })
export class LocalPlacesCategoryService {
  private readonly _savedOverrides = signal<Record<string, boolean>>({});

  getItems(categoryKey: string): PlaceCategoryItem[] {
    const overrides = this._savedOverrides();
    return (MOCK_CATEGORY_ITEMS[categoryKey] ?? []).map((item) => ({
      ...item,
      isSaved: overrides[item.id] !== undefined ? overrides[item.id] : item.isSaved,
    }));
  }

  /** Reactive items signal for a given category — re-evaluates when savedOverrides change. */
  itemsFor(categoryKey: string) {
    return computed(() => {
      const overrides = this._savedOverrides();
      return (MOCK_CATEGORY_ITEMS[categoryKey] ?? []).map((item) => ({
        ...item,
        isSaved: overrides[item.id] !== undefined ? overrides[item.id] : item.isSaved,
      }));
    });
  }

  /** API-shaped stream — swap for HttpClient.get<PlaceCategoryItem[]>(`/api/places?category=${key}`) when ready. */
  getItems$(categoryKey: string): Observable<PlaceCategoryItem[]> {
    return of(this.getItems(categoryKey)).pipe(delay(0));
  }

  toggleSaved(id: string, categoryKey: string): void {
    const current = this.getItems(categoryKey).find((item) => item.id === id)?.isSaved ?? false;
    this._savedOverrides.update((m) => ({ ...m, [id]: !current }));
  }
}
