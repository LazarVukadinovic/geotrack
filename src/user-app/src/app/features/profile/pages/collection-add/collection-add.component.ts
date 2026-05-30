import { Component, OnDestroy, computed, effect, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { EXPLORE_IMAGE_FALLBACK } from '../../../../core/constants/explore-media.constants';
import { DestinationService } from '../../../../core/services/destination.service';
import { LocationService } from '../../../../core/services/location.service';
import {
  DestinationDiscoveryDestinationTypeFilter,
  DestinationDiscoverySearchItem,
  DestinationDiscoveryService,
  DestinationDiscoveryTagTypeFilter,
} from '../../../destinations/services/destination-discovery.service';
import { ExploreSectionComponent, type ExploreSectionSaveEvent } from '../../../explore/components/explore-section/explore-section.component';
import { ExploreService } from '../../../explore/services/explore.service';
import type { ExploreSection, ExploreSectionItem } from '../../../explore/models/explore-extras.model';
import { EmptyStateComponent } from '../../../../shared/components/empty-state/empty-state.component';
import { SearchBarComponent } from '../../../../shared/components/search-bar/search-bar.component';
import { CollectionsService } from '../../services/collections';

const SEARCH_DEBOUNCE_MS = 220;
const DESTINATION_PAGE_SIZE = 12;
const MIN_SEARCH_LENGTH = 2;

@Component({
  selector: 'app-collection-add',
  standalone: true,
  imports: [ExploreSectionComponent, EmptyStateComponent, SearchBarComponent],
  templateUrl: './collection-add.component.html',
  styleUrl: './collection-add.component.scss',
})
export class CollectionAddComponent implements OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly collectionsService = inject(CollectionsService);
  private readonly explore = inject(ExploreService);
  private readonly destinationSvc = inject(DestinationService);
  private readonly locationSvc = inject(LocationService);
  private readonly discovery = inject(DestinationDiscoveryService);

  private readonly collectionId = Number(this.route.snapshot.paramMap.get('id') ?? 0);

  protected readonly imageFallback = EXPLORE_IMAGE_FALLBACK;
  protected readonly collection = computed(() =>
    this.collectionsService.collections().find(collection => collection.id === this.collectionId) ?? null,
  );
  protected readonly sections = this.explore.sections;
  protected readonly browseSections = computed<ExploreSection[]>(() =>
    this.sections().map(section => ({
      ...section,
      moreRoute: `/explore/${section.key}`,
    })),
  );
  protected readonly loading = this.explore.loading;
  protected readonly loaded = this.explore.loaded;
  protected readonly loadError = this.explore.loadError;
  protected readonly selectedDestination = this.explore.selectedDestination;
  protected readonly destinationName = computed(() =>
    this.selectedDestination()?.name || this.destinationSvc.name() || 'Current area',
  );

  protected readonly destinationPickerOpen = signal(false);
  protected readonly destinationQuery = signal('');
  protected readonly destinationItems = signal<DestinationDiscoverySearchItem[]>([]);
  protected readonly destinationTotalCount = signal(0);
  protected readonly destinationLoading = signal(false);
  protected readonly destinationLoadingMore = signal(false);
  protected readonly destinationError = signal<string | null>(null);
  protected readonly destinationHasMore = signal(false);
  protected readonly destinationTypeFilters = signal<DestinationDiscoveryDestinationTypeFilter[]>([]);
  protected readonly tagTypeFilters = signal<DestinationDiscoveryTagTypeFilter[]>([]);
  protected readonly selectedDestinationTypeIds = signal<Set<number>>(new Set());
  protected readonly selectedTagIds = signal<Set<number>>(new Set());
  protected readonly filtersExpanded = signal(false);

  protected readonly visibleDestinationTypeFilters = computed(() =>
    this.destinationTypeFilters().filter(type => type.count > 0),
  );
  protected readonly visibleTagTypeFilters = computed(() =>
    this.tagTypeFilters().filter(group => group.options.length > 0),
  );
  protected readonly activeFilterCount = computed(() =>
    this.selectedDestinationTypeIds().size + this.selectedTagIds().size,
  );
  protected readonly hasDestinationFilters = computed(() =>
    this.destinationQuery().trim().length > 0 ||
    this.selectedDestinationTypeIds().size > 0 ||
    this.selectedTagIds().size > 0,
  );
  protected readonly destinationSummary = computed(() => {
    if (this.destinationLoading() && this.destinationItems().length === 0) return 'Searching...';
    const count = this.destinationTotalCount();
    if (count === 1) return '1 destination';
    return `${count} destinations`;
  });

  private nextDestinationSkip = 0;
  private destinationSearchTimer?: ReturnType<typeof setTimeout>;
  private destinationRequestVersion = 0;

  constructor() {
    this.collectionsService.ensureLoaded();
    if (this.collectionId > 0) {
      this.collectionsService.loadDetails(this.collectionId);
    }

    void this.locationSvc.ensureIpFallback();
    this.loadDestinationFilters();

    effect(() => {
      const destinationId = this.destinationSvc.id();
      if (this.destinationSvc.isDestinationMode() && destinationId != null) {
        void this.explore.loadForDestination(destinationId);
      }
    });
  }

  ngOnDestroy(): void {
    if (this.destinationSearchTimer) {
      clearTimeout(this.destinationSearchTimer);
    }
  }

  protected onSave(event: ExploreSectionSaveEvent): void {
    this.explore.toggleSaved(event.item, event.section.key, this.collectionId);
  }

  protected openItem(item: ExploreSectionItem): void {
    const route = item.detailRoute?.trim() || this.defaultDetailRoute(item);
    void this.router.navigateByUrl(this.withCollectionContext(route));
  }

  protected openMore(section: ExploreSection): void {
    if (!section.moreRoute) return;
    const destinationId = this.selectedDestination()?.id ?? this.destinationSvc.id();
    void this.router.navigateByUrl(this.withCollectionContext(
      section.moreRoute,
      destinationId != null && destinationId > 0 ? { destinationId } : undefined,
    ));
  }

  protected openDestinationPicker(): void {
    this.destinationPickerOpen.set(true);
    if (this.destinationItems().length === 0 && !this.destinationLoading()) {
      this.runDestinationSearch(0, false);
    }
  }

  protected closeDestinationPicker(): void {
    this.destinationPickerOpen.set(false);
  }

  protected done(): void {
    if (this.collectionId > 0) {
      this.collectionsService.loadDetails(this.collectionId);
      void this.router.navigate(['/profile/collections', this.collectionId]);
    }
  }

  protected goBack(): void {
    this.done();
  }

  protected useCurrentLocation(): void {
    void this.locationSvc.requestGps()
      .then(({ latitude, longitude }) => {
        this.destinationSvc.useLocationMode();
        return this.explore.loadForLocation(latitude, longitude);
      })
      .then(() => this.destinationPickerOpen.set(false))
      .catch(() => {});
  }

  protected onDestinationQueryChange(value: string): void {
    this.destinationQuery.set(value);

    if (this.destinationSearchTimer) {
      clearTimeout(this.destinationSearchTimer);
    }

    const trimmed = value.trim();
    if (trimmed.length > 0 && trimmed.length < MIN_SEARCH_LENGTH) {
      this.destinationItems.set([]);
      this.destinationTotalCount.set(0);
      this.destinationHasMore.set(false);
      this.destinationError.set(null);
      return;
    }

    this.destinationSearchTimer = setTimeout(
      () => this.runDestinationSearch(0, false),
      SEARCH_DEBOUNCE_MS,
    );
  }

  protected submitDestinationSearch(): void {
    if (this.destinationSearchTimer) {
      clearTimeout(this.destinationSearchTimer);
    }

    this.runDestinationSearch(0, false);
  }

  protected clearDestinationSearch(): void {
    this.destinationQuery.set('');
    this.runDestinationSearch(0, false);
  }

  protected toggleFilters(): void {
    this.filtersExpanded.update(value => !value);
  }

  protected toggleDestinationType(destinationTypeId: number): void {
    this.selectedDestinationTypeIds.update(current => this.toggleSetValue(current, destinationTypeId));
    this.runDestinationSearch(0, false);
  }

  protected toggleTag(tagId: number): void {
    this.selectedTagIds.update(current => this.toggleSetValue(current, tagId));
    this.runDestinationSearch(0, false);
  }

  protected clearDestinationFilters(): void {
    this.destinationQuery.set('');
    this.selectedDestinationTypeIds.set(new Set());
    this.selectedTagIds.set(new Set());
    this.runDestinationSearch(0, false);
  }

  protected loadMoreDestinations(): void {
    if (this.destinationLoading() || this.destinationLoadingMore() || !this.destinationHasMore()) return;
    this.runDestinationSearch(this.nextDestinationSkip, true);
  }

  protected selectDestination(destination: DestinationDiscoverySearchItem): void {
    this.destinationSvc.setDestinationOverride({
      id: destination.id,
      name: destination.name,
      latitude: destination.latitude,
      longitude: destination.longitude,
    });
    void this.explore.loadForDestination(destination.id);
    this.destinationPickerOpen.set(false);
  }

  protected isDestinationTypeSelected(destinationTypeId: number): boolean {
    return this.selectedDestinationTypeIds().has(destinationTypeId);
  }

  protected isTagSelected(tagId: number): boolean {
    return this.selectedTagIds().has(tagId);
  }

  private loadDestinationFilters(): void {
    this.discovery.getFilters().subscribe({
      next: (filters) => {
        this.destinationTypeFilters.set(filters.destinationTypeFilters ?? []);
        this.tagTypeFilters.set(filters.tagTypeFilters ?? []);
      },
      error: () => {},
    });
  }

  private runDestinationSearch(skip: number, append: boolean): void {
    const trimmed = this.destinationQuery().trim();
    if (trimmed.length > 0 && trimmed.length < MIN_SEARCH_LENGTH) return;

    const requestVersion = ++this.destinationRequestVersion;
    this.destinationError.set(null);

    if (append) {
      this.destinationLoadingMore.set(true);
    } else {
      this.destinationLoading.set(true);
      this.nextDestinationSkip = 0;
    }

    this.discovery.search({
      query: trimmed,
      destinationTypeIds: [...this.selectedDestinationTypeIds()],
      tagIds: [...this.selectedTagIds()],
      skip,
      take: DESTINATION_PAGE_SIZE,
    }).subscribe({
      next: (response) => {
        if (requestVersion !== this.destinationRequestVersion) return;

        this.destinationItems.set(append ? [...this.destinationItems(), ...response.items] : response.items);
        this.destinationTotalCount.set(response.totalCount);
        this.nextDestinationSkip = response.nextSkip;
        this.destinationHasMore.set(response.hasMore);
        this.destinationLoading.set(false);
        this.destinationLoadingMore.set(false);
      },
      error: () => {
        if (requestVersion !== this.destinationRequestVersion) return;

        if (!append) {
          this.destinationItems.set([]);
          this.destinationTotalCount.set(0);
          this.destinationHasMore.set(false);
        }

        this.destinationError.set('Could not load destinations.');
        this.destinationLoading.set(false);
        this.destinationLoadingMore.set(false);
      },
    });
  }

  private toggleSetValue(current: Set<number>, value: number): Set<number> {
    const next = new Set(current);
    if (next.has(value)) {
      next.delete(value);
    } else {
      next.add(value);
    }

    return next;
  }

  private defaultDetailRoute(item: ExploreSectionItem): string {
    if (item.entityType === 'event') return `/events/${item.id}`;
    if (item.entityType === 'stay') return `/stays/${item.id}`;
    return `/places/${item.id}`;
  }

  private withCollectionContext(route: string, extraQueryParams: Record<string, number> = {}): string {
    const [path, query = ''] = route.split('?');
    const params = new URLSearchParams(query);
    params.set('collectionId', String(this.collectionId));

    for (const [key, value] of Object.entries(extraQueryParams)) {
      params.set(key, String(value));
    }

    return `${path}?${params.toString()}`;
  }
}
