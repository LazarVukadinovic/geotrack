import { Injectable, computed, signal } from '@angular/core';
import { delay, Observable, of } from 'rxjs';
import type { EventListingItem, EventsFeatured } from '../models/events-listing.model';
import { MOCK_EVENTS_LISTING, EVENTS_FEATURED } from '../data/mock-events-listing.data';

/**
 * Events listing data access.
 * Replace mock observables with HttpClient calls when the API is ready.
 */
@Injectable({ providedIn: 'root' })
export class EventsListingService {
  private readonly _items = signal<EventListingItem[]>([...MOCK_EVENTS_LISTING]);
  private readonly _savedOverrides = signal<Record<string, boolean>>({});

  readonly featured: EventsFeatured = EVENTS_FEATURED;

  readonly items = computed(() => {
    const overrides = this._savedOverrides();
    return this._items().map((item) => ({
      ...item,
      isSaved: overrides[item.id] !== undefined ? overrides[item.id] : item.isSaved,
    }));
  });

  /** API-shaped stream — swap for HttpClient.get<EventListingItem[]>('/api/events') when ready. */
  getItems$(): Observable<EventListingItem[]> {
    return of(this.items()).pipe(delay(0));
  }

  /** API-shaped stream — swap for HttpClient.get<EventsFeatured>('/api/events/featured') when ready. */
  getFeatured$(): Observable<EventsFeatured> {
    return of(this.featured).pipe(delay(0));
  }

  toggleSaved(id: string): void {
    const current = this.items().find((item) => item.id === id)?.isSaved ?? false;
    this._savedOverrides.update((m) => ({ ...m, [id]: !current }));
  }
}
