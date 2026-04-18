import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { delay, Observable, of } from 'rxjs';
import { AuthService } from '../../../core/services/auth.service';
import { DestinationService } from '../../../core/services/destination.service';
import { ExploreService } from '../../explore/services/explore.service';
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
  private readonly auth = inject(AuthService);
  private readonly destinationSvc = inject(DestinationService);
  // Ensure ExploreService is instantiated so its LocationService effect can resolve
  // the destination id on a deep-link cold-start.
  private readonly exploreSvc = inject(ExploreService);
  private readonly _hotels = signal<HotelListItem[]>([]);
  private readonly _savedOverrides = signal<Record<string, boolean>>({});
  private readonly _loaded = signal(false);
  private loadedDestinationId: number | null = null;

  readonly hotels = computed(() => {
    const overrides = this._savedOverrides();
    return this._hotels().map((h) => ({
      ...h,
      isSaved: overrides[h.id] !== undefined ? overrides[h.id] : h.isSaved,
    }));
  });

  constructor() {
    void this.exploreSvc;
    effect(() => {
      const destId = this.destinationSvc.id();
      if (destId == null || destId === this.loadedDestinationId) return;
      this.loadFromApi(destId);
    });
  }

  private loadFromApi(destinationId: number): void {
    this.http.get<ApiPlaceListItem[]>(`/api/places/hotels?destinationId=${destinationId}`).subscribe({
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
        this.loadedDestinationId = destinationId;
        this._loaded.set(true);
      },
      error: () => {
        this._hotels.set([...MOCK_HOTELS_LISTING]);
        this.loadedDestinationId = destinationId;
        this._loaded.set(true);
      },
    });
  }

  getHotels$(): Observable<HotelListItem[]> {
    return of(this.hotels()).pipe(delay(0));
  }

  toggleSaved(id: string): void {
    if (!this.auth.isLoggedIn()) return;

    const flip = () => {
      const current = this.hotels().find((h) => h.id === id)?.isSaved ?? false;
      this._savedOverrides.update((m) => ({ ...m, [id]: !current }));
    };
    flip();

    const numId = parseInt(id, 10);
    if (isNaN(numId)) return;
    this.http.post('/api/favorites/toggle', { entityType: 'place', entityId: numId }).subscribe({
      error: () => flip(),
    });
  }
}
