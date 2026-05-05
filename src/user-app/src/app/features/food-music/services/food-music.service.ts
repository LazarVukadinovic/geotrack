import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { delay, Observable, of } from 'rxjs';
import { DestinationService } from '../../../core/services/destination.service';
import { FavoriteStateService } from '../../../core/services/favorite-state.service';
import { LocationService } from '../../../core/services/location.service';
import { SaveToCollectionService } from '../../../core/services/save-to-collection.service';
import { ExploreService } from '../../explore/services/explore.service';
import type { FoodMusicItem } from '../models/food-music.model';

const FIRST_PAGE_SIZE = 6;
const NEXT_PAGE_SIZE = 6;
const FOOD_MUSIC_TAGS = 'food-drink,nightlife,live-music,fine-dining,beach-bars';

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

interface ApiPlaceListingResponse {
  items: ApiPlaceListItem[];
  skip: number;
  take: number;
  nextSkip: number;
  hasMore: boolean;
}

@Injectable({ providedIn: 'root' })
export class FoodMusicService {
  private readonly http = inject(HttpClient);
  private readonly destinationSvc = inject(DestinationService);
  private readonly favoriteState = inject(FavoriteStateService);
  private readonly locationSvc = inject(LocationService);
  private readonly saveToCollection = inject(SaveToCollectionService);
  private readonly exploreSvc = inject(ExploreService);
  private readonly _items = signal<FoodMusicItem[]>([]);
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

  readonly items = computed(() => {
    return this._items().map((item) => ({
      ...item,
      isSaved: this.favoriteState.resolve('place', item.id, item.isSaved),
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
    this._items.set([]);
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
    this._items.set([]);
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

    this.http.get<ApiPlaceListingResponse>('/api/places', {
      params: this.buildParams(destinationId, skip, take),
    }).subscribe({
      next: (res) => {
        if (requestId !== this.requestVersion) return;
        const mapped = this.mapItems(res.items, append ? this._items().length : 0);
        this._items.set(append ? this.mergeItems(this._items(), mapped) : mapped);
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
          this._loadMoreError.set("We couldn't load more food & music places. Try again.");
        } else {
          this._items.set([]);
          this._hasMore.set(false);
          this._nextSkip.set(0);
          this._loadError.set("We couldn't load food & music places. Check your connection and try again.");
        }
        this.finishLoading(append);
      },
    });
  }

  private buildParams(destinationId: number, skip: number, take: number): HttpParams {
    let params = new HttpParams()
      .set('destinationId', destinationId)
      .set('tags', this.activeFilter === 'all' ? FOOD_MUSIC_TAGS : this.activeFilter)
      .set('skip', skip)
      .set('take', take);

    if (this.requestLatitude != null && this.requestLongitude != null) {
      params = params
        .set('latitude', this.requestLatitude)
        .set('longitude', this.requestLongitude);
    }

    return params;
  }

  private captureLocationContext(): void {
    if (this.destinationSvc.isDestinationMode() &&
      this.destinationSvc.latitude() != null &&
      this.destinationSvc.longitude() != null) {
      this.requestLatitude = this.destinationSvc.latitude();
      this.requestLongitude = this.destinationSvc.longitude();
      return;
    }

    const loc = this.locationSvc.location();
    this.requestLatitude = loc?.latitude ?? null;
    this.requestLongitude = loc?.longitude ?? null;
  }

  private mapItems(items: ApiPlaceListItem[], startIndex: number): FoodMusicItem[] {
    return items.map((dto, i) => ({
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
      variant: (startIndex + i === 0 ? 'large' : 'compact') as FoodMusicItem['variant'],
      isSaved: dto.isSaved,
    }));
  }

  private mergeItems(current: FoodMusicItem[], next: FoodMusicItem[]): FoodMusicItem[] {
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

  getItems$(): Observable<FoodMusicItem[]> {
    return of(this.items()).pipe(delay(0));
  }

  toggleSaved(id: string): void {
    const numId = parseInt(id, 10);
    if (isNaN(numId)) return;
    const item = this.items().find((entry) => entry.id === id);
    if (!item) return;
    this.saveToCollection.open({
      entityType: 'place',
      entityId: numId,
      title: item.title,
      imageUrl: item.image,
      surface: 'food_music_listing',
      analyticsEntityType: 'place',
    });
  }
}
