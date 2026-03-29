import { Injectable, computed, signal } from '@angular/core';
import { delay, map, Observable, of } from 'rxjs';
import type { ExploreItem } from '../../../core/models/explore-item.model';
import type { ExploreEvent, ExplorePromoCard, NearbyStatus } from '../models/explore-extras.model';
import {
  MOCK_EVENTS_THIS_WEEK,
  MOCK_LOCAL_FAVORITES,
  MOCK_NEARBY_STATUS,
  MOCK_PROMO_CARD,
  MOCK_RECOMMENDED_ITEMS,
} from '../data/mock-explore.data';

/**
 * Explore data access. Replace mock observables with HttpClient calls when the API is ready.
 */
@Injectable({
  providedIn: 'root',
})
export class ExploreService {
  private readonly _recommended = signal<ExploreItem[]>([...MOCK_RECOMMENDED_ITEMS]);
  private readonly _localFavorites = signal<ExploreItem[]>([...MOCK_LOCAL_FAVORITES]);
  private readonly _nearbyStatus = signal<NearbyStatus>({ ...MOCK_NEARBY_STATUS });
  private readonly _promo = signal<ExplorePromoCard>({ ...MOCK_PROMO_CARD });
  private readonly _eventsThisWeek = signal<ExploreEvent[]>([...MOCK_EVENTS_THIS_WEEK]);

  /** Reactive lists for templates that prefer signals. */
  readonly recommended = computed(() => this._recommended());
  readonly localFavorites = computed(() => this._localFavorites());
  readonly nearbyStatus = computed(() => this._nearbyStatus());
  readonly promo = computed(() => this._promo());
  readonly eventsThisWeek = computed(() => this._eventsThisWeek());

  /** Example API-shaped stream — swap implementation for HttpClient.get. */
  getRecommended$(): Observable<ExploreItem[]> {
    return of(this._recommended()).pipe(delay(0));
  }

  getLocalFavorites$(): Observable<ExploreItem[]> {
    return of(this._localFavorites()).pipe(delay(0));
  }

  getEventsThisWeek$(): Observable<ExploreEvent[]> {
    return of(this._eventsThisWeek()).pipe(delay(0));
  }

  toggleSaved(id: string, list: 'recommended' | 'localFavorites'): void {
    if (list === 'recommended') {
      this._recommended.update((items) =>
        items.map((item) => (item.id === id ? { ...item, isSaved: !item.isSaved } : item)),
      );
      return;
    }
    this._localFavorites.update((items) =>
      items.map((item) => (item.id === id ? { ...item, isSaved: !item.isSaved } : item)),
    );
  }

  /** Future: map API DTO to ExploreItem here. */
  mapFromApi(dto: unknown): ExploreItem[] {
    return dto as ExploreItem[];
  }

  /** Example refresh hook for pull-to-refresh / backend sync. */
  refreshFromApi$(): Observable<{ recommended: ExploreItem[]; localFavorites: ExploreItem[] }> {
    return of({
      recommended: this._recommended(),
      localFavorites: this._localFavorites(),
    }).pipe(
      map((bundle) => ({
        recommended: bundle.recommended.map((i) => ({ ...i })),
        localFavorites: bundle.localFavorites.map((i) => ({ ...i })),
      })),
    );
  }
}
