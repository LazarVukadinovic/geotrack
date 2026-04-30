import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { delay, Observable, of } from 'rxjs';
import { DestinationService } from '../../../core/services/destination.service';
import { LocationService } from '../../../core/services/location.service';
import { ExploreService } from '../../explore/services/explore.service';
import type { LocalPlacesCategory, CategoryMeta } from '../models/local-places.model';

const FIRST_PAGE_SIZE = 8;
const NEXT_PAGE_SIZE = 8;

const CATEGORY_META: Record<string, CategoryMeta> = {
  beaches: { title: 'Beaches', subtitle: 'Explore beach spots and coastal places' },
  museums: { title: 'Museums', subtitle: 'Discover museums and exhibitions' },
  parks: { title: 'Parks', subtitle: 'Browse parks and green spaces' },
  markets: { title: 'Markets', subtitle: 'Local markets and fresh finds' },
  restaurants: { title: 'Restaurants', subtitle: 'Places to eat nearby' },
  cafes: { title: 'Cafes', subtitle: 'Coffee and casual stops nearby' },
  'historic-sites': { title: 'Historic Sites', subtitle: 'History-backed places in this destination' },
  galleries: { title: 'Galleries', subtitle: 'Art and exhibition spaces' },
  'nature-reserves': { title: 'Nature Reserves', subtitle: 'Protected nature and outdoor places' },
  viewpoints: { title: 'Viewpoints', subtitle: 'Scenic places from the catalog' },
};

interface ApiCategory {
  id: number;
  title: string;
  image?: string;
  categoryKey: string;
  subtitle?: string;
}

interface ApiCategoriesResponse {
  items: ApiCategory[];
  skip: number;
  take: number;
  nextSkip: number;
  hasMore: boolean;
}

@Injectable({ providedIn: 'root' })
export class LocalPlacesService {
  private readonly http = inject(HttpClient);
  private readonly destinationSvc = inject(DestinationService);
  private readonly locationSvc = inject(LocationService);
  private readonly exploreSvc = inject(ExploreService);
  private readonly _categories = signal<LocalPlacesCategory[]>([]);
  private readonly _loading = signal(false);
  private readonly _loadingMore = signal(false);
  private readonly _loadError = signal<string | null>(null);
  private readonly _loadMoreError = signal<string | null>(null);
  private readonly _hasMore = signal(false);
  private readonly _nextSkip = signal(0);
  private loadedDestinationId: number | null = null;
  private requestLatitude: number | null = null;
  private requestLongitude: number | null = null;
  private requestVersion = 0;

  readonly categories = this._categories.asReadonly();
  readonly loadError = computed(() => this._loadError());
  readonly loadMoreError = computed(() => this._loadMoreError());
  readonly loading = computed(() => this._loading());
  readonly loadingMore = computed(() => this._loadingMore());
  readonly hasMore = computed(() => this._hasMore());

  constructor() {
    void this.exploreSvc;
    effect(() => {
      const destId = this.destinationSvc.id();
      if (destId == null || destId === this.loadedDestinationId) return;
      this.resetForDestination(destId);
      this.loadPage(destId, 0, FIRST_PAGE_SIZE, false);
    });
  }

  loadMore(): void {
    const destinationId = this.loadedDestinationId;
    if (destinationId == null || this._loading() || this._loadingMore() || !this._hasMore()) return;
    this.loadPage(destinationId, this._nextSkip(), NEXT_PAGE_SIZE, true);
  }

  private resetForDestination(destinationId: number): void {
    this.loadedDestinationId = destinationId;
    this._categories.set([]);
    this._hasMore.set(false);
    this._nextSkip.set(0);
    this._loadError.set(null);
    this._loadMoreError.set(null);
  }

  private loadPage(destinationId: number, skip: number, take: number, append: boolean): void {
    const requestId = ++this.requestVersion;

    if (append) {
      this._loadingMore.set(true);
      this._loadMoreError.set(null);
    } else {
      this.captureLocationContext();
      this._loading.set(true);
      this._loadError.set(null);
      this._loadMoreError.set(null);
    }

    this.http.get<ApiCategoriesResponse>('/api/explore/categories', {
      params: this.buildParams(destinationId, skip, take),
    }).subscribe({
      next: (res) => {
        if (requestId !== this.requestVersion) return;
        const mapped = res.items.map(c => ({
          id: String(c.id),
          title: c.title,
          image: c.image ?? '',
          categoryKey: c.categoryKey,
          subtitle: c.subtitle ?? '',
        }));
        this._categories.set(append ? this.mergeCategories(this._categories(), mapped) : mapped);
        this.loadedDestinationId = destinationId;
        this._hasMore.set(res.hasMore);
        this._nextSkip.set(res.nextSkip);
        this._loadError.set(null);
        this._loadMoreError.set(null);
        this.finishLoading(append);
      },
      error: () => {
        if (requestId !== this.requestVersion) return;
        if (append) {
          this._loadMoreError.set("We couldn't load more local categories. Try again.");
        } else {
          this._categories.set([]);
          this._hasMore.set(false);
          this._nextSkip.set(0);
          this._loadError.set("We couldn't load local places. Check your connection and try again.");
        }
        this.finishLoading(append);
      },
    });
  }

  private buildParams(destinationId: number, skip: number, take: number): HttpParams {
    let params = new HttpParams()
      .set('destinationId', destinationId)
      .set('skip', skip)
      .set('take', take);

    if (this.requestLatitude != null && this.requestLongitude != null) {
      params = params
        .set('latitude', this.requestLatitude)
        .set('longitude', this.requestLongitude);
    }

    return params;
  }

  private captureLocationContext(): void {
    const loc = this.locationSvc.location();
    this.requestLatitude = loc?.latitude ?? null;
    this.requestLongitude = loc?.longitude ?? null;
  }

  private mergeCategories(current: LocalPlacesCategory[], next: LocalPlacesCategory[]): LocalPlacesCategory[] {
    const existingKeys = new Set(current.map(item => item.categoryKey));
    return [...current, ...next.filter(item => !existingKeys.has(item.categoryKey))];
  }

  private finishLoading(append: boolean): void {
    if (append) {
      this._loadingMore.set(false);
    } else {
      this._loading.set(false);
    }
  }

  getCategories$(): Observable<LocalPlacesCategory[]> {
    return of(this._categories()).pipe(delay(0));
  }

  getCategoryMeta(key: string): CategoryMeta {
    const found = this._categories().find(c => c.categoryKey === key);
    if (found) return { title: found.title, subtitle: found.subtitle };
    return CATEGORY_META[key] ?? { title: key, subtitle: '' };
  }
}
