import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { FavoriteEntityType, FavoriteStateService } from '../../../core/services/favorite-state.service';
import { SaveToCollectionService } from '../../../core/services/save-to-collection.service';
import type { ExploreSectionItem } from '../models/explore-extras.model';
import type { ExploreSectionListingResponse } from '../models/explore-section-listing.model';

const FIRST_PAGE_SIZE = 20;
const NEXT_PAGE_SIZE = 20;

interface ApiExploreSectionListingResponse {
  isKnownSection?: boolean;
  selectedDestination?: {
    id: number;
    name: string;
    description?: string;
    latitude: number;
    longitude: number;
  };
  key: string;
  title: string;
  subtitle?: string;
  cardType?: string;
  items: ApiExploreSectionItem[];
  skip: number;
  take: number;
  nextSkip: number;
  hasMore: boolean;
  totalCount: number;
  emptyReason?: string;
}

interface ApiExploreSectionItem {
  id: number;
  entityType?: string;
  detailRoute?: string;
  title: string;
  subtitle?: string;
  description?: string;
  categoryLabel?: string;
  image?: string;
  rating?: number;
  reviewCount?: string;
  distance?: string;
  dateLabel?: string;
  openingHours?: string;
  pricePerNight?: number;
  isSaved?: boolean;
  tags?: { tagId: number; name: string; slug: string }[];
}

@Injectable({ providedIn: 'root' })
export class ExploreSectionListingService {
  private readonly http = inject(HttpClient);
  private readonly favoriteState = inject(FavoriteStateService);
  private readonly saveToCollection = inject(SaveToCollectionService);

  private readonly _page = signal<ExploreSectionListingResponse | null>(null);
  private readonly _items = signal<ExploreSectionItem[]>([]);
  private readonly _loading = signal(false);
  private readonly _loadingMore = signal(false);
  private readonly _loadError = signal<string | null>(null);
  private readonly _loadMoreError = signal<string | null>(null);
  private readonly _hasMore = signal(false);
  private readonly _nextSkip = signal(0);

  private currentSectionKey: string | null = null;
  private currentDestinationId: number | null = null;
  private requestVersion = 0;

  readonly page = computed(() => this._page());
  readonly title = computed(() => this._page()?.title ?? 'Explore');
  readonly subtitle = computed(() => this._page()?.subtitle ?? '');
  readonly totalCount = computed(() => this._page()?.totalCount ?? this._items().length);
  readonly emptyReason = computed(() => this._page()?.emptyReason ?? 'No items found for this section.');
  readonly loading = computed(() => this._loading());
  readonly loadingMore = computed(() => this._loadingMore());
  readonly loadError = computed(() => this._loadError());
  readonly loadMoreError = computed(() => this._loadMoreError());
  readonly hasMore = computed(() => this._hasMore());

  readonly items = computed(() => this._items().map(item => this.resolveSavedState(item)));

  load(sectionKey: string, destinationId: number): void {
    const normalizedKey = sectionKey.trim();
    if (!normalizedKey || !Number.isFinite(destinationId) || destinationId <= 0) {
      this._loadError.set('Invalid explore section request.');
      return;
    }

    if (this.currentSectionKey === normalizedKey && this.currentDestinationId === destinationId && this._page()) {
      return;
    }

    this.currentSectionKey = normalizedKey;
    this.currentDestinationId = destinationId;
    this._page.set(null);
    this._items.set([]);
    this._hasMore.set(false);
    this._nextSkip.set(0);
    this.loadPage(normalizedKey, destinationId, 0, FIRST_PAGE_SIZE, false);
  }

  loadMore(): void {
    if (!this.currentSectionKey || !this.currentDestinationId) return;
    if (this._loading() || this._loadingMore() || !this._hasMore()) return;

    this.loadPage(
      this.currentSectionKey,
      this.currentDestinationId,
      this._nextSkip(),
      NEXT_PAGE_SIZE,
      true,
    );
  }

