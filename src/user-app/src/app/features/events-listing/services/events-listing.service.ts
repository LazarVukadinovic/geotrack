import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { delay, Observable, of } from 'rxjs';
import { AuthService } from '../../../core/services/auth.service';
import { DestinationService } from '../../../core/services/destination.service';
import { ExploreService } from '../../explore/services/explore.service';
import type { EventListingItem, EventsFeatured } from '../models/events-listing.model';
import { MOCK_EVENTS_LISTING, EVENTS_FEATURED } from '../data/mock-events-listing.data';

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
  private readonly exploreSvc = inject(ExploreService);
  private readonly _items = signal<EventListingItem[]>([]);
  private readonly _savedOverrides = signal<Record<string, boolean>>({});
  private readonly _featured = signal<EventsFeatured>(EVENTS_FEATURED);
  private loadedDestinationId: number | null = null;

  readonly featured = computed(() => this._featured());

  readonly items = computed(() => {
    const overrides = this._savedOverrides();
    return this._items().map((item) => ({
      ...item,
      isSaved: overrides[item.id] !== undefined ? overrides[item.id] : item.isSaved,
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
    this.http.get<ApiEventsResponse>(`/api/events?destinationId=${destinationId}`).subscribe({
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
      },
      error: () => {
        this._items.set([...MOCK_EVENTS_LISTING]);
        this.loadedDestinationId = destinationId;
      },
    });
  }

  getItems$(): Observable<EventListingItem[]> {
    return of(this.items()).pipe(delay(0));
  }

  getFeatured$(): Observable<EventsFeatured> {
    return of(this._featured()).pipe(delay(0));
  }

  toggleSaved(id: string): void {
    if (!this.auth.isLoggedIn()) return;

    const flip = () => {
      const current = this.items().find((item) => item.id === id)?.isSaved ?? false;
      this._savedOverrides.update((m) => ({ ...m, [id]: !current }));
    };
    flip();

    const numId = parseInt(id, 10);
    if (isNaN(numId)) return;
    this.http.post('/api/favorites/toggle', { entityType: 'event', entityId: numId }).subscribe({
      error: () => flip(),
    });
  }
}
