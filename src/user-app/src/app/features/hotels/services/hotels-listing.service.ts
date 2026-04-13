import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { delay, Observable, of } from 'rxjs';
import type { HotelListItem } from '../models/hotels-listing.model';
import { MOCK_HOTELS_LISTING } from '../data/mock-hotels-listing.data';

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
  pricePerNight?: number;
  tags: string[];
  variant: string;
  isSaved: boolean;
}

@Injectable({ providedIn: 'root' })
export class HotelsListingService {
  private readonly http = inject(HttpClient);
  private readonly _hotels = signal<HotelListItem[]>([]);
  private readonly _savedOverrides = signal<Record<string, boolean>>({});
  private readonly _loaded = signal(false);

  readonly hotels = computed(() => {
    const overrides = this._savedOverrides();
    return this._hotels().map((h) => ({
      ...h,
      isSaved: overrides[h.id] !== undefined ? overrides[h.id] : h.isSaved,
    }));
  });

  constructor() {
    this.http.get<ApiPlaceListItem[]>('/api/places/hotels').subscribe({
      next: (items) => {
        this._hotels.set(items.map((dto, i) => ({
          id: String(dto.id),
          title: dto.title,
          location: dto.location ?? '',
          image: dto.image ?? '',
          rating: dto.rating ?? 0,
          reviewCount: dto.reviewCount,
          distance: dto.distance ?? '',
          pricePerNight: dto.pricePerNight ?? 0,
          description: dto.description ?? '',
          tags: dto.tags,
          variant: (i === 0 ? 'large' : 'compact') as HotelListItem['variant'],
          isSaved: dto.isSaved,
        })));
        this._loaded.set(true);
      },
      error: () => {
        this._hotels.set([...MOCK_HOTELS_LISTING]);
        this._loaded.set(true);
      },
    });
  }

  getHotels$(): Observable<HotelListItem[]> {
    return of(this.hotels()).pipe(delay(0));
  }

  toggleSaved(id: string): void {
    const current = this.hotels().find((h) => h.id === id)?.isSaved ?? false;
    this._savedOverrides.update((m) => ({ ...m, [id]: !current }));
    const numId = parseInt(id, 10);
    if (!isNaN(numId)) {
      this.http.post('/api/favorites/toggle', { entityType: 'place', entityId: numId }).subscribe();
    }
  }
}
