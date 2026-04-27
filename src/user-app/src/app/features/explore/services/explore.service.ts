import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { delay, Observable, of } from 'rxjs';
import { AuthService } from '../../../core/services/auth.service';
import { DestinationService } from '../../../core/services/destination.service';
import { FavoriteEntityType, FavoriteStateService } from '../../../core/services/favorite-state.service';
import { LocationService } from '../../../core/services/location.service';
import { AnalyticsService } from '../../../core/services/analytics.service';
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

interface ExploreApiResponse {
  selectedDestination?: { id: number; name: string; latitude: number; longitude: number };
  eventsThisWeek: { id: number; title: string; subtitle?: string; dateLabel?: string; image?: string }[];
  topStays: { id: number; title: string; image?: string; rating?: number; reviewCount?: string; distance?: string; pricePerNight: number; isSaved: boolean }[];
  popularNearby: { id: number; title: string; categoryLabel?: string; image?: string; rating?: number; reviewCount?: string; distance?: string; isSaved: boolean }[];
  mustSeeAttractions: { id: number; title: string; type?: string; image?: string }[];
  landmarkCategories: { id: number; title: string; image?: string; categoryKey?: string }[];
  curatedForYou: { id: number; title: string; subtitle?: string; categoryLabel?: string; image?: string; isSaved: boolean }[];
  localFavorites: { id: number; title: string; subtitle?: string; description?: string; category?: string; type?: string; location?: string; image?: string; rating?: number; reviewCount?: string; isSaved: boolean; openingHours?: string; isRecommended: boolean }[];
}

/** Minimum distance in km to consider a location "meaningfully different" for reloading. */
const RELOAD_THRESHOLD_KM = 5;

const DEFAULT_NEARBY_STATUS: NearbyStatus = {
  label: 'Near',
  value: 'Resolving destination',
};

const AUTH_PROMO_CARD: ExplorePromoCard = {
  title: 'Personalize GeoTrack',
  message: 'Sign in to save places, manage collections, and keep your profile preferences synced.',
  ctaLabel: 'Sign In',
};

@Injectable({
  providedIn: 'root',
})
export class ExploreService {
  private readonly http = inject(HttpClient);
  private readonly locationSvc = inject(LocationService);
  private readonly auth = inject(AuthService);
  private readonly destinationSvc = inject(DestinationService);
  private readonly favoriteState = inject(FavoriteStateService);
  private readonly analytics = inject(AnalyticsService);

  private readonly _curatedForYou = signal<ExploreCuratedItem[]>([]);
  private readonly _localFavorites = signal<ExploreItem[]>([]);
  private readonly _nearbyStatus = signal<NearbyStatus>({ ...DEFAULT_NEARBY_STATUS });
  private readonly _promo = signal<ExplorePromoCard>({ ...AUTH_PROMO_CARD });
  private readonly _eventsThisWeek = signal<ExploreEvent[]>([]);
  private readonly _popularNearby = signal<ExploreNearbyItem[]>([]);
  private readonly _topStays = signal<ExploreStay[]>([]);
  private readonly _mustSeeAttractions = signal<ExploreAttraction[]>([]);
  private readonly _landmarkCategories = signal<ExploreLandmarkCategory[]>([]);
  private readonly _loaded = signal(false);
  private readonly _loadError = signal<string | null>(null);

  /** Coordinates used for the last successful load. */
  private loadedLat = 0;
  private loadedLng = 0;
  private loadedSource: 'gps' | 'ip' | 'manual' | null = null;

  /** Reactive lists for templates that prefer signals. */
  readonly curatedForYou = computed(() => this.withSavedState(this._curatedForYou(), 'place'));
  readonly localFavorites = computed(() => this.withSavedState(this._localFavorites(), 'place'));
  readonly nearbyStatus = computed(() => this._nearbyStatus());
  readonly promo = computed(() => this._promo());
  readonly eventsThisWeek = computed(() => this._eventsThisWeek());
  readonly popularNearby = computed(() => this.withSavedState(this._popularNearby(), 'place'));
  readonly topStays = computed(() => this.withSavedState(this._topStays(), 'place'));
  readonly mustSeeAttractions = computed(() => this._mustSeeAttractions());
  readonly landmarkCategories = computed(() => this._landmarkCategories());
  readonly loaded = computed(() => this._loaded());
  readonly loadError = computed(() => this._loadError());

  constructor() {
    // React to location changes — reload explore data when city meaningfully changes
    effect(() => {
      const loc = this.locationSvc.location();
      if (!loc) return;

      const dist = this.haversineKm(this.loadedLat, this.loadedLng, loc.latitude, loc.longitude);
      const sourceChanged = this.loadedSource !== loc.source;
      // First load (0,0 → real) always triggers. After that we still reload when
      // the source changes, so a fresh GPS fix can replace an approximate IP load
      // even if both coordinates are within the same city.
      if (this._loaded() && dist < RELOAD_THRESHOLD_KM && !sourceChanged) return;

      this.loadFromApi(loc.latitude, loc.longitude, undefined, loc.source);
    });
  }

