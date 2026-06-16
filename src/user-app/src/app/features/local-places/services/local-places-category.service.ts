import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { delay, Observable, of } from 'rxjs';
import { DestinationService } from '../../../core/services/destination.service';
import { FavoriteStateService } from '../../../core/services/favorite-state.service';
import { LocationService } from '../../../core/services/location.service';
import { SaveToCollectionService } from '../../../core/services/save-to-collection.service';
import { ExploreService } from '../../explore/services/explore.service';
import type { PlaceCategoryItem } from '../models/local-places.model';

const FIRST_PAGE_SIZE = 6;
const NEXT_PAGE_SIZE = 6;

interface ApiPlaceListItem {
  id: number;
  title: string;
  subtitle?: string;
  description?: string;
  location?: string;
  image?: string;
  rating?: number;
  reviewCount?: string;
  openingHours?: string;
  distance?: string;
  tags: string[];
  variant: string;
  isSaved: boolean;
}

interface ApiPlaceListingResponse {
  items: ApiPlaceListItem[];
  skip: number;
  take: number;
  nextSkip: number;
  hasMore: boolean;
}

@Injectable({ providedIn: 'root' })
export class LocalPlacesCategoryService {
  private readonly http = inject(HttpClient);
  private readonly destinationSvc = inject(DestinationService);
  private readonly favoriteState = inject(FavoriteStateService);
  private readonly locationSvc = inject(LocationService);
  private readonly saveToCollection = inject(SaveToCollectionService);
  private readonly exploreSvc = inject(ExploreService);
  private readonly _apiItems = signal<Record<string, PlaceCategoryItem[]>>({});
  private readonly _loadErrorByKey = signal<Record<string, string>>({});
  private readonly _loadMoreErrorByKey = signal<Record<string, string>>({});
  private readonly _hasMoreByKey = signal<Record<string, boolean>>({});
  private readonly _nextSkipByKey = signal<Record<string, number>>({});
  private readonly _loadingByKey = signal<Record<string, boolean>>({});
  private readonly _loadingMoreByKey = signal<Record<string, boolean>>({});
  private readonly _loadedKeys = new Set<string>();
  private readonly _pendingKeys = signal<string[]>([]);
  private loadedDestinationId: number | null = null;
  private loadedLocationKey: string | null = null;

  constructor() {
    void this.exploreSvc;
    effect(() => {
      const destId = this.destinationSvc.id();
      const mode = this.destinationSvc.mode();
      const loc = this.locationSvc.location();
      if (destId == null) return;
      const locationKey = this.buildLocationKey(destId, mode, loc);
      const destChanged = destId !== this.loadedDestinationId;
      // Skip if GPS is still resolving AND we already have data for this destination
      const isPending = locationKey.endsWith(':pending');
      const alreadyLoadedForDest = this.loadedLocationKey?.startsWith(`${destId}:`) ?? false;
      const locationChanged = !isPending && locationKey !== this.loadedLocationKey;
      if (destChanged || locationChanged) {
        untracked(() => this.resetForDestination(destId));
        this.loadedLocationKey = isPending && alreadyLoadedForDest ? this.loadedLocationKey : locationKey;
      }
      const pending = this._pendingKeys();
      if (pending.length === 0) return;
      untracked(() => {
        for (const key of pending) {
          if (this._loadedKeys.has(key)) continue;
          this._loadedKeys.add(key);
          this.fetchCategory(key, destId, 0, FIRST_PAGE_SIZE, false);
        }
        this._pendingKeys.set([]);
      });
    });
  }

  loadCategory(categoryKey: string): void {
    if (this._loadedKeys.has(categoryKey)) return;
    const queued = this._pendingKeys();
    if (queued.includes(categoryKey)) return;
    this._pendingKeys.set([...queued, categoryKey]);
  }

  private resetForDestination(destinationId: number): void {
    this.loadedDestinationId = destinationId;
    this._apiItems.set({});
    this._loadErrorByKey.set({});
    this._loadMoreErrorByKey.set({});
    this._hasMoreByKey.set({});
    this._nextSkipByKey.set({});
    this._loadingByKey.set({});
    this._loadingMoreByKey.set({});
    this._loadedKeys.clear();
  }

  loadMore(categoryKey: string): void {
    const destinationId = this.destinationSvc.id();
    if (
      destinationId == null ||
      this._loadingByKey()[categoryKey] ||
      this._loadingMoreByKey()[categoryKey] ||
      !this._hasMoreByKey()[categoryKey]
    ) {
      return;
    }

    this.fetchCategory(categoryKey, destinationId, this._nextSkipByKey()[categoryKey] ?? 0, NEXT_PAGE_SIZE, true);
  }

