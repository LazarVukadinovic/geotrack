import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { delay, Observable, of } from 'rxjs';
import { DestinationService } from '../../../core/services/destination.service';
import { FavoriteStateService } from '../../../core/services/favorite-state.service';
import { LocationService } from '../../../core/services/location.service';
import { SaveToCollectionService } from '../../../core/services/save-to-collection.service';
import { ExploreService } from '../../explore/services/explore.service';
import type { HotelListItem } from '../models/hotels-listing.model';

const FIRST_PAGE_SIZE = 6;
const NEXT_PAGE_SIZE = 6;

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

interface ApiPlaceListingResponse {
  items: ApiPlaceListItem[];
  skip: number;
  take: number;
  nextSkip: number;
  hasMore: boolean;
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
  private readonly _loading = signal(false);
  private readonly _loadingMore = signal(false);
  private readonly _loadError = signal<string | null>(null);
  private readonly _loadMoreError = signal<string | null>(null);
  private readonly _hasMore = signal(false);
  private readonly _nextSkip = signal(0);
  private loadedDestinationId: number | null = null;
  private activeFilter = 'all';
  private requestLatitude: number | null = null;
  private requestLongitude: number | null = null;
  private requestVersion = 0;

  readonly hotels = computed(() => {
    return this._hotels().map((h) => ({
      ...h,
      isSaved: this.favoriteState.resolve('place', h.id, h.isSaved),
    }));
  });

  readonly loadError = computed(() => this._loadError());
  readonly loadMoreError = computed(() => this._loadMoreError());
  readonly loading = computed(() => this._loading());
  readonly loadingMore = computed(() => this._loadingMore());
  readonly hasMore = computed(() => this._hasMore());

  constructor() {
    void this.exploreSvc;
    effect(() => {
      const destId = this.destinationSvc.id();
      if (destId == null || destId === this.loadedDestinationId) return;
      this.resetForDestination(destId);
      this.loadPage(destId, 0, FIRST_PAGE_SIZE, false);
    });
  }

  setFilter(filterId: string): void {
    if (this.activeFilter === filterId) return;
    this.activeFilter = filterId;
    const destinationId = this.loadedDestinationId ?? this.destinationSvc.id();
    if (destinationId == null) return;
    this._hotels.set([]);
    this._hasMore.set(false);
    this._nextSkip.set(0);
    this.loadPage(destinationId, 0, FIRST_PAGE_SIZE, false);
  }

  loadMore(): void {
    const destinationId = this.loadedDestinationId;
    if (destinationId == null || this._loading() || this._loadingMore() || !this._hasMore()) return;
    this.loadPage(destinationId, this._nextSkip(), NEXT_PAGE_SIZE, true);
  }

  private resetForDestination(destinationId: number): void {
    this.loadedDestinationId = destinationId;
    this._hotels.set([]);
    this._hasMore.set(false);
    this._nextSkip.set(0);
    this._loadError.set(null);
    this._loadMoreError.set(null);
  }

  private loadPage(destinationId: number, skip: number, take: number, append: boolean): void {
    const requestId = ++this.requestVersion;

    if (append) {
      this._loadingMore.set(true);
      this._loadMoreError.set(null);
    } else {
      this.captureLocationContext();
      this._loading.set(true);
      this._loadError.set(null);
      this._loadMoreError.set(null);
    }

    this.http.get<ApiPlaceListingResponse>('/api/places/hotels', {
      params: this.buildParams(destinationId, skip, take),
    }).subscribe({
      next: (res) => {
        if (requestId !== this.requestVersion) return;
        const mapped = this.mapItems(res.items, append ? this._hotels().length : 0);
        this._hotels.set(append ? this.mergeItems(this._hotels(), mapped) : mapped);
        this.loadedDestinationId = destinationId;
        this._hasMore.set(res.hasMore);
        this._nextSkip.set(res.nextSkip);
        this._loadError.set(null);
        this._loadMoreError.set(null);
        this.finishLoading(append);
      },
      error: () => {
        if (requestId !== this.requestVersion) return;
        if (append) {
          this._loadMoreError.set("We couldn't load more hotels. Try again.");
        } else {
          this._hotels.set([]);
          this._hasMore.set(false);
          this._nextSkip.set(0);
          this._loadError.set("We couldn't load hotels. Check your connection and try again.");
        }
        this.finishLoading(append);
      },
    });
  }

  private buildParams(destinationId: number, skip: number, take: number): HttpParams {
    let params = new HttpParams()
      .set('destinationId', destinationId)
      .set('skip', skip)
      .set('take', take);

    if (this.activeFilter !== 'all') {
      params = params.set('tag', this.activeFilter);
    }

    if (this.requestLatitude != null && this.requestLongitude != null) {
      params = params
        .set('latitude', this.requestLatitude)
        .set('longitude', this.requestLongitude);
    }

    return params;
  }

  private captureLocationContext(): void {
    const loc = this.locationSvc.location();
    this.requestLatitude = loc?.latitude ?? null;
    this.requestLongitude = loc?.longitude ?? null;
  }

  private mapItems(items: ApiPlaceListItem[], startIndex: number): HotelListItem[] {
    return items.map((dto, i) => ({
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
      variant: (startIndex + i === 0 ? 'large' : 'compact') as HotelListItem['variant'],
      isSaved: dto.isSaved,
    }));
  }

  private mergeItems(current: HotelListItem[], next: HotelListItem[]): HotelListItem[] {
    const existingIds = new Set(current.map(item => item.id));
    return [...current, ...next.filter(item => !existingIds.has(item.id))];
  }

  private finishLoading(append: boolean): void {
    if (append) {
      this._loadingMore.set(false);
    } else {
      this._loading.set(false);
    }
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
