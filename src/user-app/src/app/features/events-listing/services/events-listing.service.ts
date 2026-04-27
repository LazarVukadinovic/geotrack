import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { delay, Observable, of } from 'rxjs';
import { AuthService } from '../../../core/services/auth.service';
import { DestinationService } from '../../../core/services/destination.service';
import { FavoriteStateService } from '../../../core/services/favorite-state.service';
import { LocationService } from '../../../core/services/location.service';
import { AnalyticsService } from '../../../core/services/analytics.service';
import { ExploreService } from '../../explore/services/explore.service';
import type { EventListingItem, EventsFeatured } from '../models/events-listing.model';

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
}

@Injectable({ providedIn: 'root' })
export class EventsListingService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly destinationSvc = inject(DestinationService);
  private readonly favoriteState = inject(FavoriteStateService);
  private readonly locationSvc = inject(LocationService);
  private readonly analytics = inject(AnalyticsService);
  private readonly exploreSvc = inject(ExploreService);
  private readonly _items = signal<EventListingItem[]>([]);
  private readonly _featured = signal<EventsFeatured | null>(null);
  private readonly _loadError = signal<string | null>(null);
  private loadedDestinationId: number | null = null;

  readonly featured = computed(() => this._featured());

  readonly items = computed(() => {
    return this._items().map((item) => ({
      ...item,
      isSaved: this.favoriteState.resolve('event', item.id, item.isSaved),
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
    this.http.get<ApiEventsResponse>(`/api/events?destinationId=${destinationId}${this.buildLocationQuery()}`).subscribe({
      next: (res) => {
        this._items.set(res.items.map((dto, i) => ({
          id: String(dto.id),
          title: dto.title,
          subtitle: dto.subtitle ?? '',
          description: dto.description ?? '',
          location: dto.location ?? '',
          image: dto.image ?? '',
          dateLabel: dto.dateLabel ?? '',
          tags: dto.tags,
          variant: (i === 0 ? 'large' : 'compact') as EventListingItem['variant'],
          isSaved: dto.isSaved,
        })));
        if (res.featured) {
          this._featured.set({
            title: res.featured.title,
            description: res.featured.description ?? '',
            dateRange: res.featured.dateRange ?? '',
            locationLine: res.featured.locationLine ?? '',
          });
        }
        this.loadedDestinationId = destinationId;
        this._loadError.set(null);
      },
      error: () => {
        this._items.set([]);
        this.loadedDestinationId = destinationId;
        this._loadError.set("We couldn't load events. Check your connection and try again.");
      },
    });
  }

  private buildLocationQuery(): string {
    const loc = this.locationSvc.location();
    if (!loc) return '';
    return `&latitude=${loc.latitude}&longitude=${loc.longitude}`;
  }

  getItems$(): Observable<EventListingItem[]> {
    return of(this.items()).pipe(delay(0));
  }

  getFeatured$(): Observable<EventsFeatured | null> {
    return of(this._featured()).pipe(delay(0));
  }

  toggleSaved(id: string): void {
    if (!this.auth.isLoggedIn()) return;
    const numId = parseInt(id, 10);
    if (isNaN(numId)) return;
    const current = this.items().find((item) => item.id === id)?.isSaved ?? false;
    const next = !current;
    this.favoriteState.set('event', id, next);
    this.analytics.track(next ? 'favorite_added' : 'favorite_removed', {
      entityType: 'event',
      entityId: numId,
      metadata: { surface: 'events_listing' },
    });
    this.http.post('/api/favorites/toggle', { entityType: 'event', entityId: numId }).subscribe({
      error: () => this.favoriteState.set('event', id, current),
    });
  }
}
