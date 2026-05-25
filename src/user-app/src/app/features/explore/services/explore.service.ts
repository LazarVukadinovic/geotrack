import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { delay, Observable, of } from 'rxjs';
import { AuthService } from '../../../core/services/auth.service';
import { DestinationService } from '../../../core/services/destination.service';
import { FavoriteEntityType, FavoriteStateService } from '../../../core/services/favorite-state.service';
import { LocationService } from '../../../core/services/location.service';
import { SaveToCollectionService } from '../../../core/services/save-to-collection.service';
import { TagService } from '../../../core/services/tag.service';
import { UserService } from '../../../core/services/user.service';
import type { ExploreItem } from '../../../core/models/explore-item.model';
import type {
  ExploreAttraction,
  ExploreCuratedItem,
  ExploreEvent,
  ExploreLandmarkCategory,
  ExploreNearbyItem,
  ExplorePromoCard,
  ExploreSection,
  ExploreSectionItem,
  ExploreStay,
  NearbyStatus,
} from '../models/explore-extras.model';

interface ExploreApiResponse {
  selectedDestination?: { id: number; name: string; latitude: number; longitude: number; territoryId?: number; territoryName?: string };
  sections?: ApiExploreSection[];

  // Legacy payload support while backend/frontend are being switched together.
  eventsThisWeek?: { id: number; title: string; subtitle?: string; dateLabel?: string; image?: string }[];
  topStays?: { id: number; title: string; image?: string; rating?: number; reviewCount?: string; distance?: string; pricePerNight?: number; isSaved: boolean }[];
  curatedForYou?: { id: number; title: string; subtitle?: string; categoryLabel?: string; image?: string; isSaved: boolean }[];
  dynamicSections?: ApiExploreSection[];
}

interface ApiExploreSection {
  tagTypeId?: number;
  key: string;
  title: string;
  subtitle?: string;
  layout?: string;
  cardType?: string;
  moreRoute?: string;
  priority?: number;
  items: ApiExploreSectionItem[];
}

interface ApiExploreSectionItem {
  id: number;
  entityType?: string;
  detailRoute?: string;
  title: string;
  subtitle?: string;
  description?: string;
  categoryLabel?: string;
  locationName?: string;
  image?: string;
  rating?: number;
  reviewCount?: string;
  distance?: string;
  dateLabel?: string;
  openingHours?: string;
  statusBadge?: { label?: string; tone?: string };
  pricePerNight?: number;
  isSaved?: boolean;
  tags?: { tagId: number; name: string; slug: string }[];
}

const RELOAD_THRESHOLD_KM = 5;

const DEFAULT_NEARBY_STATUS: NearbyStatus = {
  label: 'Near',
  value: 'Resolving destination',
};

const AUTH_PROMO_CARD: ExplorePromoCard = {
  title: 'Personalize your stay',
  message: 'Sign in to save places, manage collections, and keep your profile preferences synced.',
  ctaLabel: 'Sign In',
};

type SelectedExploreDestination = NonNullable<ExploreApiResponse['selectedDestination']>;


export interface ExploreSearchResultItem {
  id: number;
  entityType: 'place' | 'location' | string;
  placeKind: 'place' | 'stay' | 'area' | string;
  displayType: 'Place' | 'Area' | string;
  title: string;
  subtitle?: string | null;
  description?: string | null;
  categoryLabel?: string | null;
  image?: string | null;
  detailRoute: string;
  destinationId: number;
  locationName?: string | null;
  latitude?: number | null;
  longitude?: number | null;
  rating?: number | null;
  reviewCount?: string | null;
  tags: string[];
  score: number;
}

export interface ExploreSearchResponse {
  destinationId: number;
  query: string;
  items: ExploreSearchResultItem[];
  totalCount: number;
  skip: number;
  take: number;
  nextSkip: number;
  hasMore: boolean;
}

