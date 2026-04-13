import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { delay, Observable, of } from 'rxjs';
import type { NatureItem } from '../models/nature.model';
import { MOCK_NATURE } from '../data/mock-nature.data';

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
export class NatureService {
  private readonly http = inject(HttpClient);
  private readonly _items = signal<NatureItem[]>([]);
  private readonly _savedOverrides = signal<Record<string, boolean>>({});

  readonly items = computed(() => {
    const overrides = this._savedOverrides();
    return this._items().map((item) => ({
      ...item,
      isSaved: overrides[item.id] !== undefined ? overrides[item.id] : item.isSaved,
    }));
  });

  constructor() {
    this.http.get<ApiPlaceListItem[]>('/api/places?tags=nature').subscribe({
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
          distance: dto.distance,
          tags: dto.tags,
          variant: (i === 0 ? 'large' : 'compact') as NatureItem['variant'],
          isSaved: dto.isSaved,
        })));
      },
      error: () => {
        this._items.set([...MOCK_NATURE]);
      },
    });
  }

  getItems$(): Observable<NatureItem[]> {
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
