import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { delay, Observable, of } from 'rxjs';
import type { CultureHistoryItem } from '../models/culture-history.model';
import { MOCK_CULTURE_HISTORY } from '../data/mock-culture-history.data';

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
export class CultureHistoryService {
  private readonly http = inject(HttpClient);
  private readonly _items = signal<CultureHistoryItem[]>([]);
  private readonly _savedOverrides = signal<Record<string, boolean>>({});

  readonly items = computed(() => {
    const overrides = this._savedOverrides();
    return this._items().map((item) => ({
      ...item,
      isSaved: overrides[item.id] !== undefined ? overrides[item.id] : item.isSaved,
    }));
  });

  constructor() {
    this.http.get<ApiPlaceListItem[]>('/api/places?tags=culture,history').subscribe({
      next: (items) => {
        this._items.set(items.map((dto, i) => ({
          id: String(dto.id),
          title: dto.title,
          subtitle: dto.subtitle ?? '',
          description: dto.description ?? '',
          location: dto.location ?? '',
          image: dto.image ?? '',
          rating: dto.rating,
          reviewCount: dto.reviewCount,
          openingHours: dto.openingHours,
          tags: dto.tags,
          variant: (i === 0 ? 'large' : 'compact') as CultureHistoryItem['variant'],
          isSaved: dto.isSaved,
        })));
      },
      error: () => {
        this._items.set([...MOCK_CULTURE_HISTORY]);
      },
    });
  }

  getItems$(): Observable<CultureHistoryItem[]> {
    return of(this.items()).pipe(delay(0));
  }

  toggleSaved(id: string): void {
    const current = this.items().find((item) => item.id === id)?.isSaved ?? false;
    this._savedOverrides.update((m) => ({ ...m, [id]: !current }));
    const numId = parseInt(id, 10);
    if (!isNaN(numId)) {
      this.http.post('/api/favorites/toggle', { entityType: 'place', entityId: numId }).subscribe();
    }
  }
}
