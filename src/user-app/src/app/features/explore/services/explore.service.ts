import { Injectable, computed, signal } from '@angular/core';
import { delay, map, Observable, of } from 'rxjs';
import type { ExploreItem } from '../../../core/models/explore-item.model';
import type {
  ExploreAttraction,
  ExploreCuratedItem,
  ExploreEvent,
  ExploreLandmarkCategory,
  ExploreNearbyItem,
  ExplorePromoCard,
  ExploreStay,
  NearbyStatus,
} from '../models/explore-extras.model';
import {
  MOCK_CURATED_FOR_YOU,
  MOCK_EVENTS_THIS_WEEK,
  MOCK_LANDMARK_CATEGORIES,
  MOCK_LOCAL_FAVORITES,
  MOCK_MUST_SEE_ATTRACTIONS,
  MOCK_NEARBY_STATUS,
  MOCK_POPULAR_NEARBY,
  MOCK_PROMO_CARD,
  MOCK_TOP_STAYS,
} from '../data/mock-explore.data';

/**
 * Explore data access. Replace mock observables with HttpClient calls when the API is ready.
 */
@Injectable({
  providedIn: 'root',
})
export class ExploreService {
  private readonly _curatedForYou = signal<ExploreCuratedItem[]>([...MOCK_CURATED_FOR_YOU]);
  private readonly _localFavorites = signal<ExploreItem[]>([...MOCK_LOCAL_FAVORITES]);
  private readonly _nearbyStatus = signal<NearbyStatus>({ ...MOCK_NEARBY_STATUS });
  private readonly _promo = signal<ExplorePromoCard>({ ...MOCK_PROMO_CARD });
  private readonly _eventsThisWeek = signal<ExploreEvent[]>([...MOCK_EVENTS_THIS_WEEK]);
  private readonly _popularNearby = signal<ExploreNearbyItem[]>([...MOCK_POPULAR_NEARBY]);
  private readonly _topStays = signal<ExploreStay[]>([...MOCK_TOP_STAYS]);
  private readonly _mustSeeAttractions = signal<ExploreAttraction[]>([...MOCK_MUST_SEE_ATTRACTIONS]);
  private readonly _landmarkCategories = signal<ExploreLandmarkCategory[]>([...MOCK_LANDMARK_CATEGORIES]);

  /** Reactive lists for templates that prefer signals. */
  readonly curatedForYou = computed(() => this._curatedForYou());
  readonly localFavorites = computed(() => this._localFavorites());
  readonly nearbyStatus = computed(() => this._nearbyStatus());
  readonly promo = computed(() => this._promo());
  readonly eventsThisWeek = computed(() => this._eventsThisWeek());
  readonly popularNearby = computed(() => this._popularNearby());
  readonly topStays = computed(() => this._topStays());
  readonly mustSeeAttractions = computed(() => this._mustSeeAttractions());
  readonly landmarkCategories = computed(() => this._landmarkCategories());

  /** Example API-shaped streams — swap implementation for HttpClient.get. */
  getLocalFavorites$(): Observable<ExploreItem[]> {
    return of(this._localFavorites()).pipe(delay(0));
  }

  getEventsThisWeek$(): Observable<ExploreEvent[]> {
    return of(this._eventsThisWeek()).pipe(delay(0));
  }

  getPopularNearby$(): Observable<ExploreNearbyItem[]> {
    return of(this._popularNearby()).pipe(delay(0));
  }

  getTopStays$(): Observable<ExploreStay[]> {
    return of(this._topStays()).pipe(delay(0));
  }

  getMustSeeAttractions$(): Observable<ExploreAttraction[]> {
    return of(this._mustSeeAttractions()).pipe(delay(0));
  }

  getLandmarkCategories$(): Observable<ExploreLandmarkCategory[]> {
    return of(this._landmarkCategories()).pipe(delay(0));
  }

  toggleSaved(id: string, list: 'curatedForYou' | 'localFavorites' | 'popularNearby' | 'topStays'): void {
    if (list === 'curatedForYou') {
      this._curatedForYou.update((items) =>
        items.map((item) => (item.id === id ? { ...item, isSaved: !item.isSaved } : item)),
      );
      return;
    }
    if (list === 'localFavorites') {
      this._localFavorites.update((items) =>
        items.map((item) => (item.id === id ? { ...item, isSaved: !item.isSaved } : item)),
      );
      return;
    }
    if (list === 'popularNearby') {
      this._popularNearby.update((items) =>
        items.map((item) => (item.id === id ? { ...item, isSaved: !item.isSaved } : item)),
      );
      return;
    }
    if (list === 'topStays') {
      this._topStays.update((items) =>
        items.map((item) => (item.id === id ? { ...item, isSaved: !item.isSaved } : item)),
      );
    }
  }

  /** Future: map API DTO to ExploreItem here. */
  mapFromApi(dto: unknown): ExploreItem[] {
    return dto as ExploreItem[];
  }

  /** Example refresh hook for pull-to-refresh / backend sync. */
  refreshFromApi$(): Observable<{ localFavorites: ExploreItem[] }> {
    return of({
      localFavorites: this._localFavorites(),
    }).pipe(
      map((bundle) => ({
        localFavorites: bundle.localFavorites.map((i) => ({ ...i })),
      })),
    );
  }
}