@Injectable({ providedIn: 'root' })
export class ExploreService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly userService = inject(UserService);
  private readonly locationSvc = inject(LocationService);
  private readonly destinationSvc = inject(DestinationService);
  private readonly favoriteState = inject(FavoriteStateService);
  private readonly saveToCollection = inject(SaveToCollectionService);
  private readonly tagService = inject(TagService);

  private readonly _sections = signal<ExploreSection[]>([]);
  private readonly _nearbyStatus = signal<NearbyStatus>({ ...DEFAULT_NEARBY_STATUS });
  private readonly _selectedDestination = signal<SelectedExploreDestination | null>(null);
  private readonly _promo = signal<ExplorePromoCard>({ ...AUTH_PROMO_CARD });
  private readonly _loaded = signal(false);
  private readonly _loading = signal(false);
  private readonly _loadError = signal<string | null>(null);

  private loadedLat = 0;
  private loadedLng = 0;
  private loadedSource: 'gps' | 'approximate' | 'manual' | 'destination' | 'default' | 'unknown' | null = null;
  private loadedDestinationId: number | null = null;
  private loadedCurationContextKey: string | null = null;
  private loadedRequestKey: string | null = null;
  private loadingRequestKey: string | null = null;
  private requestVersion = 0;

  private readonly curationContextKey = computed(() => {
    if (!this.auth.isLoggedIn()) return 'guest';

    const profile = this.userService.profile();
    if (!profile) return null;

    return [
      'user',
      profile.id,
      profile.needsInterestsOnboarding ? 'onboarding' : 'ready',
      this.tagService.interestRevision(),
    ].join(':');
  });

  readonly sections = computed(() =>
    this._sections().map(section => ({
      ...section,
      items: section.items.map(item => this.resolveSavedState(item)),
    })),
  );

  readonly selectedDestination = computed(() => this._selectedDestination());

  readonly nearbyStatus = computed(() => this._nearbyStatus());
  readonly promo = computed(() => this._promo());
  readonly loaded = computed(() => this._loaded());
  readonly loading = computed(() => this._loading());
  readonly loadError = computed(() => this._loadError());

  // Compatibility signals while other old components/tests are still around.
  readonly curatedForYou = computed<ExploreCuratedItem[]>(() =>
    this.itemsForSection('curated-for-you').map(item => ({
      id: item.id,
      title: item.title,
      subtitle: item.subtitle ?? '',
      categoryLabel: item.categoryLabel ?? '',
      image: item.image,
      isSaved: item.isSaved,
    })),
  );

  readonly topStays = computed<ExploreStay[]>(() =>
    this._sections().flatMap(section => section.items)
      .filter(item => item.entityType === 'stay')
      .map(item => ({
        id: item.id,
        title: item.title,
        image: item.image,
        rating: item.rating,
        reviewCount: item.reviewCount,
        distance: item.distance ?? '',
        pricePerNight: item.pricePerNight ?? 0,
        isSaved: this.resolveSavedState(item).isSaved,
      })),
  );

  readonly eventsThisWeek = computed<ExploreEvent[]>(() =>
    this._sections().flatMap(section => section.items)
      .filter(item => item.entityType === 'event')
      .map(item => ({
        id: item.id,
        title: item.title,
        subtitle: item.subtitle ?? '',
        dateLabel: item.dateLabel ?? '',
        image: item.image,
      })),
  );

  readonly localFavorites = computed<ExploreItem[]>(() => []);
  readonly popularNearby = computed<ExploreNearbyItem[]>(() => []);
  readonly mustSeeAttractions = computed<ExploreAttraction[]>(() => []);
  readonly landmarkCategories = computed<ExploreLandmarkCategory[]>(() => []);

  constructor() {
    effect(() => {
      const curationContextKey = this.curationContextKey();
      if (curationContextKey !== this.loadedCurationContextKey) {
        this.removeUserSpecificSections();
      }
    });

    effect(() => {
      const curationContextKey = this.curationContextKey();
      if (!curationContextKey) return;
      if (this.destinationSvc.mode() !== 'destination') return;

      const destinationId = this.destinationSvc.id();
      if (destinationId == null) return;

      const requestKey = this.buildDestinationRequestKey(destinationId, curationContextKey);
      if (this.loadingRequestKey === requestKey) return;
      if (this._loaded() && this.loadedRequestKey === requestKey) return;

      this.loadDestinationFromApi(destinationId, undefined, curationContextKey, requestKey);
    });

    effect(() => {
      const curationContextKey = this.curationContextKey();
      if (!curationContextKey) return;
      if (this.destinationSvc.mode() === 'destination') return;

      const loc = this.locationSvc.location();
      if (!loc) return;

      const dist = this.haversineKm(this.loadedLat, this.loadedLng, loc.latitude, loc.longitude);
      const sourceChanged = this.loadedSource !== loc.source;
      const curationChanged = this.loadedCurationContextKey !== curationContextKey;
      const requestKey = this.buildLocationRequestKey(loc.latitude, loc.longitude, loc.source, curationContextKey);

      if (this.loadingRequestKey === requestKey) return;
      if (
        this._loaded() &&
        !curationChanged &&
        dist < RELOAD_THRESHOLD_KM &&
        !sourceChanged &&
        this.loadedSource !== 'destination'
      ) {
        return;
      }

      this.loadFromApi(loc.latitude, loc.longitude, undefined, loc.source, curationContextKey, requestKey);
    });
  }

  loadForLocation(lat: number, lng: number): Promise<void> {
    const curationContextKey = this.curationContextKey();
    if (!curationContextKey) return Promise.resolve();

    const dist = this.haversineKm(this.loadedLat, this.loadedLng, lat, lng);
    const source = this.locationSvc.location()?.source ?? null;
    if (
      this._loaded() &&
      this.loadedCurationContextKey === curationContextKey &&
      dist < RELOAD_THRESHOLD_KM &&
      this.loadedSource === source
    ) {
      return Promise.resolve();
    }

    return new Promise((resolve) => {
      this.loadFromApi(lat, lng, resolve, source, curationContextKey);
    });
  }

  loadForDestination(destinationId: number): Promise<void> {
    if (destinationId <= 0) return Promise.resolve();

    const curationContextKey = this.curationContextKey();
    if (!curationContextKey) return Promise.resolve();

    const requestKey = this.buildDestinationRequestKey(destinationId, curationContextKey);
    if (this._loaded() && this.loadedRequestKey === requestKey) {
      return Promise.resolve();
    }

    return new Promise((resolve) => {
      this.loadDestinationFromApi(destinationId, resolve, curationContextKey, requestKey);
    });
  }

  useCurrentLocation(): void {
    this.destinationSvc.useLocationMode();
    const loc = this.locationSvc.location();
    if (!loc) {
      void this.locationSvc.ensureIpFallback().then((resolved) => {
        if (!resolved) return;
        const curationContextKey = this.curationContextKey();
        if (curationContextKey) {
          this.loadFromApi(resolved.latitude, resolved.longitude, undefined, resolved.source, curationContextKey);
        }
      });
      return;
    }

    const curationContextKey = this.curationContextKey();
    if (!curationContextKey) return;

    this.loadFromApi(loc.latitude, loc.longitude, undefined, loc.source, curationContextKey);
  }

  toggleSaved(item: ExploreSectionItem, sectionKey = 'section'): void {
    const numericId = parseInt(item.id, 10);
    if (isNaN(numericId)) return;
    if (item.entityType === 'event') return;

    const saveEntityType = item.entityType === 'stay' ? 'place' : item.entityType;
    if (saveEntityType !== 'place') return;

    this.saveToCollection.open({
      entityType: 'place',
      entityId: numericId,
      title: item.title,
      imageUrl: item.image,
      surface: `explore_${sectionKey}`,
      analyticsEntityType: item.entityType === 'stay' ? 'hotel' : 'place',
    });
  }


  searchCurrentDestination(
    query: string,
    destinationId: number,
    skip = 0,
    take = 20,
  ): Observable<ExploreSearchResponse> {
    const params = new HttpParams()
      .set('destinationId', String(destinationId))
      .set('q', query)
      .set('skip', String(skip))
      .set('take', String(take));

    return this.http.get<ExploreSearchResponse>('/api/explore/search', { params });
  }

  getSections$(): Observable<ExploreSection[]> {
    return of(this.sections()).pipe(delay(0));
  }

  getLocalFavorites$(): Observable<ExploreItem[]> {
    return of(this.localFavorites()).pipe(delay(0));
  }

  getEventsThisWeek$(): Observable<ExploreEvent[]> {
    return of(this.eventsThisWeek()).pipe(delay(0));
  }

  getPopularNearby$(): Observable<ExploreNearbyItem[]> {
    return of(this.popularNearby()).pipe(delay(0));
  }

  getTopStays$(): Observable<ExploreStay[]> {
    return of(this.topStays()).pipe(delay(0));
  }

  getMustSeeAttractions$(): Observable<ExploreAttraction[]> {
    return of(this.mustSeeAttractions()).pipe(delay(0));
  }

  getLandmarkCategories$(): Observable<ExploreLandmarkCategory[]> {
    return of(this.landmarkCategories()).pipe(delay(0));
  }

  private distanceOriginForSource(
    source?: 'gps' | 'approximate' | 'manual' | 'destination' | 'default' | 'unknown' | null,
  ): 'current-location' | 'destination-centre' {
    return source === 'gps' ? 'current-location' : 'destination-centre';
  }

  private loadFromApi(
    lat: number,
    lng: number,
    onComplete?: () => void,
    source?: 'gps' | 'approximate' | 'manual' | 'default' | 'unknown' | null,
    curationContextKey = this.curationContextKey(),
    requestKey?: string,
  ): void {
    if (!curationContextKey) {
      onComplete?.();
      return;
    }

    const effectiveRequestKey = requestKey ?? this.buildLocationRequestKey(lat, lng, source, curationContextKey);
    if (this.loadingRequestKey === effectiveRequestKey) {
      onComplete?.();
      return;
    }

    const requestId = ++this.requestVersion;
    this.loadingRequestKey = effectiveRequestKey;
    this._loadError.set(null);
    this._loading.set(true);

    const params = new HttpParams()
      .set('latitude', String(lat))
      .set('longitude', String(lng))
      .set('distanceOrigin', this.distanceOriginForSource(source));

    this.http.get<ExploreApiResponse>('/api/explore', { params }).subscribe({
      next: (res) => {
        if (this.isStaleRequest(requestId, effectiveRequestKey)) {
          onComplete?.();
          return;
        }

        this.applyResponse(res);
        this.loadedLat = lat;
        this.loadedLng = lng;
        this.loadedSource = source ?? this.locationSvc.location()?.source ?? null;
        this.loadedDestinationId = res.selectedDestination?.id ?? null;
        this.loadedCurationContextKey = curationContextKey;
        this.loadedRequestKey = effectiveRequestKey;
        this.loadingRequestKey = null;
        this._loadError.set(null);
        this._loading.set(false);
        this._loaded.set(true);
        onComplete?.();
      },
      error: () => {
        if (this.isStaleRequest(requestId, effectiveRequestKey)) {
          onComplete?.();
          return;
        }

        this.clearFeed();
        this.loadedLat = lat;
        this.loadedLng = lng;
        this.loadedSource = source ?? this.locationSvc.location()?.source ?? null;
        this.loadedDestinationId = null;
        this.loadedCurationContextKey = curationContextKey;
        this.loadedRequestKey = effectiveRequestKey;
        this.loadingRequestKey = null;
        this._loadError.set("We couldn't load the Explore feed. Check your connection and try again.");
        this._loading.set(false);
        this._loaded.set(true);
        onComplete?.();
      },
    });
  }

  private loadDestinationFromApi(
    destinationId: number,
    onComplete?: () => void,
    curationContextKey = this.curationContextKey(),
    requestKey?: string,
  ): void {
    if (!curationContextKey) {
      onComplete?.();
      return;
    }

    const effectiveRequestKey = requestKey ?? this.buildDestinationRequestKey(destinationId, curationContextKey);
    if (this.loadingRequestKey === effectiveRequestKey) {
      onComplete?.();
      return;
    }

    const requestId = ++this.requestVersion;
    this.loadingRequestKey = effectiveRequestKey;
    this._loadError.set(null);
    this._loading.set(true);

    const params = new HttpParams()
      .set('destinationId', String(destinationId))
      .set('distanceOrigin', 'destination-centre');

    this.http.get<ExploreApiResponse>('/api/explore', { params }).subscribe({
      next: (res) => {
        if (this.isStaleRequest(requestId, effectiveRequestKey)) {
          onComplete?.();
          return;
        }

        this.applyResponse(res);
        this.loadedSource = 'destination';
        this.loadedDestinationId = destinationId;
        this.loadedLat = res.selectedDestination?.latitude ?? 0;
        this.loadedLng = res.selectedDestination?.longitude ?? 0;
        this.loadedCurationContextKey = curationContextKey;
        this.loadedRequestKey = effectiveRequestKey;
        this.loadingRequestKey = null;
        this._loadError.set(null);
        this._loading.set(false);
        this._loaded.set(true);
        onComplete?.();
      },
      error: () => {
        if (this.isStaleRequest(requestId, effectiveRequestKey)) {
          onComplete?.();
          return;
        }

        this.clearFeed();
        this.loadedSource = 'destination';
        this.loadedDestinationId = destinationId;
        this.loadedCurationContextKey = curationContextKey;
        this.loadedRequestKey = effectiveRequestKey;
        this.loadingRequestKey = null;
        this._loadError.set("We couldn't load that destination. Check your connection and try again.");
        this._loading.set(false);
        this._loaded.set(true);
        onComplete?.();
      },
    });
  }

  private applyResponse(res: ExploreApiResponse): void {
    this._sections.set(this.normalizeSections(res));
    this._selectedDestination.set(res.selectedDestination ?? null);

    if (res.selectedDestination) {
      this._nearbyStatus.set({
        label: 'Near',
        value: res.selectedDestination.name,
      });
      this.destinationSvc.set({
        id: res.selectedDestination.id,
        name: res.selectedDestination.name,
        latitude: res.selectedDestination.latitude,
        longitude: res.selectedDestination.longitude,
      });
    }
  }

  private normalizeSections(res: ExploreApiResponse): ExploreSection[] {
    if (res.sections?.length) {
      return res.sections
        .map(section => this.mapApiSection(section))
        .filter(section => section.items.length > 0)
        .sort((a, b) => a.priority - b.priority || a.title.localeCompare(b.title));
    }

    return this.mapLegacyResponseToSections(res);
  }

  private mapApiSection(section: ApiExploreSection): ExploreSection {
    return {
      tagTypeId: section.tagTypeId,
      key: section.key,
      title: section.title,
      subtitle: section.subtitle,
      layout: section.layout ?? 'horizontal-cards',
      cardType: section.cardType ?? 'place-card',
      moreRoute: section.moreRoute,
      priority: section.priority ?? 100,
      items: (section.items ?? []).map(item => this.mapApiItem(item, section.cardType)),
    };
  }

  private mapApiItem(item: ApiExploreSectionItem, sectionCardType?: string): ExploreSectionItem {
    const fallbackEntityType = sectionCardType === 'event-card'
      ? 'event'
      : sectionCardType === 'stay-card'
        ? 'stay'
        : 'place';
    const entityType = item.entityType ?? fallbackEntityType;

    return {
      id: String(item.id),
      entityType,
      detailRoute: item.detailRoute || this.defaultDetailRoute(entityType, item.id),
      title: item.title,
      subtitle: item.subtitle,
      description: item.description,
      categoryLabel: item.categoryLabel,
      locationName: item.locationName ?? undefined,
      image: item.image ?? '',
      rating: item.rating,
      reviewCount: item.reviewCount,
      distance: item.distance,
      dateLabel: item.dateLabel,
      openingHours: item.openingHours,
      statusBadge: item.statusBadge?.label
        ? {
            label: item.statusBadge.label,
            tone: item.statusBadge.tone ?? 'neutral',
          }
        : undefined,
      pricePerNight: item.pricePerNight,
      isSaved: item.isSaved ?? false,
      tags: (item.tags ?? []).map(tag => ({
        tagId: String(tag.tagId),
        name: tag.name,
        slug: tag.slug,
      })),
    };
  }

  private mapLegacyResponseToSections(res: ExploreApiResponse): ExploreSection[] {
    const sections: ExploreSection[] = [];

    if (res.curatedForYou?.length) {
      sections.push({
        key: 'curated-for-you',
        title: 'Curated for You',
        subtitle: 'Based on your interests and recent activity nearby',
        layout: 'horizontal-cards',
        cardType: 'place-card',
        moreRoute: '/recommendations',
        priority: 10,
        items: res.curatedForYou.map(item => this.mapApiItem({
          id: item.id,
          entityType: 'place',
          detailRoute: `/places/${item.id}`,
          title: item.title,
          subtitle: item.subtitle,
          categoryLabel: item.categoryLabel,
          image: item.image,
          isSaved: item.isSaved,
        })),
      });
    }

    if (res.topStays?.length) {
      sections.push({
        key: 'top-stays',
        title: 'Top Stays',
        subtitle: 'Highly rated accommodation nearby',
        layout: 'horizontal-cards',
        cardType: 'stay-card',
        moreRoute: '/hotels',
        priority: 20,
        items: res.topStays.map(item => this.mapApiItem({
          id: item.id,
          entityType: 'stay',
          detailRoute: `/stays/${item.id}`,
          title: item.title,
          image: item.image,
          rating: item.rating,
          reviewCount: item.reviewCount,
          distance: item.distance,
          pricePerNight: item.pricePerNight,
          isSaved: item.isSaved,
        }, 'stay-card')),
      });
    }

    if (res.eventsThisWeek?.length) {
      sections.push({
        key: 'events',
        title: 'Events',
        subtitle: "What's happening around town",
        layout: 'horizontal-cards',
        cardType: 'event-card',
        moreRoute: '/events',
        priority: 30,
        items: res.eventsThisWeek.map(item => this.mapApiItem({
          id: item.id,
          entityType: 'event',
          detailRoute: `/events/${item.id}`,
          title: item.title,
          subtitle: item.subtitle,
          dateLabel: item.dateLabel,
          image: item.image,
        }, 'event-card')),
      });
    }

    for (const section of res.dynamicSections ?? []) {
      sections.push(this.mapApiSection({
        ...section,
        priority: section.priority ?? 100 + sections.length,
      }));
    }

    return sections
      .filter(section => section.items.length > 0)
      .sort((a, b) => a.priority - b.priority || a.title.localeCompare(b.title));
  }

  private resolveSavedState(item: ExploreSectionItem): ExploreSectionItem {
    if (item.entityType !== 'place' && item.entityType !== 'stay') {
      return item;
    }

    const favoriteType: FavoriteEntityType = 'place';
    const isSaved = this.favoriteState.resolve(favoriteType, item.id, item.isSaved);
    return item.isSaved === isSaved ? item : { ...item, isSaved };
  }

  private itemsForSection(key: string): ExploreSectionItem[] {
    return this.sections().find(section => section.key === key)?.items ?? [];
  }

  private removeUserSpecificSections(): void {
    this._sections.update(sections => sections.filter(section => section.key !== 'curated-for-you'));
  }

  private clearFeed(): void {
    this._sections.set([]);
    this._selectedDestination.set(null);
  }

  private defaultDetailRoute(entityType: string, id: number): string {
    if (entityType === 'event') return `/events/${id}`;
    if (entityType === 'stay') return `/stays/${id}`;
    if (entityType === 'destination') return `/explore?destinationId=${id}`;
    return `/places/${id}`;
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

  private buildLocationRequestKey(
    latitude: number,
    longitude: number,
    source: 'gps' | 'approximate' | 'manual' | 'default' | 'unknown' | null | undefined,
    curationContextKey: string,
  ): string {
    return ['location', latitude.toFixed(4), longitude.toFixed(4), source ?? 'unknown', curationContextKey].join(':');
  }

  private buildDestinationRequestKey(destinationId: number, curationContextKey: string): string {
    return ['destination', destinationId, curationContextKey].join(':');
  }

  private isStaleRequest(requestId: number, requestKey: string): boolean {
    return requestId !== this.requestVersion || this.loadingRequestKey !== requestKey;
  }
}
