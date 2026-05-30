import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { FavoriteEntityType, FavoriteStateService } from '../../../core/services/favorite-state.service';
import { SaveToCollectionService, type SaveToCollectionTarget } from '../../../core/services/save-to-collection.service';
import { LocationService } from '../../../core/services/location.service';
import { DestinationService } from '../../../core/services/destination.service';
import { CollectionsService } from '../../profile/services/collections';
import type { ExploreSectionItem } from '../models/explore-extras.model';
import type { ExploreSectionFilterGroup, ExploreSectionListingResponse } from '../models/explore-section-listing.model';

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
  filters?: ApiExploreSectionFilterGroup[];
}

interface ApiExploreSectionFilterGroup {
  tagTypeId: number;
  name: string;
  slug: string;
  label: string;
  order: number;
  options: ApiExploreSectionFilterOption[];
}

interface ApiExploreSectionFilterOption {
  tagId: number;
  name: string;
  slug: string;
  count: number;
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

@Injectable({ providedIn: 'root' })
export class ExploreSectionListingService {
  private readonly http = inject(HttpClient);
  private readonly favoriteState = inject(FavoriteStateService);
  private readonly saveToCollection = inject(SaveToCollectionService);
  private readonly collectionsService = inject(CollectionsService);
  private readonly locationSvc = inject(LocationService);
  private readonly destinationSvc = inject(DestinationService);

  private readonly _page = signal<ExploreSectionListingResponse | null>(null);
  private readonly _items = signal<ExploreSectionItem[]>([]);
  private readonly _loading = signal(false);
  private readonly _loadingMore = signal(false);
  private readonly _loadError = signal<string | null>(null);
  private readonly _loadMoreError = signal<string | null>(null);
  private readonly _hasMore = signal(false);
  private readonly _nextSkip = signal(0);
  private readonly _filters = signal<ExploreSectionFilterGroup[]>([]);
  private readonly _selectedTagIds = signal<number[]>([]);

  private currentSectionKey: string | null = null;
  private currentDestinationId: number | null = null;
  private currentLocationKey: string | null = null;
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
  readonly filters = computed(() => this._filters());
  readonly selectedTagIds = computed(() => this._selectedTagIds());
  readonly hasActiveFilters = computed(() => this._selectedTagIds().length > 0);

  readonly items = computed(() => this._items().map(item => this.resolveSavedState(item)));

  invalidate(): void {
    this.currentSectionKey = null;
    this.currentDestinationId = null;
    this.currentLocationKey = null;
    this._page.set(null);
    this._items.set([]);
  }

  load(sectionKey: string, destinationId: number): void {
    const normalizedKey = sectionKey.trim();
    if (!normalizedKey || !Number.isFinite(destinationId) || destinationId <= 0) {
      this._loadError.set('Invalid explore section request.');
      return;
    }

    const locationKey = this.buildLocationKey();
    if (
      this.currentSectionKey === normalizedKey &&
      this.currentDestinationId === destinationId &&
      this.currentLocationKey === locationKey &&
      this._page()
    ) {
      return;
    }

    this.currentSectionKey = normalizedKey;
    this.currentDestinationId = destinationId;
    this.currentLocationKey = locationKey;
    this._page.set(null);
    this._items.set([]);
    this._filters.set([]);
    this._selectedTagIds.set([]);
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

  toggleTagFilter(tagId: number): void {
    if (!Number.isFinite(tagId) || tagId <= 0) return;
    if (!this.currentSectionKey || !this.currentDestinationId) return;

    const current = this._selectedTagIds();
    const next = current.includes(tagId)
      ? current.filter(id => id !== tagId)
      : [...current, tagId];

    this._selectedTagIds.set(next);
    this._items.set([]);
    this._hasMore.set(false);
    this._nextSkip.set(0);
    this.loadPage(this.currentSectionKey, this.currentDestinationId, 0, FIRST_PAGE_SIZE, false);
  }

  clearFilters(): void {
    if (!this.currentSectionKey || !this.currentDestinationId) return;
    if (this._selectedTagIds().length === 0) return;

    this._selectedTagIds.set([]);
    this._items.set([]);
    this._hasMore.set(false);
    this._nextSkip.set(0);
    this.loadPage(this.currentSectionKey, this.currentDestinationId, 0, FIRST_PAGE_SIZE, false);
  }

  isTagSelected(tagId: number): boolean {
    return this._selectedTagIds().includes(tagId);
  }

  toggleSaved(item: ExploreSectionItem, collectionId?: number | null): void {
    const target = this.toSaveTarget(item);
    if (!target) return;

    if (collectionId != null && collectionId > 0) {
      this.collectionsService.addItem(collectionId, {
        ...target,
        image: target.imageUrl,
      }).subscribe({ error: () => {} });
      return;
    }

    this.saveToCollection.open(target);
  }

  private toSaveTarget(item: ExploreSectionItem): SaveToCollectionTarget | null {
    const numericId = parseInt(item.id, 10);
    if (isNaN(numericId)) return null;

    if (item.entityType === 'event') {
      return {
        entityType: 'event',
        entityId: numericId,
        title: item.title,
        imageUrl: item.image,
        surface: `explore_section_${this.currentSectionKey ?? 'unknown'}`,
        analyticsEntityType: 'event',
      };
    }

    const saveEntityType = item.entityType === 'stay' ? 'place' : item.entityType;
    if (saveEntityType !== 'place') return null;

    return {
      entityType: 'place',
      entityId: numericId,
      title: item.title,
      imageUrl: item.image,
      surface: `explore_section_${this.currentSectionKey ?? 'unknown'}`,
      analyticsEntityType: item.entityType === 'stay' ? 'hotel' : 'place',
    };
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

    let params = new HttpParams()
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

    for (const tagId of this._selectedTagIds()) {
      params = params.append('tagIds', tagId);
    }

    this.http.get<ApiExploreSectionListingResponse>(`/api/explore/${encodeURIComponent(sectionKey)}`, {
      params,
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
          filters: this.mapApiFilters(res.filters ?? []),
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
        this._filters.set(mappedPage.filters);
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
          this._filters.set([]);
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

  private mapApiFilters(filters: ApiExploreSectionFilterGroup[]): ExploreSectionFilterGroup[] {
    return filters.map(group => ({
      tagTypeId: group.tagTypeId,
      name: group.name,
      slug: group.slug,
      label: group.label || group.name,
      order: group.order,
      options: (group.options ?? []).map(option => ({
        tagId: option.tagId,
        name: option.name,
        slug: option.slug,
        count: option.count,
      })),
    }));
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
      locationName: dto.locationName ?? undefined,
      image: dto.image ?? '',
      rating: dto.rating,
      reviewCount: dto.reviewCount,
      distance: dto.distance,
      dateLabel: dto.dateLabel,
      openingHours: dto.openingHours,
      statusBadge: dto.statusBadge?.label
        ? {
            label: dto.statusBadge.label,
            tone: dto.statusBadge.tone ?? 'neutral',
          }
        : undefined,
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

  private buildLocationKey(): string {
    if (this.destinationSvc.mode() === 'location') {
      const loc = this.locationSvc.location();
      if (loc) {
        return `gps:${loc.latitude.toFixed(4)}:${loc.longitude.toFixed(4)}`;
      }
      return 'location-pending';
    }
    return 'dest-centre';
  }
}
