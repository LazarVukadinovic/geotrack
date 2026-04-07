import { Injectable, computed, signal } from '@angular/core';
import { delay, Observable, of } from 'rxjs';
import type { HotelListItem } from '../models/hotels-listing.model';
import { MOCK_HOTELS_LISTING } from '../data/mock-hotels-listing.data';

/**
 * Hotels listing data access. Replace mock observables with HttpClient calls when the API is ready.
 */
@Injectable({ providedIn: 'root' })
export class HotelsListingService {
  private readonly _hotels = signal<HotelListItem[]>([...MOCK_HOTELS_LISTING]);
  private readonly _savedOverrides = signal<Record<string, boolean>>({});

  readonly hotels = computed(() => {
    const overrides = this._savedOverrides();
    return this._hotels().map((h) => ({
      ...h,
      isSaved: overrides[h.id] !== undefined ? overrides[h.id] : h.isSaved,
    }));
  });

  /** API-shaped stream — swap implementation for HttpClient.get when backend is ready. */
  getHotels$(): Observable<HotelListItem[]> {
    return of(this.hotels()).pipe(delay(0));
  }

  toggleSaved(id: string): void {
    const current = this.hotels().find((h) => h.id === id)?.isSaved ?? false;
    this._savedOverrides.update((m) => ({ ...m, [id]: !current }));
  }
}
