import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { delay, Observable, of } from 'rxjs';
import type { PlaceCategoryItem } from '../models/local-places.model';
import { MOCK_CATEGORY_ITEMS } from '../data/mock-category-items.data';

interface ApiPlaceListItem {
  id: number;
  title: string;
  subtitle?: string;
  description?: string;
  location?: string;
  image?: string;
  rating?: number;
  reviewCount?: string;
  openingHours?: string;
  distance?: string;
  tags: string[];
  variant: string;
  isSaved: boolean;
}

@Injectable({ providedIn: 'root' })
export class LocalPlacesCategoryService {
  private readonly http = inject(HttpClient);
  private readonly _savedOverrides = signal<Record<string, boolean>>({});
  private readonly _apiItems = signal<Record<string, PlaceCategoryItem[]>>({});
  private readonly _loadedKeys = new Set<string>();

  loadCategory(categoryKey: string): void {
    if (this._loadedKeys.has(categoryKey)) return;
    this._loadedKeys.add(categoryKey);
    this.http.get<ApiPlaceListItem[]>(`/api/places?category=${categoryKey}`).subscribe({
      next: (items) => {
        const mapped = items.map((dto, i) => ({
          id: String(dto.id),
          title: dto.title,
          subtitle: dto.subtitle ?? '',
          description: dto.description ?? '',
          location: dto.location ?? '',
          image: dto.image ?? '',
          rating: dto.rating,
          reviewCount: dto.reviewCount,
          openingHours: dto.openingHours,
          variant: (i === 0 ? 'large' : 'compact') as PlaceCategoryItem['variant'],
          isSaved: dto.isSaved,
        }));
        this._apiItems.update(m => ({ ...m, [categoryKey]: mapped }));
      },
    });
  }

  getItems(categoryKey: string): PlaceCategoryItem[] {
    const overrides = this._savedOverrides();
    const apiData = this._apiItems()[categoryKey];
    const source = apiData ?? (MOCK_CATEGORY_ITEMS[categoryKey] ?? []);
    return source.map((item) => ({
      ...item,
      isSaved: overrides[item.id] !== undefined ? overrides[item.id] : item.isSaved,
    }));
  }

  itemsFor(categoryKey: string) {
    return computed(() => {
      const overrides = this._savedOverrides();
      const apiData = this._apiItems()[categoryKey];
      const source = apiData ?? (MOCK_CATEGORY_ITEMS[categoryKey] ?? []);
      return source.map((item) => ({
        ...item,
        isSaved: overrides[item.id] !== undefined ? overrides[item.id] : item.isSaved,
      }));
    });
  }

  getItems$(categoryKey: string): Observable<PlaceCategoryItem[]> {
    return of(this.getItems(categoryKey)).pipe(delay(0));
  }

  toggleSaved(id: string, categoryKey: string): void {
    const current = this.getItems(categoryKey).find((item) => item.id === id)?.isSaved ?? false;
    this._savedOverrides.update((m) => ({ ...m, [id]: !current }));
    const numId = parseInt(id, 10);
    if (!isNaN(numId)) {
      this.http.post('/api/favorites/toggle', { entityType: 'place', entityId: numId }).subscribe();
    }
  }
}
