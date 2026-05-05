import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { delay, Observable, of } from 'rxjs';
import { DestinationService } from '../../../core/services/destination.service';
import { FavoriteStateService } from '../../../core/services/favorite-state.service';
import { LocationService } from '../../../core/services/location.service';
import { SaveToCollectionService } from '../../../core/services/save-to-collection.service';
import { ExploreService } from '../../explore/services/explore.service';
import type { EventListingItem, EventsFeatured } from '../models/events-listing.model';

const FIRST_PAGE_SIZE = 6;
const NEXT_PAGE_SIZE = 6;

interface ApiEventsResponse {
  items: {
    id: number;
    title: string;
    subtitle?: string;
    description?: string;
    location?: string;
    image?: string;
    dateLabel?: string;
    tags: string[];
    variant: string;
    isSaved: boolean;
  }[];
  featured?: {
    title: string;
    description?: string;
    dateRange?: string;
    locationLine?: string;
  };
  skip: number;
  take: number;
  nextSkip: number;
  hasMore: boolean;
}

@Injectable({ providedIn: 'root' })
export class EventsListingService {
  private readonly http = inject(HttpClient);
  private readonly destinationSvc = inject(DestinationService);
  private readonly favoriteState = inject(FavoriteStateService);
  private readonly locationSvc = inject(LocationService);
  private readonly saveToCollection = inject(SaveToCollectionService);
  private readonly exploreSvc = inject(ExploreService);
  private readonly _items = signal<EventListingItem[]>([]);
  private readonly _featured = signal<EventsFeatured | null>(null);
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

  readonly featured = computed(() => this._featured());

  readonly items = computed(() => {
    return this._items().map((item) => ({
      ...item,
      isSaved: this.favoriteState.resolve('event', item.id, item.isSaved),
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
    this._featured.set(null);
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
    this._featured.set(null);
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

    this.http.get<ApiEventsResponse>('/api/events', {
      params: this.buildParams(destinationId, skip, take),
    }).subscribe({
      next: (res) => {
        if (requestId !== this.requestVersion) return;
        const mapped = this.mapItems(res.items, append ? this._items().length : 0);
        this._items.set(append ? this.mergeItems(this._items(), mapped) : mapped);
        if (!append && res.featured) {
          this._featured.set({
            title: res.featured.title,
            description: res.featured.description ?? '',
            dateRange: res.featured.dateRange ?? '',
            locationLine: res.featured.locationLine ?? '',
          });
        } else if (!append) {
          this._featured.set(null);
        }
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
          this._loadMoreError.set("We couldn't load more events. Try again.");
        } else {
          this._items.set([]);
          this._featured.set(null);
          this._hasMore.set(false);
          this._nextSkip.set(0);
          this._loadError.set("We couldn't load events. Check your connection and try again.");
        }
        this.finishLoading(append);
      },
    });
  }

  private buildParams(destinationId: number, skip: number, take: number): HttpParams {
    let params = new HttpParams()
      .set('destinationId', destinationId)
      .set('upcoming', true)
      .set('skip', skip)
      .set('take', take);

    if (this.activeFilter !== 'all') {
      params = params.set('tags', this.activeFilter);
    }

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

  private mapItems(items: ApiEventsResponse['items'], startIndex: number): EventListingItem[] {
    return items.map((dto, i) => ({
      id: String(dto.id),
      title: dto.title,
      subtitle: dto.subtitle ?? '',
      description: dto.description ?? '',
      location: dto.location ?? '',
      image: dto.image ?? '',
      dateLabel: dto.dateLabel ?? '',
      tags: dto.tags,
      variant: (startIndex + i === 0 ? 'large' : 'compact') as EventListingItem['variant'],
      isSaved: dto.isSaved,
    }));
  }

  private mergeItems(current: EventListingItem[], next: EventListingItem[]): EventListingItem[] {
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

  getItems$(): Observable<EventListingItem[]> {
    return of(this.items()).pipe(delay(0));
  }

  getFeatured$(): Observable<EventsFeatured | null> {
    return of(this._featured()).pipe(delay(0));
  }

  toggleSaved(id: string): void {
    const numId = parseInt(id, 10);
    if (isNaN(numId)) return;
    const event = this.items().find((entry) => entry.id === id);
    if (!event) return;
    this.saveToCollection.open({
      entityType: 'event',
      entityId: numId,
      title: event.title,
      imageUrl: event.image,
      surface: 'events_listing',
      analyticsEntityType: 'event',
    });
  }
}