  toggleSaved(item: ExploreSectionItem): void {
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
      surface: `explore_section_${this.currentSectionKey ?? 'unknown'}`,
      analyticsEntityType: item.entityType === 'stay' ? 'hotel' : 'place',
    });
  }

  private loadPage(sectionKey: string, destinationId: number, skip: number, take: number, append: boolean): void {
    const requestId = ++this.requestVersion;

    if (append) {
      this._loadingMore.set(true);
      this._loadMoreError.set(null);
    } else {
      this._loading.set(true);
      this._loadError.set(null);
      this._loadMoreError.set(null);
    }

    this.http.get<ApiExploreSectionListingResponse>(`/api/explore/sections/${encodeURIComponent(sectionKey)}`, {
      params: new HttpParams()
        .set('destinationId', destinationId)
        .set('skip', skip)
        .set('take', take),
    }).subscribe({
      next: (res) => {
        if (requestId !== this.requestVersion) return;

        const mappedPage: ExploreSectionListingResponse = {
          isKnownSection: res.isKnownSection ?? true,
          selectedDestination: res.selectedDestination,
          key: res.key,
          title: res.title,
          subtitle: res.subtitle ?? '',
          cardType: res.cardType ?? 'place-card',
          items: [],
          skip: res.skip,
          take: res.take,
          nextSkip: res.nextSkip,
          hasMore: res.hasMore,
          totalCount: res.totalCount,
          emptyReason: res.emptyReason,
        };

        const mappedItems = (res.items ?? []).map(item => this.mapApiItem(item, mappedPage.cardType));
        const nextItems = append ? this.mergeItems(this._items(), mappedItems) : mappedItems;

        mappedPage.items = nextItems;
        this._page.set(mappedPage);
        this._items.set(nextItems);
        this._hasMore.set(res.hasMore);
        this._nextSkip.set(res.nextSkip);
        this._loadError.set(null);
        this._loadMoreError.set(null);
        this.finishLoading(append);
      },
      error: () => {
        if (requestId !== this.requestVersion) return;
        if (append) {
          this._loadMoreError.set('Could not load more items. Try again.');
        } else {
          this._page.set(null);
          this._items.set([]);
          this._hasMore.set(false);
          this._nextSkip.set(0);
          this._loadError.set('Could not load this explore section. Check your connection and try again.');
        }
        this.finishLoading(append);
      },
    });
  }

  private finishLoading(append: boolean): void {
    if (append) {
      this._loadingMore.set(false);
    } else {
      this._loading.set(false);
    }
  }

  private mapApiItem(dto: ApiExploreSectionItem, cardType: string): ExploreSectionItem {
    const entityType = dto.entityType ?? (cardType === 'stay-card' ? 'stay' : cardType === 'event-card' ? 'event' : 'place');

    return {
      id: String(dto.id),
      entityType,
      detailRoute: dto.detailRoute?.trim() || this.defaultDetailRoute(entityType, dto.id),
      title: dto.title,
      subtitle: dto.subtitle,
      description: dto.description,
      categoryLabel: dto.categoryLabel,
      image: dto.image ?? '',
      rating: dto.rating,
      reviewCount: dto.reviewCount,
      distance: dto.distance,
      dateLabel: dto.dateLabel,
      openingHours: dto.openingHours,
      pricePerNight: dto.pricePerNight,
      isSaved: dto.isSaved === true,
      tags: (dto.tags ?? []).map(tag => ({
        tagId: String(tag.tagId),
        name: tag.name,
        slug: tag.slug,
      })),
    };
  }

  private resolveSavedState(item: ExploreSectionItem): ExploreSectionItem {
    if (item.entityType !== 'place' && item.entityType !== 'stay' && item.entityType !== 'event') {
      return item;
    }

    const favoriteType: FavoriteEntityType = item.entityType === 'event' ? 'event' : 'place';
    const isSaved = this.favoriteState.resolve(favoriteType, item.id, item.isSaved);
    return item.isSaved === isSaved ? item : { ...item, isSaved };
  }

  private mergeItems(current: ExploreSectionItem[], next: ExploreSectionItem[]): ExploreSectionItem[] {
    const existingIds = new Set(current.map(item => `${item.entityType}:${item.id}`));
    return [...current, ...next.filter(item => !existingIds.has(`${item.entityType}:${item.id}`))];
  }

  private defaultDetailRoute(entityType: string, id: number): string {
    if (entityType === 'event') return `/events/${id}`;
    if (entityType === 'stay') return `/stays/${id}`;
    if (entityType === 'destination') return `/explore?destinationId=${id}`;
    return `/places/${id}`;
  }
}
