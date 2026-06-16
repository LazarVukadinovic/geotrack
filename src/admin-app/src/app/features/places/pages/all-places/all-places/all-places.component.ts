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
import { finalize, Subscription } from 'rxjs';
import { SidebarComponent } from '../../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../../shared/components/topbar/topbar.component';
import { DashboardService } from '../../../../../core/services/dashboard.service';
import { AuthService } from '../../../../../core/services/auth.service';
import {
  PlaceListItem,
  PlacesListService,
} from '../../../../../core/services/places-list.service';
import { PlaceSort } from '../../../../../core/models/place.model';
import { RegionalAssignedRegion } from '../../../../../core/models/dashboard.model';

const PAGE_SIZE = 25;

interface SortOption {
  label: string;
  value: PlaceSort;
}

@Component({
  selector: 'app-all-places',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './all-places.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AllPlacesComponent implements OnInit, OnDestroy {
  private static readonly activeRegionStorageKey = 'geotrack_admin_active_region_id';

  @ViewChild('scrollContainer') scrollContainer?: ElementRef<HTMLDivElement>;

  assignedRegions: RegionalAssignedRegion[] = [];
  activeRegionId: number | null = null;
  items: PlaceListItem[] = [];
  totalItems = 0;
  regionLoading = false;
  isLoading = false;
  isLoadingMore = false;
  authRequired = false;
  errorMessage: string | null = null;
  feedbackMessage: string | null = null;

  searchQuery = '';
  currentSort: PlaceSort = 'newest';
  sortOpen = false;
  readonly sortOptions: SortOption[] = [
    { label: 'Newest First', value: 'newest' },
    { label: 'Oldest First', value: 'oldest' },
    { label: 'Name A-Z', value: 'nameAsc' },
    { label: 'Name Z-A', value: 'nameDesc' },
  ];

  deleteTarget: PlaceListItem | null = null;
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
    private readonly placesService: PlacesListService,
    private readonly cdr: ChangeDetectorRef,
  ) {}

  ngOnInit(): void {
    const saved = this.placesService.getListState();
    if (saved) {
      this.searchQuery = saved.searchQuery;
      this.currentSort = saved.sort;
      this.pendingScrollTop = saved.scrollTop;
    }

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
    return this.assignedRegions.find(region => region.id === this.activeRegionId) ?? null;
  }

  get regionDisplayName(): string {
    return this.activeRegion?.displayName ?? '';
  }

  get currentSortLabel(): string {
    return this.sortOptions.find(option => option.value === this.currentSort)?.label ?? 'Newest First';
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
    if (this.searchQuery.trim()) return 'No places found for this search.';
    return 'No places found in this region.';
  }

  @HostListener('document:click')
  onDocumentClick(): void {
    if (this.sortOpen) {
      this.sortOpen = false;
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
        next: regions => {
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
            this.assignedRegions.find(region => region.id === queryRegionId) ??
            this.assignedRegions.find(region => region.id === persistedRegionId) ??
            this.assignedRegions[0];

          this.setActiveRegion(selectedRegion.id, false);
          this.resetList();
          this.loadPlaces(true);
        },
        error: error => this.handleLoadError(error, 'Unable to load assigned regions.'),
      });
  }

  onRegionSelected(regionId: number): void {
    if (regionId === this.activeRegionId) return;

    this.setActiveRegion(regionId, true);
    this.searchQuery = '';
    this.currentSort = 'newest';
    this.deleteTarget = null;
    this.resetList();
    this.loadPlaces(true);
    this.notifyStateChanged();
  }

  onSearchChange(): void {
    if (this.searchDebounceHandle) {
      clearTimeout(this.searchDebounceHandle);
    }

    this.searchDebounceHandle = setTimeout(() => {
      this.resetList();
      this.loadPlaces(true);
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
    this.loadPlaces(true);
    this.notifyStateChanged();
  }

  loadMore(): void {
    if (!this.hasMore || this.isLoadingMore) return;
    this.loadPlaces(false);
  }

  navigateToAdd(): void {
    this.router.navigate(['/places/new'], {
      queryParams: this.activeRegionId ? { regionId: this.activeRegionId } : undefined,
      state: { returnTo: '/places', regionId: this.activeRegionId },
    });
  }

  viewPlace(item: PlaceListItem): void {
    this.saveListState();
    this.router.navigate([item.detailRoute ?? `/places/detail/${item.id}`], {
      queryParams: this.activeRegionId ? { regionId: this.activeRegionId } : undefined,
      state: item,
    });
  }

  editPlace(item: PlaceListItem): void {
    this.saveListState();
    const full = this.placesService.findById(item.id) ?? item;
    this.router.navigate([full.editRoute ?? `/places/edit/${full.id}`], {
      queryParams: this.activeRegionId ? { regionId: this.activeRegionId } : undefined,
      state: { place: full, editMode: true, returnTo: '/places', regionId: this.activeRegionId },
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

  trackById(_: number, item: PlaceListItem): string {
    return item.id;
  }

  hasImage(item: PlaceListItem): boolean {
    return Boolean(item.imageUrl) && !this.failedImageIds.has(item.id);
  }

  onImageError(item: PlaceListItem): void {
    this.failedImageIds.add(item.id);
    this.notifyStateChanged();
  }

  promptDelete(item: PlaceListItem, event?: MouseEvent): void {
    event?.stopPropagation();
    this.errorMessage = null;
    this.deleteModalError = null;
    this.deleteTarget = item;
    this.isCheckingDelete = true;
    this.notifyStateChanged();

    this.placesService.getPlace(item.regionId, item.id).subscribe({
      next: dto => {
        if (dto.canDelete === false) {
          this.deleteModalError = 'Cannot delete: this place has related content.';
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
    if (!this.deleteTarget || !this.activeRegionId || this.isDeleting || this.deleteModalError) return;

    const target = this.deleteTarget;
    this.isDeleting = true;
    this.notifyStateChanged();

    this.deleteSubscription = this.placesService
      .deletePlace(this.activeRegionId, target.id)
      .pipe(
        finalize(() => {
          this.isDeleting = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: () => {
          this.deleteTarget = null;
          this.items = this.items.filter(item => item.id !== target.id);
          this.totalItems = Math.max(0, this.totalItems - 1);
          this.feedbackMessage = 'Place deleted.';

          if (this.items.length === 0 && this.totalItems > 0) {
            this.resetList();
            this.loadPlaces(true);
          }
        },
        error: error => this.handleActionError(error),
      });
  }

  private loadPlaces(reset: boolean): void {
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

    this.listSubscription = this.placesService
      .list({
        regionId: this.activeRegionId,
        search: this.searchQuery,
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
        next: response => {
          if (this.listRequestSequence !== requestId) return;

          const newItems = (response.items ?? []).map(item => this.placesService.toListItem(item));
          this.items = reset ? newItems : [...this.items, ...newItems];
          this.placesService.rememberItems(this.items);
          this.totalItems = this.toSafeCount(response.totalCount);
        },
        error: error => {
          if (this.listRequestSequence === requestId) {
            this.handleLoadError(error, 'Unable to load places.');
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
        this.errorMessage = 'Sign in with an authenticated Regional Manager account to view places.';
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
      this.deleteModalError = error.error?.message ?? 'Unable to delete this place.';
      this.notifyStateChanged();
      return;
    }

    this.deleteModalError = 'Unable to delete this place.';
    this.notifyStateChanged();
  }

  private saveListState(): void {
    this.placesService.saveListState({
      searchQuery: this.searchQuery,
      sort: this.currentSort,
      displayCount: this.items.length,
      scrollTop: this.scrollContainer?.nativeElement?.scrollTop ?? 0,
    });
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
      const value = sessionStorage.getItem(AllPlacesComponent.activeRegionStorageKey);
      const parsed = value ? Number(value) : NaN;
      return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
    } catch {
      return null;
    }
  }

  private persistRegion(regionId: number): void {
    try {
      sessionStorage.setItem(AllPlacesComponent.activeRegionStorageKey, String(regionId));
    } catch {
      // Session persistence is a convenience only.
    }
  }

  private clearPersistedRegion(): void {
    try {
      sessionStorage.removeItem(AllPlacesComponent.activeRegionStorageKey);
    } catch {
      // Session persistence is a convenience only.
    }
  }
}
