import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { AuthService } from '../../../core/services/auth.service';
import { AnalyticsService } from '../../../core/services/analytics.service';
import { FavoriteStateService } from '../../../core/services/favorite-state.service';
import { LocationService } from '../../../core/services/location.service';
import type {
  RecommendationFilter,
  RecommendationItem,
  RecommendationsApiItem,
  RecommendationsApiResponse,
  RecommendationsDestination,
} from '../models/recommendations.model';

const FIRST_PAGE_SIZE = 6;
const NEXT_PAGE_SIZE = 6;

@Injectable({ providedIn: 'root' })
export class RecommendationsService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly analytics = inject(AnalyticsService);
  private readonly favoriteState = inject(FavoriteStateService);
  private readonly locationSvc = inject(LocationService);

  private readonly _items = signal<RecommendationItem[]>([]);
  private readonly _filters = signal<RecommendationFilter[]>([]);
  private readonly _destination = signal<RecommendationsDestination | null>(null);
  private readonly _activeTagId = signal<number | null>(null);
  private readonly _loading = signal(false);
  private readonly _loadingMore = signal(false);
  private readonly _loadError = signal<string | null>(null);
  private readonly _loadMoreError = signal<string | null>(null);
  private readonly _hasSignals = signal(true);
  private readonly _emptyReason = signal<string | null>(null);
  private readonly _hasMore = signal(false);
  private readonly _nextSkip = signal(0);
  private readonly _loadedKey = signal<string | null>(null);
  private requestVersion = 0;

  readonly items = computed(() =>
    this._items().map((item) => {
      const isSaved = this.favoriteState.resolve('place', item.id, item.isSaved);
      return item.isSaved === isSaved ? item : { ...item, isSaved };
    }),
  );
  readonly filters = computed(() => this._filters());
  readonly destination = computed(() => this._destination());
  readonly activeTagId = computed(() => this._activeTagId());
  readonly loading = computed(() => this._loading());
  readonly loadingMore = computed(() => this._loadingMore());
  readonly loadError = computed(() => this._loadError());
  readonly loadMoreError = computed(() => this._loadMoreError());
  readonly hasSignals = computed(() => this._hasSignals());
  readonly emptyReason = computed(() => this._emptyReason());
  readonly hasMore = computed(() => this._hasMore());

  loadInitial(force = false): void {
    if (this._loading()) return;
    void this.fetchPage(0, FIRST_PAGE_SIZE, false, force);
  }

  loadMore(): void {
    if (this._loadingMore() || this._loading() || !this._hasMore()) return;
    void this.fetchPage(this._nextSkip(), NEXT_PAGE_SIZE, true, true);
  }

  setFilter(tagId: number | null): void {
    if (this._activeTagId() === tagId) return;

    this._activeTagId.set(tagId);
    this._items.set([]);
    this._hasMore.set(false);
    this._nextSkip.set(0);
    this._loadedKey.set(null);
    this._loadError.set(null);
    this._loadMoreError.set(null);

    const filterLabel = tagId == null
      ? 'All'
      : this._filters().find((filter) => filter.tagId === tagId)?.label ?? String(tagId);

    this.analytics.track('filter_applied', {
      entityType: 'place',
      metadata: { surface: 'recommendations', filter: filterLabel, tagId },
    });

    this.loadInitial(true);
  }

  refresh(): void {
    this._loadedKey.set(null);
    this.loadInitial(true);
  }

  toggleSaved(id: string): void {
    if (!this.auth.isLoggedIn()) return;

    const numId = parseInt(id, 10);
    if (isNaN(numId)) return;

    const current = this.items().find((item) => item.id === id)?.isSaved ?? false;
    const next = !current;
    this.favoriteState.set('place', id, next);
    this.analytics.track(next ? 'favorite_added' : 'favorite_removed', {
      entityType: 'place',
      entityId: numId,
      metadata: { surface: 'recommendations' },
    });

    this.http.post('/api/favorites/toggle', { entityType: 'place', entityId: numId }).subscribe({
      error: () => this.favoriteState.set('place', id, current),
    });
  }

  private async fetchPage(skip: number, take: number, append: boolean, force: boolean): Promise<void> {
    if (!this.auth.isLoggedIn()) {
      this._items.set([]);
      this._hasSignals.set(false);
      this._emptyReason.set('Sign in to see recommendations based on your interests.');
      return;
    }

    if (append) {
      this._loadingMore.set(true);
      this._loadMoreError.set(null);
    } else {
      this._loading.set(true);
      this._loadError.set(null);
      this._loadMoreError.set(null);
    }

    const loc = this.locationSvc.location() ?? await this.locationSvc.ensureIpFallback();
    if (!loc) {
      this.finishLoading(append);
      this.setLoadError(append, 'We could not resolve your location for recommendations.');
      return;
    }

    const key = this.buildRequestKey(loc.latitude, loc.longitude);
    if (!append && !force && this._loadedKey() === key) {
      this.finishLoading(append);
      return;
    }

    const requestId = ++this.requestVersion;
    const params = this.buildParams(loc.latitude, loc.longitude, skip, take);

    this.http.get<RecommendationsApiResponse>('/api/explore/recommendations', { params }).subscribe({
      next: (res) => {
        if (requestId !== this.requestVersion) return;

        const mapped = res.items.map((item) => this.mapItem(item));
        this._items.set(append ? [...this._items(), ...mapped] : mapped);
        this._filters.set(res.filters ?? []);
        this._destination.set(res.selectedDestination ?? null);
        this._hasSignals.set(res.hasSignals);
        this._emptyReason.set(res.emptyReason ?? null);
        this._hasMore.set(res.hasMore);
        this._nextSkip.set(res.nextSkip);
        this._loadedKey.set(key);
        this._loadError.set(null);
        this._loadMoreError.set(null);
        this.finishLoading(append);
      },
      error: () => {
        if (requestId !== this.requestVersion) return;
        this.setLoadError(append, append
          ? "We couldn't load more recommendations. Try again."
          : "We couldn't load your recommendations. Check your connection and try again.");
        this.finishLoading(append);
      },
    });
  }

  private buildParams(latitude: number, longitude: number, skip: number, take: number): HttpParams {
    let params = new HttpParams()
      .set('latitude', latitude)
      .set('longitude', longitude)
      .set('skip', skip)
      .set('take', take);

    const tagId = this._activeTagId();
    if (tagId != null) {
      params = params.set('tagId', tagId);
    }

    return params;
  }

  private mapItem(item: RecommendationsApiItem): RecommendationItem {
    return {
      id: String(item.id),
      title: item.title,
      reason: item.reason ?? '',
      description: item.description ?? '',
      location: item.location ?? '',
      image: item.image ?? '',
      rating: item.rating,
      reviewCount: item.reviewCount,
      distance: item.distance,
      openingHours: item.openingHours,
      categoryLabel: item.categoryLabel ?? '',
      matchedTagId: item.matchedTagId,
      isSaved: item.isSaved,
    };
  }

  private buildRequestKey(latitude: number, longitude: number): string {
    return [
      latitude.toFixed(4),
      longitude.toFixed(4),
      this._activeTagId() ?? 'all',
    ].join(':');
  }

  private setLoadError(append: boolean, message: string): void {
    if (append) {
      this._loadMoreError.set(message);
    } else {
      this._items.set([]);
      this._loadError.set(message);
    }
  }

  private finishLoading(append: boolean): void {
    if (append) {
      this._loadingMore.set(false);
    } else {
      this._loading.set(false);
    }
  }
}