  /**
   * Explicitly load explore data for given coordinates.
   * Returns a Promise that resolves when the load finishes.
   * Used by the landing page for preloading before navigation.
   */
  loadForLocation(lat: number, lng: number): Promise<void> {
    const dist = this.haversineKm(this.loadedLat, this.loadedLng, lat, lng);
    const source = this.locationSvc.location()?.source ?? null;
    if (this._loaded() && dist < RELOAD_THRESHOLD_KM && this.loadedSource === source) {
      return Promise.resolve(); // already loaded for a nearby location
    }
    return new Promise((resolve) => {
      this.loadFromApi(lat, lng, resolve, source);
    });
  }

  private loadFromApi(
    lat: number,
    lng: number,
    onComplete?: () => void,
    source?: 'gps' | 'ip' | 'manual' | null,
  ): void {
    this._loadError.set(null);

    this.http.get<ExploreApiResponse>(`/api/explore?latitude=${lat}&longitude=${lng}`).subscribe({
      next: (res) => {
        this.applyResponse(res);
        this.loadedLat = lat;
        this.loadedLng = lng;
        this.loadedSource = source ?? this.locationSvc.location()?.source ?? null;
        this._loadError.set(null);
        this._loaded.set(true);
        onComplete?.();
      },
      error: () => {
        this._eventsThisWeek.set([]);
        this._topStays.set([]);
        this._popularNearby.set([]);
        this._mustSeeAttractions.set([]);
        this._landmarkCategories.set([]);
        this._curatedForYou.set([]);
        this._localFavorites.set([]);
        this.loadedLat = lat;
        this.loadedLng = lng;
        this.loadedSource = source ?? this.locationSvc.location()?.source ?? null;
        this._loadError.set("We couldn't load the Explore feed. Check your connection and try again.");
        this._loaded.set(true);
        onComplete?.();
      },
    });
  }

  private applyResponse(res: ExploreApiResponse): void {
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
      reviewCount: p.reviewCount,
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
      reviewCount: f.reviewCount,
      isSaved: f.isSaved,
      openingHours: f.openingHours,
      isRecommended: f.isRecommended,
    })));
    if (res.selectedDestination) {
      this._nearbyStatus.set({
        label: 'Near',
        value: res.selectedDestination.name,
      });
      this.destinationSvc.set({
        id: res.selectedDestination.id,
        name: res.selectedDestination.name,
      });
    }
  }

  private haversineKm(lat1: number, lon1: number, lat2: number, lon2: number): number {
    const R = 6371;
    const toRad = (deg: number) => (deg * Math.PI) / 180;
    const dLat = toRad(lat2 - lat1);
    const dLon = toRad(lon2 - lon1);
    const a =
      Math.sin(dLat / 2) ** 2 +
      Math.cos(toRad(lat1)) * Math.cos(toRad(lat2)) * Math.sin(dLon / 2) ** 2;
    return R * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
  }

  /** Signal-backed streams kept for existing components that consume observables. */
  getLocalFavorites$(): Observable<ExploreItem[]> {
    return of(this.localFavorites()).pipe(delay(0));
  }

  getEventsThisWeek$(): Observable<ExploreEvent[]> {
    return of(this._eventsThisWeek()).pipe(delay(0));
  }

  getPopularNearby$(): Observable<ExploreNearbyItem[]> {
    return of(this.popularNearby()).pipe(delay(0));
  }

  getTopStays$(): Observable<ExploreStay[]> {
    return of(this.topStays()).pipe(delay(0));
  }

  getMustSeeAttractions$(): Observable<ExploreAttraction[]> {
    return of(this._mustSeeAttractions()).pipe(delay(0));
  }

  getLandmarkCategories$(): Observable<ExploreLandmarkCategory[]> {
    return of(this._landmarkCategories()).pipe(delay(0));
  }

  toggleSaved(id: string, list: 'curatedForYou' | 'localFavorites' | 'popularNearby' | 'topStays'): void {
    if (!this.auth.isLoggedIn()) return;

    const numId = parseInt(id, 10);
    if (isNaN(numId)) return;
    const current = this.currentSavedStateForList(list, id);
    const next = !current;
    this.favoriteState.set('place', id, next);
    this.analytics.track(next ? 'favorite_added' : 'favorite_removed', {
      entityType: list === 'topStays' ? 'hotel' : 'place',
      entityId: numId,
      metadata: { surface: `explore_${list}` },
    });

    this.http.post('/api/favorites/toggle', { entityType: 'place', entityId: numId }).subscribe({
      error: () => this.favoriteState.set('place', id, current),
    });
  }

  private currentSavedStateForList(
    list: 'curatedForYou' | 'localFavorites' | 'popularNearby' | 'topStays',
    id: string,
  ): boolean {
    if (list === 'curatedForYou') {
      return this.curatedForYou().find((item) => item.id === id)?.isSaved ?? false;
    }
    if (list === 'localFavorites') {
      return this.localFavorites().find((item) => item.id === id)?.isSaved ?? false;
    }
    if (list === 'popularNearby') {
      return this.popularNearby().find((item) => item.id === id)?.isSaved ?? false;
    }
    return this.topStays().find((item) => item.id === id)?.isSaved ?? false;
  }

  private withSavedState<T extends { id: string; isSaved: boolean }>(
    items: T[],
    entityType: FavoriteEntityType,
  ): T[] {
    return items.map((item) => {
      const isSaved = this.favoriteState.resolve(entityType, item.id, item.isSaved);
      return item.isSaved === isSaved ? item : { ...item, isSaved };
    });
  }
}
