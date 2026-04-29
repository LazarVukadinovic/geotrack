import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { delay, Observable, of } from 'rxjs';
import { DestinationService } from '../../../core/services/destination.service';
import { FavoriteStateService } from '../../../core/services/favorite-state.service';
import { LocationService } from '../../../core/services/location.service';
import { SaveToCollectionService } from '../../../core/services/save-to-collection.service';
import { ExploreService } from '../../explore/services/explore.service';
import type { HotelListItem } from '../models/hotels-listing.model';

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
  private readonly destinationSvc = inject(DestinationService);
  private readonly favoriteState = inject(FavoriteStateService);
  private readonly locationSvc = inject(LocationService);
  private readonly saveToCollection = inject(SaveToCollectionService);
  // Ensure ExploreService is instantiated so its LocationService effect can resolve
  // the destination id on a deep-link cold-start.
  private readonly exploreSvc = inject(ExploreService);
  private readonly _hotels = signal<HotelListItem[]>([]);
  private readonly _loaded = signal(false);
  private readonly _loadError = signal<string | null>(null);
  private loadedDestinationId: number | null = null;

  readonly hotels = computed(() => {
    return this._hotels().map((h) => ({
      ...h,
      isSaved: this.favoriteState.resolve('place', h.id, h.isSaved),
    }));
  });

  readonly loadError = computed(() => this._loadError());

  constructor() {
    void this.exploreSvc;
    effect(() => {
      const destId = this.destinationSvc.id();
      if (destId == null || destId === this.loadedDestinationId) return;
      this.loadFromApi(destId);
    });
  }

  private loadFromApi(destinationId: number): void {
    this.http.get<ApiPlaceListItem[]>(`/api/places/hotels?destinationId=${destinationId}${this.buildLocationQuery()}`).subscribe({
      next: (items) => {
        this._hotels.set(items.map((dto, i) => ({
          id: String(dto.id),
          title: dto.title,
          location: dto.location ?? '',
          image: dto.image ?? '',
          rating: dto.rating,
          reviewCount: dto.reviewCount,
          distance: dto.distance ?? '',
          pricePerNight: dto.pricePerNight ?? 0,
          description: dto.description ?? '',
          tags: dto.tags,
          variant: (i === 0 ? 'large' : 'compact') as HotelListItem['variant'],
          isSaved: dto.isSaved,
        })));
        this.loadedDestinationId = destinationId;
        this._loadError.set(null);
        this._loaded.set(true);
      },
      error: () => {
        this._hotels.set([]);
        this.loadedDestinationId = destinationId;
        this._loadError.set("We couldn't load hotels. Check your connection and try again.");
        this._loaded.set(true);
      },
    });
  }

  private buildLocationQuery(): string {
    const loc = this.locationSvc.location();
    if (!loc) return '';
    return `&latitude=${loc.latitude}&longitude=${loc.longitude}`;
  }

  getHotels$(): Observable<HotelListItem[]> {
    return of(this.hotels()).pipe(delay(0));
  }

  toggleSaved(id: string): void {
    const numId = parseInt(id, 10);
    if (isNaN(numId)) return;
    const hotel = this.hotels().find((entry) => entry.id === id);
    if (!hotel) return;
    this.saveToCollection.open({
      entityType: 'place',
      entityId: numId,
      title: hotel.title,
      imageUrl: hotel.image,
      surface: 'hotels_listing',
      analyticsEntityType: 'hotel',
    });
  }
}