  private fetchCategory(categoryKey: string, destinationId: number, skip: number, take: number, append: boolean): void {
    this.setLoading(categoryKey, append, true);
    this.clearLoadMoreError(categoryKey);
    if (!append) {
      this.clearLoadError(categoryKey);
    }

    this.http.get<ApiPlaceListingResponse>('/api/places', {
      params: this.buildParams(categoryKey, destinationId, skip, take),
    }).subscribe({
      next: (res) => {
        const mapped = this.mapItems(res.items, append ? (this._apiItems()[categoryKey]?.length ?? 0) : 0);
        this._apiItems.update(m => ({
          ...m,
          [categoryKey]: append
            ? this.mergeItems(m[categoryKey] ?? [], mapped)
            : mapped,
        }));
        this._hasMoreByKey.update(m => ({ ...m, [categoryKey]: res.hasMore }));
        this._nextSkipByKey.update(m => ({ ...m, [categoryKey]: res.nextSkip }));
        this._loadErrorByKey.update(m => {
          const copy = { ...m };
          delete copy[categoryKey];
          return copy;
        });
        this.clearLoadMoreError(categoryKey);
        this.setLoading(categoryKey, append, false);
      },
      error: () => {
        if (append) {
          this._loadMoreErrorByKey.update(m => ({
            ...m,
            [categoryKey]: "We couldn't load more places. Try again.",
          }));
        } else {
          this._apiItems.update(m => ({ ...m, [categoryKey]: [] }));
          this._hasMoreByKey.update(m => ({ ...m, [categoryKey]: false }));
          this._nextSkipByKey.update(m => ({ ...m, [categoryKey]: 0 }));
          this._loadErrorByKey.update(m => ({
            ...m,
            [categoryKey]: "We couldn't load these places. Check your connection and try again.",
          }));
        }
        this.setLoading(categoryKey, append, false);
      },
    });
  }

  private buildParams(categoryKey: string, destinationId: number, skip: number, take: number): HttpParams {
    let params = new HttpParams()
      .set('category', this.normalizeCategoryKey(categoryKey))
      .set('destinationId', destinationId)
      .set('skip', skip)
      .set('take', take);

    if (this.destinationSvc.mode() === 'location') {
      const loc = this.locationSvc.location();
      if (loc) {
        params = params
          .set('latitude', loc.latitude)
          .set('longitude', loc.longitude)
          .set('distanceOrigin', 'current-location');
      } else {
        params = params.set('distanceOrigin', 'destination-centre');
      }
    } else {
      params = params.set('distanceOrigin', 'destination-centre');
    }

    return params;
  }

  private normalizeCategoryKey(categoryKey: string): string {
    const normalized = categoryKey.trim().toLowerCase();
    return normalized === 'nature-reserves' || normalized === 'nature-reserve' ? 'nature' : normalized;
  }

  private buildLocationKey(destId: number, mode: string, loc: { latitude: number; longitude: number } | null): string {
    if (mode === 'location') {
      if (loc) return `${destId}:gps:${loc.latitude.toFixed(4)}:${loc.longitude.toFixed(4)}`;
      return `${destId}:pending`;
    }
    return `${destId}:dest`;
  }

  private mapItems(items: ApiPlaceListItem[], startIndex: number): PlaceCategoryItem[] {
    return items.map((dto, i) => ({
      id: String(dto.id),
      title: dto.title,
      subtitle: dto.subtitle ?? '',
      description: dto.description ?? '',
      location: dto.location ?? '',
      image: dto.image ?? '',
      rating: dto.rating,
      reviewCount: dto.reviewCount,
      openingHours: dto.openingHours,
      variant: (startIndex + i === 0 ? 'large' : 'compact') as PlaceCategoryItem['variant'],
      isSaved: dto.isSaved,
    }));
  }

  private mergeItems(current: PlaceCategoryItem[], next: PlaceCategoryItem[]): PlaceCategoryItem[] {
    const existingIds = new Set(current.map(item => item.id));
    return [...current, ...next.filter(item => !existingIds.has(item.id))];
  }

  private setLoading(categoryKey: string, append: boolean, loading: boolean): void {
    const target = append ? this._loadingMoreByKey : this._loadingByKey;
    target.update(m => ({ ...m, [categoryKey]: loading }));
  }

  private clearLoadError(categoryKey: string): void {
    this._loadErrorByKey.update(m => {
      const copy = { ...m };
      delete copy[categoryKey];
      return copy;
    });
  }

  private clearLoadMoreError(categoryKey: string): void {
    this._loadMoreErrorByKey.update(m => {
      const copy = { ...m };
      delete copy[categoryKey];
      return copy;
    });
  }

  getItems(categoryKey: string): PlaceCategoryItem[] {
    const source = this._apiItems()[categoryKey] ?? [];
    return source.map((item) => ({
      ...item,
      isSaved: this.favoriteState.resolve('place', item.id, item.isSaved),
    }));
  }

  itemsFor(categoryKey: string) {
    return computed(() => {
      const source = this._apiItems()[categoryKey] ?? [];
      return source.map((item) => ({
        ...item,
        isSaved: this.favoriteState.resolve('place', item.id, item.isSaved),
      }));
    });
  }

  loadErrorFor(categoryKey: string) {
    return computed(() => this._loadErrorByKey()[categoryKey] ?? null);
  }

  loadMoreErrorFor(categoryKey: string) {
    return computed(() => this._loadMoreErrorByKey()[categoryKey] ?? null);
  }

  hasMoreFor(categoryKey: string) {
    return computed(() => this._hasMoreByKey()[categoryKey] ?? false);
  }

  loadingFor(categoryKey: string) {
    return computed(() => this._loadingByKey()[categoryKey] ?? false);
  }

  loadingMoreFor(categoryKey: string) {
    return computed(() => this._loadingMoreByKey()[categoryKey] ?? false);
  }

  getItems$(categoryKey: string): Observable<PlaceCategoryItem[]> {
    return of(this.getItems(categoryKey)).pipe(delay(0));
  }

  toggleSaved(id: string, categoryKey: string): void {
    const numId = parseInt(id, 10);
    if (isNaN(numId)) return;
    const item = this.getItems(categoryKey).find((entry) => entry.id === id);
    if (!item) return;
    this.saveToCollection.open({
      entityType: 'place',
      entityId: numId,
      title: item.title,
      imageUrl: item.image,
      surface: `local_places_category:${categoryKey}`,
      analyticsEntityType: 'place',
    });
  }
}
