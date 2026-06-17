import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  HostListener,
  OnDestroy,
  OnInit,
  ViewChild,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { finalize, forkJoin, Subscription } from 'rxjs';
import { SidebarComponent } from '../../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../../shared/components/topbar/topbar.component';
import { DashboardService } from '../../../../../core/services/dashboard.service';
import { AuthService } from '../../../../../core/services/auth.service';
import { EventListItem, EventsListService } from '../../../../../core/services/events-list.service';
import {
  EventDateFilter,
  EventSort,
  EventStatusFilter,
  RegionalEventParentDestinationDto,
  RegionalEventParentLocationDto,
  RegionalEventParentPlaceDto,
  RegionalEventTagDto,
  RegionalEventTypeDto,
} from '../../../../../core/models/event.model';
import { RegionalAssignedRegion } from '../../../../../core/models/dashboard.model';

const PAGE_SIZE = 25;

interface SortOption {
  label: string;
  value: EventSort;
}

@Component({
  selector: 'app-all-events',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './all-events.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AllEventsComponent implements OnInit, OnDestroy {
  private static readonly activeRegionStorageKey = 'geotrack_admin_active_region_id';

  @ViewChild('scrollContainer') scrollContainer?: ElementRef<HTMLDivElement>;

  assignedRegions: RegionalAssignedRegion[] = [];
  activeRegionId: number | null = null;
  items: EventListItem[] = [];
  totalItems = 0;
  regionLoading = false;
  isLoading = false;
  isLoadingMore = false;
  authRequired = false;
  errorMessage: string | null = null;
  feedbackMessage: string | null = null;

  searchQuery = '';
  currentSort: EventSort = 'newest';
  sortOpen = false;
  readonly sortOptions: SortOption[] = [
    { label: 'Newest First', value: 'newest' },
    { label: 'Oldest First', value: 'oldest' },
    { label: 'Name A-Z', value: 'nameAsc' },
    { label: 'Name Z-A', value: 'nameDesc' },
    { label: 'Event Date Soonest', value: 'eventDateAsc' },
    { label: 'Event Date Latest', value: 'eventDateDesc' },
  ];

  statusFilter: EventStatusFilter = 'all';
  selectedDestinationId: number | null = null;
  selectedLocationId: number | null = null;
  selectedPlaceId: number | null = null;
  selectedEventTypeId: number | null = null;
  dateFilter: EventDateFilter = 'upcoming';
  dateFrom = '';
  dateTo = '';
  selectedTagIds: number[] = [];
  destinations: RegionalEventParentDestinationDto[] = [];
  locations: RegionalEventParentLocationDto[] = [];
  places: RegionalEventParentPlaceDto[] = [];
  eventTypes: RegionalEventTypeDto[] = [];
  filterTags: RegionalEventTagDto[] = [];
  moreFiltersOpen = false;
  filterOptionsLoading = false;
  readonly statusOptions: { label: string; value: EventStatusFilter }[] = [
    { label: 'All statuses', value: 'all' },
    { label: 'Published', value: 'published' },
    { label: 'Draft', value: 'draft' },
    { label: 'Archived', value: 'archived' },
  ];
  readonly dateOptions: { label: string; value: EventDateFilter }[] = [
    { label: 'All dates', value: 'all' },
    { label: 'Upcoming', value: 'upcoming' },
    { label: 'Ongoing now', value: 'ongoing' },
    { label: 'Past', value: 'past' },
    { label: 'Custom range', value: 'custom' },
  ];

  deleteTarget: EventListItem | null = null;
  isDeleting = false;
  isCheckingDelete = false;
  deleteModalError: string | null = null;

  private searchDebounceHandle: ReturnType<typeof setTimeout> | null = null;
  private listRequestSequence = 0;
  private listSubscription: Subscription | null = null;
  private deleteSubscription: Subscription | null = null;
  private pendingScrollTop = 0;
  private failedImageIds = new Set<string>();

  constructor(
    private readonly router: Router,
    private readonly route: ActivatedRoute,
    private readonly auth: AuthService,
    private readonly dashboardService: DashboardService,
    private readonly eventsService: EventsListService,
    private readonly cdr: ChangeDetectorRef,
  ) {}

  ngOnInit(): void {
    const saved = this.eventsService.getListState();
    if (saved) {
      this.searchQuery = saved.searchQuery;
      this.currentSort = saved.sort;
      this.statusFilter = (saved.status as EventStatusFilter) ?? 'all';
      this.selectedDestinationId = saved.destinationId ?? null;
      this.selectedEventTypeId = saved.eventTypeId ?? null;
      this.dateFilter = (saved.dateFilter as EventDateFilter) ?? 'upcoming';
      this.dateFrom = saved.dateFrom ?? '';
      this.dateTo = saved.dateTo ?? '';
      this.selectedTagIds = [...(saved.tagIds ?? [])];
      this.pendingScrollTop = saved.scrollTop;
    }
    this.restoreFiltersFromUrl();
    this.loadAssignedRegions();
  }

  ngOnDestroy(): void {
    this.listRequestSequence++;
    this.listSubscription?.unsubscribe();
    this.deleteSubscription?.unsubscribe();

    if (this.searchDebounceHandle) {
      clearTimeout(this.searchDebounceHandle);
    }
  }

  get userName(): string {
    return this.auth.fullName;
  }

  get userRole(): string {
    return this.auth.getUser()?.role ?? '';
  }

  get activeRegion(): RegionalAssignedRegion | null {
    return this.assignedRegions.find((region) => region.id === this.activeRegionId) ?? null;
  }

  get regionDisplayName(): string {
    return this.activeRegion?.displayName ?? '';
  }

  get currentSortLabel(): string {
    return (
      this.sortOptions.find((option) => option.value === this.currentSort)?.label ?? 'Newest First'
    );
  }

  get filteredLocations(): RegionalEventParentLocationDto[] {
    return this.selectedDestinationId
      ? this.locations.filter((x) => x.destinationId === this.selectedDestinationId)
      : this.locations;
  }
  get filteredPlaces(): RegionalEventParentPlaceDto[] {
    return this.places.filter(
      (x) =>
        (!this.selectedDestinationId || x.destinationId === this.selectedDestinationId) &&
        (!this.selectedLocationId || x.locationId === this.selectedLocationId),
    );
  }
  get activeFilterCount(): number {
    return (
      (this.statusFilter !== 'all' ? 1 : 0) +
      (this.selectedDestinationId ? 1 : 0) +
      (this.selectedEventTypeId ? 1 : 0) +
      (this.dateFilter !== 'all' ? 1 : 0) +
      this.selectedTagIds.length
    );
  }
  get selectedDestinationName(): string {
    return (
      this.destinations.find((x) => x.id === this.selectedDestinationId)?.name ?? 'Destination'
    );
  }
  get selectedLocationName(): string {
    return this.locations.find((x) => x.id === this.selectedLocationId)?.name ?? 'Location';
  }
  get selectedPlaceName(): string {
    return this.places.find((x) => x.id === this.selectedPlaceId)?.name ?? 'Place';
  }
  get selectedEventTypeName(): string {
    return this.eventTypes.find((x) => x.id === this.selectedEventTypeId)?.name ?? 'Type';
  }
  get tagGroups(): { label: string; tags: RegionalEventTagDto[] }[] {
    const groups = new Map<string, RegionalEventTagDto[]>();
    for (const tag of this.filterTags) {
      const label = tag.tagTypeLabel || tag.tagTypeName || 'Tags';
      groups.set(label, [...(groups.get(label) ?? []), tag]);
    }
    return [...groups.entries()].map(([label, tags]) => ({ label, tags }));
  }

  get hasMore(): boolean {
    return this.items.length < this.totalItems;
  }

  get nextPageCount(): number {
    return Math.min(PAGE_SIZE, Math.max(0, this.totalItems - this.items.length));
  }

  get waitingForRegion(): boolean {
    return this.regionLoading && !this.activeRegionId;
  }

  get emptyMessage(): string {
    if (!this.activeRegionId) return 'No active assigned region.';
    if (this.searchQuery.trim() || this.activeFilterCount > 0)
      return 'No events match the selected filters.';
    return 'No events found in this region.';
  }

  @HostListener('document:click')
  onDocumentClick(): void {
    if (this.sortOpen || this.moreFiltersOpen) {
      this.sortOpen = false;
      this.moreFiltersOpen = false;
      this.notifyStateChanged();
    }
  }

  loadAssignedRegions(): void {
    this.regionLoading = true;
    this.errorMessage = null;
    this.authRequired = false;
    this.notifyStateChanged();

    this.dashboardService
      .getAssignedRegions()
      .pipe(
        finalize(() => {
          this.regionLoading = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: (regions) => {
          this.assignedRegions = regions ?? [];

          if (this.assignedRegions.length === 0) {
            this.activeRegionId = null;
            this.items = [];
            this.totalItems = 0;
            this.isLoading = false;
            this.clearPersistedRegion();
            this.notifyStateChanged();
            return;
          }

          const queryRegionId = Number(this.route.snapshot.queryParamMap.get('regionId'));
          const persistedRegionId = this.readPersistedRegionId();
          const selectedRegion =
            this.assignedRegions.find((region) => region.id === queryRegionId) ??
            this.assignedRegions.find((region) => region.id === persistedRegionId) ??
            this.assignedRegions[0];

          this.setActiveRegion(selectedRegion.id, false);
          this.loadFilterOptions();
          this.resetList();
          this.loadEvents(true);
        },
        error: (error) => this.handleLoadError(error, 'Unable to load assigned regions.'),
      });
  }

  onRegionSelected(regionId: number): void {
    if (regionId === this.activeRegionId) return;

    this.setActiveRegion(regionId, true);
    this.resetFilters();
    this.deleteTarget = null;
    this.loadFilterOptions();
    this.resetList();
    this.syncUrl();
    this.loadEvents(true);
    this.notifyStateChanged();
  }

  onSearchChange(): void {
    if (this.searchDebounceHandle) {
      clearTimeout(this.searchDebounceHandle);
    }

    this.searchDebounceHandle = setTimeout(() => {
      this.resetList();
      this.syncUrl();
      this.loadEvents(true);
      this.notifyStateChanged();
    }, 300);
  }

  clearSearch(): void {
    this.searchQuery = '';
    this.onSearchChange();
  }

  applySortOption(option: SortOption): void {
    if (option.value === this.currentSort) {
      this.sortOpen = false;
      this.notifyStateChanged();
      return;
    }

    this.currentSort = option.value;
    this.sortOpen = false;
    this.resetList();
    this.syncUrl();
    this.loadEvents(true);
    this.notifyStateChanged();
  }

  onDestinationFilterChange(): void {
    const loc = this.locations.find((x) => x.id === this.selectedLocationId);
    if (loc && loc.destinationId !== this.selectedDestinationId) {
      this.selectedLocationId = null;
      this.selectedPlaceId = null;
    }
    const place = this.places.find((x) => x.id === this.selectedPlaceId);
    if (place && place.destinationId !== this.selectedDestinationId) this.selectedPlaceId = null;
    this.onFilterChange();
  }
  onLocationFilterChange(): void {
    const loc = this.locations.find((x) => x.id === this.selectedLocationId);
    if (loc) this.selectedDestinationId = loc.destinationId;
    const place = this.places.find((x) => x.id === this.selectedPlaceId);
    if (place && place.locationId !== this.selectedLocationId) this.selectedPlaceId = null;
    this.onFilterChange();
  }
  onPlaceFilterChange(): void {
    const place = this.places.find((x) => x.id === this.selectedPlaceId);
    if (place) {
      this.selectedLocationId = place.locationId;
      this.selectedDestinationId = place.destinationId;
    }
    this.onFilterChange();
  }
  onDateFilterChange(): void {
    if (this.dateFilter !== 'custom') {
      this.dateFrom = '';
      this.dateTo = '';
    }
    this.onFilterChange();
  }
  onFilterChange(): void {
    this.resetList();
    this.syncUrl();
    this.loadEvents(true);
    this.notifyStateChanged();
  }
  toggleTag(tagId: number): void {
    this.selectedTagIds = this.selectedTagIds.includes(tagId)
      ? this.selectedTagIds.filter((id) => id !== tagId)
      : [...this.selectedTagIds, tagId];
    this.onFilterChange();
  }
  removeTag(tagId: number): void {
    this.selectedTagIds = this.selectedTagIds.filter((id) => id !== tagId);
    this.onFilterChange();
  }
  tagName(tagId: number): string {
    return this.filterTags.find((x) => x.id === tagId)?.name ?? `Tag ${tagId}`;
  }
  clearAllFilters(): void {
    this.statusFilter = 'all';
    this.selectedDestinationId = null;
    this.selectedLocationId = null;
    this.selectedPlaceId = null;
    this.selectedEventTypeId = null;
    this.dateFilter = 'all';
    this.dateFrom = '';
    this.dateTo = '';
    this.selectedTagIds = [];
    this.onFilterChange();
  }

  loadMore(): void {
    if (!this.hasMore || this.isLoadingMore) return;
    this.loadEvents(false);
  }

  navigateToAdd(): void {
    this.router.navigate(['/events/new'], {
      queryParams: this.activeRegionId ? { regionId: this.activeRegionId } : undefined,
      state: {
        returnTo: '/events',
        regionId: this.activeRegionId,
        regionDisplayName: this.regionDisplayName,
      },
    });
  }

  viewEvent(item: EventListItem): void {
    this.saveListState();
    this.router.navigate([item.detailRoute ?? `/events/detail/${item.id}`], {
      queryParams: this.activeRegionId ? { regionId: this.activeRegionId } : undefined,
      state: item,
    });
  }

  editEvent(item: EventListItem): void {
    this.saveListState();
    const full = this.eventsService.findById(item.id) ?? item;
    this.router.navigate([full.editRoute ?? `/events/edit/${full.id}`], {
      queryParams: this.activeRegionId ? { regionId: this.activeRegionId } : undefined,
      state: {
        place: full,
        editMode: true,
        returnTo: '/events',
        regionId: this.activeRegionId,
        regionDisplayName: this.regionDisplayName,
      },
    });
  }

  statusClass(status: string): string {
    switch ((status ?? '').toLowerCase()) {
      case 'published':
        return 'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200';
      case 'draft':
        return 'bg-amber-50 text-amber-700 ring-1 ring-amber-200';
      case 'archived':
        return 'bg-gray-100 text-gray-500 ring-1 ring-gray-200';
      default:
        return 'bg-gray-100 text-gray-600 ring-1 ring-gray-200';
    }
  }

  trackById(_: number, item: EventListItem): string {
    return item.id;
  }

  hasImage(item: EventListItem): boolean {
    return Boolean(item.imageUrl) && !this.failedImageIds.has(item.id);
  }

  onImageError(item: EventListItem): void {
    this.failedImageIds.add(item.id);
    this.notifyStateChanged();
  }

  promptDelete(item: EventListItem, event?: MouseEvent): void {
    event?.stopPropagation();
    this.errorMessage = null;
    this.deleteModalError = null;
    this.deleteTarget = item;
    this.isCheckingDelete = true;
    this.notifyStateChanged();

    this.eventsService.getEvent(item.regionId, item.id).subscribe({
      next: (dto) => {
        if (dto.canDelete === false) {
          this.deleteModalError = 'Cannot delete: this event has related content.';
        }
        this.isCheckingDelete = false;
        this.notifyStateChanged();
      },
      error: () => {
        this.isCheckingDelete = false;
        this.notifyStateChanged();
      },
    });
  }

  cancelDelete(): void {
    if (this.isDeleting) return;
    this.deleteTarget = null;
    this.deleteModalError = null;
    this.notifyStateChanged();
  }

  confirmDelete(): void {
    if (!this.deleteTarget || !this.activeRegionId || this.isDeleting || this.deleteModalError)
      return;

    const target = this.deleteTarget;
    this.isDeleting = true;
    this.notifyStateChanged();

    this.deleteSubscription = this.eventsService
      .deleteEvent(this.activeRegionId, target.id)
      .pipe(
        finalize(() => {
          this.isDeleting = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: () => {
          this.deleteTarget = null;
          this.items = this.items.filter((item) => item.id !== target.id);
          this.totalItems = Math.max(0, this.totalItems - 1);
          this.feedbackMessage = 'Event deleted.';

          if (this.items.length === 0 && this.totalItems > 0) {
            this.resetList();
            this.loadEvents(true);
          }
        },
        error: (error) => this.handleActionError(error),
      });
  }

  private loadEvents(reset: boolean): void {
    if (!this.activeRegionId) {
      this.listRequestSequence++;
      this.listSubscription?.unsubscribe();
      this.isLoading = false;
      this.isLoadingMore = false;
      this.items = [];
      this.totalItems = 0;
      this.notifyStateChanged();
      return;
    }

    const requestId = ++this.listRequestSequence;
    const skip = reset ? 0 : this.items.length;
    this.listSubscription?.unsubscribe();

    if (reset) {
      this.isLoading = true;
      this.items = [];
      this.totalItems = 0;
    } else {
      this.isLoadingMore = true;
    }

    this.errorMessage = null;
    this.authRequired = false;
    this.notifyStateChanged();

    this.listSubscription = this.eventsService
      .list({
        regionId: this.activeRegionId,
        search: this.searchQuery,
        status: this.statusFilter,
        destinationId: this.selectedDestinationId,
        eventTypeId: this.selectedEventTypeId,
        dateFilter: this.dateFilter,
        dateFrom:
          this.dateFilter === 'custom' && this.dateFrom ? `${this.dateFrom}T00:00:00Z` : null,
        dateTo: this.dateFilter === 'custom' && this.dateTo ? `${this.dateTo}T23:59:59Z` : null,
        tagIds: this.selectedTagIds,
        sort: this.currentSort,
        skip,
        take: PAGE_SIZE,
      })
      .pipe(
        finalize(() => {
          if (this.listRequestSequence === requestId) {
            this.isLoading = false;
            this.isLoadingMore = false;
            this.restoreScrollPositionIfNeeded();
            this.notifyStateChanged();
          }
        }),
      )
      .subscribe({
        next: (response) => {
          if (this.listRequestSequence !== requestId) return;

          const newItems = (response.items ?? []).map((item) =>
            this.eventsService.toListItem(item),
          );
          this.items = reset ? newItems : [...this.items, ...newItems];
          this.eventsService.rememberItems(this.items);
          this.totalItems = this.toSafeCount(response.totalCount);
        },
        error: (error) => {
          if (this.listRequestSequence === requestId) {
            this.handleLoadError(error, 'Unable to load events.');
          }
        },
      });
  }

  private resetList(): void {
    this.items = [];
    this.totalItems = 0;
    this.failedImageIds.clear();
  }

  private handleLoadError(error: unknown, fallback: string): void {
    this.isLoading = false;
    this.isLoadingMore = false;
    this.items = [];
    this.totalItems = 0;

    if (error instanceof HttpErrorResponse) {
      if (error.status === 401) {
        this.authRequired = true;
        this.errorMessage =
          'Sign in with an authenticated Regional Manager account to view events.';
        this.notifyStateChanged();
        return;
      }

      if (error.status === 403) {
        this.errorMessage = 'This account is not authorized for the selected region.';
        this.notifyStateChanged();
        return;
      }
    }

    this.errorMessage = fallback;
    this.notifyStateChanged();
  }

  private handleActionError(error: unknown): void {
    if (error instanceof HttpErrorResponse) {
      this.deleteModalError = error.error?.message ?? 'Unable to delete this event.';
      this.notifyStateChanged();
      return;
    }

    this.deleteModalError = 'Unable to delete this event.';
    this.notifyStateChanged();
  }

  private saveListState(): void {
    this.eventsService.saveListState({
      searchQuery: this.searchQuery,
      sort: this.currentSort,
      status: this.statusFilter,
      destinationId: this.selectedDestinationId,
      eventTypeId: this.selectedEventTypeId,
      dateFilter: this.dateFilter,
      dateFrom: this.dateFrom,
      dateTo: this.dateTo,
      tagIds: [...this.selectedTagIds],
      displayCount: this.items.length,
      scrollTop: this.scrollContainer?.nativeElement?.scrollTop ?? 0,
    });
  }

  private loadFilterOptions(): void {
    if (!this.activeRegionId) return;
    this.filterOptionsLoading = true;
    forkJoin({
      destinations: this.eventsService.getParentDestinations(this.activeRegionId, null, 500),
      types: this.eventsService.getEventTypes(),
      tags: this.eventsService.getEventTags(),
    })
      .pipe(
        finalize(() => {
          this.filterOptionsLoading = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: ({ destinations, types, tags }) => {
          this.destinations = destinations ?? [];
          this.locations = [];
          this.places = [];
          this.selectedLocationId = null;
          this.selectedPlaceId = null;
          this.eventTypes = types ?? [];
          this.filterTags = (tags ?? []).filter((tag) => tag.isFilterable);
          if (
            this.selectedDestinationId &&
            !this.destinations.some((x) => x.id === this.selectedDestinationId)
          ) {
            this.selectedDestinationId = null;
          }
          if (
            this.selectedEventTypeId &&
            !this.eventTypes.some((x) => x.id === this.selectedEventTypeId)
          )
            this.selectedEventTypeId = null;
          this.selectedTagIds = this.selectedTagIds.filter((id) =>
            this.filterTags.some((tag) => tag.id === id),
          );
          this.notifyStateChanged();
        },
        error: () => {
          this.destinations = [];
          this.locations = [];
          this.places = [];
          this.eventTypes = [];
          this.filterTags = [];
        },
      });
  }
  private resetFilters(): void {
    this.searchQuery = '';
    this.currentSort = 'newest';
    this.statusFilter = 'all';
    this.selectedDestinationId = null;
    this.selectedLocationId = null;
    this.selectedPlaceId = null;
    this.selectedEventTypeId = null;
    this.dateFilter = 'upcoming';
    this.dateFrom = '';
    this.dateTo = '';
    this.selectedTagIds = [];
    this.moreFiltersOpen = false;
  }
  private restoreFiltersFromUrl(): void {
    const p = this.route.snapshot.queryParamMap;
    if (p.has('q')) this.searchQuery = p.get('q') ?? '';
    const sort = p.get('sort') as EventSort | null;
    if (sort && this.sortOptions.some((x) => x.value === sort)) this.currentSort = sort;
    const status = p.get('status') as EventStatusFilter | null;
    if (status && this.statusOptions.some((x) => x.value === status)) this.statusFilter = status;
    this.selectedDestinationId =
      this.parsePositiveInt(p.get('destinationId')) ?? this.selectedDestinationId;
    this.selectedEventTypeId =
      this.parsePositiveInt(p.get('eventTypeId')) ?? this.selectedEventTypeId;
    const date = p.get('date') as EventDateFilter | null;
    if (date && this.dateOptions.some((x) => x.value === date)) this.dateFilter = date;
    if (p.has('from')) this.dateFrom = p.get('from') ?? '';
    if (p.has('to')) this.dateTo = p.get('to') ?? '';
    if (p.has('tagId'))
      this.selectedTagIds = p
        .getAll('tagId')
        .map(Number)
        .filter((x) => Number.isFinite(x) && x > 0);
  }
  private syncUrl(): void {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        q: this.searchQuery.trim() || null,
        sort: this.currentSort !== 'newest' ? this.currentSort : null,
        status: this.statusFilter !== 'all' ? this.statusFilter : null,
        destinationId: this.selectedDestinationId,
        locationId: null,
        placeId: null,
        eventTypeId: this.selectedEventTypeId,
        date: this.dateFilter !== 'upcoming' ? this.dateFilter : null,
        from: this.dateFilter === 'custom' && this.dateFrom ? this.dateFrom : null,
        to: this.dateFilter === 'custom' && this.dateTo ? this.dateTo : null,
        price: null,
        tagId: this.selectedTagIds.length ? this.selectedTagIds : null,
      },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }
  private parsePositiveInt(value: string | null): number | null {
    const n = value ? Number(value) : NaN;
    return Number.isFinite(n) && n > 0 ? n : null;
  }

  private setActiveRegion(regionId: number, updateUrl: boolean): void {
    this.activeRegionId = regionId;
    this.persistRegion(regionId);

    if (updateUrl) {
      this.router.navigate([], {
        relativeTo: this.route,
        queryParams: { regionId },
        queryParamsHandling: 'merge',
        replaceUrl: true,
      });
    }
  }

  private restoreScrollPositionIfNeeded(): void {
    if (this.pendingScrollTop <= 0 || !this.scrollContainer?.nativeElement) return;

    const scrollTop = this.pendingScrollTop;
    this.pendingScrollTop = 0;
    setTimeout(() => {
      if (this.scrollContainer?.nativeElement) {
        this.scrollContainer.nativeElement.scrollTop = scrollTop;
      }
    }, 0);
  }

  private toSafeCount(value: unknown): number {
    return typeof value === 'number' && Number.isFinite(value) && value >= 0 ? value : 0;
  }

  private notifyStateChanged(): void {
    this.cdr.markForCheck();
  }

  private readPersistedRegionId(): number | null {
    try {
      const value = sessionStorage.getItem(AllEventsComponent.activeRegionStorageKey);
      const parsed = value ? Number(value) : NaN;
      return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
    } catch {
      return null;
    }
  }

  private persistRegion(regionId: number): void {
    try {
      sessionStorage.setItem(AllEventsComponent.activeRegionStorageKey, String(regionId));
    } catch {
      // Session persistence is a convenience only.
    }
  }

  private clearPersistedRegion(): void {
    try {
      sessionStorage.removeItem(AllEventsComponent.activeRegionStorageKey);
    } catch {
      // Session persistence is a convenience only.
    }
  }
}
