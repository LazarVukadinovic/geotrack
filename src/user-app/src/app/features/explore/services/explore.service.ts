import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { delay, Observable, of } from 'rxjs';
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

interface ExploreApiResponse {
  selectedDestination?: { id: number; name: string; latitude: number; longitude: number };
  eventsThisWeek: { id: number; title: string; subtitle?: string; dateLabel?: string; image?: string }[];
  topStays: { id: number; title: string; image?: string; rating: number; reviewCount?: string; distance?: string; pricePerNight: number; isSaved: boolean }[];
  popularNearby: { id: number; title: string; categoryLabel?: string; image?: string; rating: number; reviewCount?: string; distance?: string; isSaved: boolean }[];
  mustSeeAttractions: { id: number; title: string; type?: string; image?: string }[];
  landmarkCategories: { id: number; title: string; image?: string; categoryKey?: string }[];
  curatedForYou: { id: number; title: string; subtitle?: string; categoryLabel?: string; image?: string; isSaved: boolean }[];
  localFavorites: { id: number; title: string; subtitle?: string; description?: string; category?: string; type?: string; location?: string; image?: string; rating?: number; isSaved: boolean; openingHours?: string; isRecommended: boolean }[];
}

@Injectable({
  providedIn: 'root',
})
export class ExploreService {
  private readonly http = inject(HttpClient);

  private readonly _curatedForYou = signal<ExploreCuratedItem[]>([]);
  private readonly _localFavorites = signal<ExploreItem[]>([]);
  private readonly _nearbyStatus = signal<NearbyStatus>({ ...MOCK_NEARBY_STATUS });
  private readonly _promo = signal<ExplorePromoCard>({ ...MOCK_PROMO_CARD });
  private readonly _eventsThisWeek = signal<ExploreEvent[]>([]);
  private readonly _popularNearby = signal<ExploreNearbyItem[]>([]);
  private readonly _topStays = signal<ExploreStay[]>([]);
  private readonly _mustSeeAttractions = signal<ExploreAttraction[]>([]);
  private readonly _landmarkCategories = signal<ExploreLandmarkCategory[]>([]);
  private readonly _loaded = signal(false);

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

  constructor() {
    this.loadFromApi();
  }

  private loadFromApi(): void {
    // Default to Dubrovnik coordinates
    this.http.get<ExploreApiResponse>('/api/explore?latitude=42.6507&longitude=18.0944').subscribe({
      next: (res) => {
        this._eventsThisWeek.set(res.eventsThisWeek.map(e => ({
          id: String(e.id),
          title: e.title,
          subtitle: e.subtitle ?? '',
          dateLabel: e.dateLabel ?? '',
          image: e.image ?? '',
        })));
        this._topStays.set(res.topStays.map(s => ({
          id: String(s.id),
          title: s.title,
          image: s.image ?? '',
          rating: s.rating,
          reviewCount: s.reviewCount,
          distance: s.distance ?? '',
          pricePerNight: s.pricePerNight,
          isSaved: s.isSaved,
        })));
        this._popularNearby.set(res.popularNearby.map(p => ({
          id: String(p.id),
          title: p.title,
          categoryLabel: p.categoryLabel ?? '',
          image: p.image ?? '',
          rating: p.rating,
          reviewCount: p.reviewCount ?? '',
          distance: p.distance ?? '',
          isSaved: p.isSaved,
        })));
        this._mustSeeAttractions.set(res.mustSeeAttractions.map(a => ({
          id: String(a.id),
          title: a.title,
          type: a.type ?? '',
          image: a.image ?? '',
        })));
        this._landmarkCategories.set(res.landmarkCategories.map(c => ({
          id: String(c.id),
          title: c.title,
          image: c.image ?? '',
          categoryKey: c.categoryKey ?? '',
        })));
        this._curatedForYou.set(res.curatedForYou.map(c => ({
          id: String(c.id),
          title: c.title,
          subtitle: c.subtitle ?? '',
          categoryLabel: c.categoryLabel ?? '',
          image: c.image ?? '',
          isSaved: c.isSaved,
        })));
        this._localFavorites.set(res.localFavorites.map(f => ({
          id: String(f.id),
          title: f.title,
          subtitle: f.subtitle ?? '',
          description: f.description ?? '',
          category: (f.category ?? 'attraction') as ExploreItem['category'],
          type: f.type ?? '',
          location: f.location ?? '',
          image: f.image ?? '',
          rating: f.rating,
          isSaved: f.isSaved,
          openingHours: f.openingHours,
          isRecommended: f.isRecommended,
        })));
        if (res.selectedDestination) {
          this._nearbyStatus.set({
            label: 'Near',
            value: res.selectedDestination.name,
          });
        }
        this._loaded.set(true);
      },
      error: () => {
        // Fallback to mock data
        this._curatedForYou.set([...MOCK_CURATED_FOR_YOU]);
        this._localFavorites.set([...MOCK_LOCAL_FAVORITES]);
        this._eventsThisWeek.set([...MOCK_EVENTS_THIS_WEEK]);
        this._popularNearby.set([...MOCK_POPULAR_NEARBY]);
        this._topStays.set([...MOCK_TOP_STAYS]);
        this._mustSeeAttractions.set([...MOCK_MUST_SEE_ATTRACTIONS]);
        this._landmarkCategories.set([...MOCK_LANDMARK_CATEGORIES]);
        this._loaded.set(true);
      },
    });
  }

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
    // Fire-and-forget API call
    this.http.post('/api/favorites/toggle', { entityType: 'place', entityId: parseInt(id, 10) }).subscribe();
  }
}
