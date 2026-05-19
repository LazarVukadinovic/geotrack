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
import { HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { Subscription, finalize } from 'rxjs';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import {
  RegionListItem,
  RegionSortValue,
  RegionsService,
} from '../../../../core/services/national/regions.service';
import { AuthService } from '../../../../core/services/auth.service';
import { TerritoryDashboardService } from '../../../../core/services/territory-dashboard.service';
import { RegionalAssignedRegion } from '../../../../core/models/dashboard.model';

const PAGE_SIZE = 25;

interface SortOption {
  label: string;
  value: RegionSortValue;
}

@Component({
  selector: 'all-regions',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './regions.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AllRegionsComponent implements OnInit, OnDestroy {
  @ViewChild('scrollContainer') scrollContainer?: ElementRef<HTMLDivElement>;

  assignedTerritories: RegionalAssignedRegion[] = [];
  activeTerritoryId: number | null = null;
  territoryLoading = false;

  items: RegionListItem[] = [];
  totalItems = 0;
  isLoading = false;
  isLoadingMore = false;
  errorMessage: string | null = null;

  searchQuery = '';
  currentSort: RegionSortValue = 'status';
  sortOpen = false;
  readonly sortOptions: SortOption[] = [
    { label: 'Status: Active First', value: 'status' },
    { label: 'Region Name', value: 'name' },
    { label: 'Most Destinations', value: 'mostDestinations' },
    { label: 'Most Locations', value: 'mostLocations' },
    { label: 'Most Places', value: 'mostPlaces' },
    { label: 'Most Events', value: 'mostEvents' },
    { label: 'Most Pending Requests', value: 'mostPending' },
    { label: 'Highest Health Score', value: 'healthHigh' },
    { label: 'Lowest Health Score', value: 'healthLow' },
  ];

  private searchDebounceHandle: ReturnType<typeof setTimeout> | null = null;
  private listRequestSequence = 0;
  private listSubscription: Subscription | null = null;
  private territoriesSubscription: Subscription | null = null;
  private pendingScrollTop = 0;
  private initialTake = PAGE_SIZE;
  private failedImageIds = new Set<string>();

  constructor(
    private readonly router: Router,
    private readonly svc: RegionsService,
    private readonly territoryDashboard: TerritoryDashboardService,
    private readonly cdr: ChangeDetectorRef,
    private readonly auth: AuthService,
  ) {}

  ngOnInit(): void {
    const saved = this.svc.getListState();
    if (saved) {
      this.searchQuery = saved.searchQuery;
      this.currentSort = saved.sort;
      this.initialTake = Math.max(PAGE_SIZE, saved.loadedCount);
      this.pendingScrollTop = saved.scrollTop;
      this.svc.clearListState();
    }

    this.loadAssignedTerritories();
  }

  ngOnDestroy(): void {
    this.listRequestSequence++;
    this.listSubscription?.unsubscribe();
    this.territoriesSubscription?.unsubscribe();

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

  get currentSortLabel(): string {
    return this.sortOptions.find(option => option.value === this.currentSort)?.label ?? 'Status: Active First';
  }

  get activeTerritoryName(): string {
    return this.assignedTerritories.find(item => item.id === this.activeTerritoryId)?.displayName ?? '';
  }

  get hasMore(): boolean {
    return this.items.length < this.totalItems;
  }

  get nextPageCount(): number {
    return Math.min(PAGE_SIZE, Math.max(0, this.totalItems - this.items.length));
  }

  get emptyMessage(): string {
    if (!this.activeTerritoryId) {
      return 'No assigned territory is active.';
    }

    if (this.searchQuery.trim()) {
      return 'No regions match this search.';
    }

    return 'No regions found in this territory.';
  }

  @HostListener('document:click')
  onDocumentClick(): void {
    if (this.sortOpen) {
      this.sortOpen = false;
      this.notifyStateChanged();
    }
  }

  onTerritoryChange(territoryId: number): void {
    if (territoryId === this.activeTerritoryId) return;

    this.activeTerritoryId = territoryId;
    this.svc.setActiveTerritoryId(territoryId);
    this.searchQuery = '';
    this.currentSort = 'status';
    this.initialTake = PAGE_SIZE;
    this.resetList();
    this.loadRegions(true);
    this.notifyStateChanged();
  }

  onSearchChange(): void {
    if (this.searchDebounceHandle) {
      clearTimeout(this.searchDebounceHandle);
    }

    this.searchDebounceHandle = setTimeout(() => {
      this.initialTake = PAGE_SIZE;
      this.resetList();
      this.loadRegions(true);
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
    this.initialTake = PAGE_SIZE;
    this.resetList();
    this.loadRegions(true);
    this.notifyStateChanged();
  }

  loadMore(): void {
    if (!this.hasMore || this.isLoadingMore) return;
    this.loadRegions(false);
  }

  viewRegion(item: RegionListItem): void {
    this.saveListState();
    this.router.navigate([item.detailRoute ?? `/territory/regions/${item.regionId}`], {
      state: { regionId: item.regionId, territoryId: item.territoryId },
    });
  }

  trackById(_: number, item: RegionListItem): string {
    return item.id;
  }

  hasImage(item: RegionListItem): boolean {
    return Boolean(item.imageUrl) && !this.failedImageIds.has(item.id);
  }

  onImageError(item: RegionListItem): void {
    this.failedImageIds.add(item.id);
    this.notifyStateChanged();
  }

  statusClass(status: string): string {
    return status === 'Active'
      ? 'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200'
      : 'bg-gray-100 text-gray-500 ring-1 ring-gray-200';
  }

  scoreClass(score?: number | null): string {
    if (score === null || score === undefined) {
      return 'bg-gray-100 text-gray-500 ring-1 ring-gray-200';
    }

    if (score >= 75) {
      return 'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200';
    }

    if (score >= 50) {
      return 'bg-amber-50 text-amber-700 ring-1 ring-amber-200';
    }

    return 'bg-red-50 text-red-700 ring-1 ring-red-200';
  }

  private loadAssignedTerritories(): void {
    this.territoryLoading = true;
    this.errorMessage = null;
    this.notifyStateChanged();

    this.territoriesSubscription = this.territoryDashboard
      .getAssignedTerritories()
      .pipe(
        finalize(() => {
          this.territoryLoading = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: territories => {
          this.assignedTerritories = (territories ?? []).map(territory => ({
            id: territory.id,
            name: territory.name,
            displayName: territory.displayName,
            statusLabel: territory.statusLabel,
          }));

          const persistedId = this.svc.getActiveTerritoryId();
          const selected =
            this.assignedTerritories.find(territory => territory.id === persistedId) ??
            this.assignedTerritories[0] ??
            null;

          this.activeTerritoryId = selected?.id ?? null;
          this.svc.setActiveTerritoryId(this.activeTerritoryId);

          if (!this.activeTerritoryId) {
            this.resetList();
            this.isLoading = false;
            return;
          }

          this.loadRegions(true);
        },
        error: error => this.handleLoadError(error, 'Unable to load assigned territories.'),
      });
  }

  private loadRegions(reset: boolean): void {
    if (!this.activeTerritoryId) {
      this.listRequestSequence++;
      this.listSubscription?.unsubscribe();
      this.resetList();
      this.isLoading = false;
      this.isLoadingMore = false;
      this.notifyStateChanged();
      return;
    }

    const requestId = ++this.listRequestSequence;
    const skip = reset ? 0 : this.items.length;
    const take = reset ? this.initialTake : PAGE_SIZE;
    this.listSubscription?.unsubscribe();

    if (reset) {
      this.isLoading = true;
      this.items = [];
      this.totalItems = 0;
      this.failedImageIds.clear();
    } else {
      this.isLoadingMore = true;
    }

    this.errorMessage = null;
    this.notifyStateChanged();

    this.listSubscription = this.svc
      .getPage$({
        territoryId: this.activeTerritoryId,
        search: this.searchQuery,
        sort: this.currentSort,
        skip,
        take,
      })
      .pipe(
        finalize(() => {
          if (this.listRequestSequence === requestId) {
            this.isLoading = false;
            this.isLoadingMore = false;
            this.initialTake = PAGE_SIZE;
            this.restoreScrollPositionIfNeeded();
            this.notifyStateChanged();
          }
        }),
      )
      .subscribe({
        next: page => {
          if (this.listRequestSequence !== requestId) return;

          this.items = reset ? page.items : [...this.items, ...page.items];
          this.totalItems = this.toSafeCount(page.total);
        },
        error: error => {
          if (this.listRequestSequence === requestId) {
            this.handleLoadError(error, 'Unable to load regions.');
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
    this.resetList();

    if (error instanceof HttpErrorResponse) {
      if (error.status === 401) {
        this.errorMessage = 'Sign in with a Territory Manager account to view regions.';
        this.notifyStateChanged();
        return;
      }

      if (error.status === 403) {
        this.errorMessage = 'This account is not authorized for the selected territory.';
        this.notifyStateChanged();
        return;
      }
    }

    this.errorMessage = fallback;
    this.notifyStateChanged();
  }

  private saveListState(): void {
    this.svc.saveListState({
      searchQuery: this.searchQuery,
      sort: this.currentSort,
      loadedCount: this.items.length,
      scrollTop: this.scrollContainer?.nativeElement?.scrollTop ?? 0,
    });
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
}
