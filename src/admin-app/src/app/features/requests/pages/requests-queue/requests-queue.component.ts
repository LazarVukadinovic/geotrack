import { ChangeDetectorRef, Component, HostListener, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { finalize, Subscription } from 'rxjs';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { DashboardService } from '../../../../core/services/dashboard.service';
import { AuthService } from '../../../../core/services/auth.service';
import { ReviewRequestsService } from '../../../../core/services/review-requests.service';
import { RegionalManagerSidebarStateService } from '../../../../core/services/regional-manager-sidebar-state.service';
import {
  RegionalRequestQueueResponse,
  RegionalRequestQueueCountsByType,
  RegionalRequestQueueItem,
  RequestEntityType,
  RequestQueueSort,
  RequestQueueStatusFilter,
  RequestQueueTypeFilter,
  RequestStatus,
} from '../../../../core/models/review-request.model';
import { RegionalAssignedRegion } from '../../../../core/models/dashboard.model';

type QueueTab =
  | 'all'
  | 'destinations'
  | 'locations'
  | 'places'
  | 'events'
  | 'approved'
  | 'rejected';

const EMPTY_COUNTS: RegionalRequestQueueCountsByType = {
  destinations: 0,
  locations: 0,
  places: 0,
  events: 0,
};

const TAB_QUERY_MAP: Record<QueueTab, { type: RequestQueueTypeFilter; status: RequestQueueStatusFilter }> = {
  all: { type: 'all', status: 'pending' },
  destinations: { type: 'destination', status: 'pending' },
  locations: { type: 'location', status: 'pending' },
  places: { type: 'place', status: 'pending' },
  events: { type: 'event', status: 'pending' },
  approved: { type: 'all', status: 'approved' },
  rejected: { type: 'all', status: 'rejected' },
};

interface Tab {
  key: QueueTab;
  label: string;
  count: number | null;
}

interface SortOption {
  value: RequestQueueSort;
  label: string;
}

@Component({
  selector: 'app-review-queue',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './requests-queue.component.html',
})
export class ReviewQueueComponent implements OnInit, OnDestroy {
  private static readonly activeRegionStorageKey = 'geotrack_admin_active_region_id';

  assignedRegions: RegionalAssignedRegion[] = [];
  activeRegionId: number | null = null;
  queue: RegionalRequestQueueResponse | null = null;
  regionLoading = false;
  loadingQueue = false;
  actionLoading = false;
  errorMessage: string | null = null;
  feedbackMessage: string | null = null;
  authRequired = false;

  activeTab: QueueTab = 'all';
  searchQuery = '';
  sortOpen = false;
  currentSort: RequestQueueSort = 'newest';
  readonly sortOptions: SortOption[] = [
    { value: 'newest', label: 'Newest First' },
    { value: 'oldest', label: 'Oldest First' },
    { value: 'status', label: 'Status' },
    { value: 'type', label: 'Type' },
  ];

  selectedIds = new Set<string>();
  processingIds = new Set<string>();
  rejectModal: { requestKeys: string[]; item?: RegionalRequestQueueItem } | null = null;
  rejectReason = '';
  rejectNote = '';

  pageSize = 10;
  skip = 0;

  private searchDebounceHandle: ReturnType<typeof setTimeout> | null = null;
  private queueRequestSequence = 0;
  private queueSubscription: Subscription | null = null;
  private lastCountsByType: RegionalRequestQueueCountsByType | null = null;
  private lastPendingCount: number | null = null;
  private lastApprovedCount: number | null = null;
  private lastRejectedCount: number | null = null;
  private readonly failedThumbnailKeys = new Set<string>();

  constructor(
    private readonly router: Router,
    private readonly route: ActivatedRoute,
    private readonly auth: AuthService,
    private readonly dashboardService: DashboardService,
    private readonly requestsService: ReviewRequestsService,
    private readonly sidebarState: RegionalManagerSidebarStateService,
    private readonly cdr: ChangeDetectorRef,
  ) {}

  ngOnInit(): void {
    this.applyNavigationFeedback();
    this.loadAssignedRegions();
  }

  ngOnDestroy(): void {
    this.queueRequestSequence++;
    this.queueSubscription?.unsubscribe();

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

  get requests(): RegionalRequestQueueItem[] {
    return this.queue?.items ?? [];
  }

  get loading(): boolean {
    return this.loadingQueue;
  }

  get tabs(): Tab[] {
    const counts = this.queue?.countsByType ?? this.lastCountsByType;
    return [
      { key: 'all', label: 'All Requests', count: this.queue?.pendingCount ?? this.lastPendingCount },
      { key: 'destinations', label: 'Destinations', count: counts?.destinations ?? 0 },
      { key: 'locations', label: 'Locations', count: counts?.locations ?? 0 },
      { key: 'places', label: 'Places', count: counts?.places ?? 0 },
      { key: 'events', label: 'Events', count: counts?.events ?? 0 },
      { key: 'approved', label: 'Approved', count: this.queue?.approvedCount ?? this.lastApprovedCount },
      { key: 'rejected', label: 'Rejected', count: this.queue?.rejectedCount ?? this.lastRejectedCount },
    ];
  }

  get pendingVisibleRequests(): RegionalRequestQueueItem[] {
    return this.requests.filter(request => request.status === 'Pending');
  }

  get allSelected(): boolean {
    const selectable = this.pendingVisibleRequests;
    return selectable.length > 0 && selectable.every(request => this.selectedIds.has(request.requestKey));
  }

  get selectedPendingCount(): number {
    return this.pendingVisibleRequests.filter(request => this.selectedIds.has(request.requestKey)).length;
  }

  get currentSortLabel(): string {
    return this.sortOptions.find(option => option.value === this.currentSort)?.label ?? 'Newest First';
  }

  get totalForCurrentView(): number {
    return this.queue?.totalCount ?? 0;
  }

  get showingStart(): number {
    return this.totalForCurrentView === 0 ? 0 : (this.queue?.skip ?? this.skip) + 1;
  }

  get showingEnd(): number {
    return Math.min((this.queue?.skip ?? this.skip) + this.requests.length, this.totalForCurrentView);
  }

  get canGoPrevious(): boolean {
    return this.skip > 0 && !this.loadingQueue;
  }

  get canGoNext(): boolean {
    return (this.queue?.skip ?? this.skip) + this.requests.length < this.totalForCurrentView && !this.loadingQueue;
  }

  get controlsBusy(): boolean {
    return this.loadingQueue || this.actionLoading;
  }

  get waitingForRegion(): boolean {
    return this.regionLoading && !this.activeRegionId;
  }

  get emptyMessage(): string {
    if (!this.activeRegionId) return 'No active assigned region.';
    return 'No requests found for this filter.';
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
            this.queue = null;
            this.loadingQueue = false;
            this.resetCountSnapshot();
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
          this.loadQueue();
        },
        error: error => this.handleLoadError(error, 'Unable to load assigned regions.'),
      });
  }

  onRegionSelected(regionId: number): void {
    if (regionId === this.activeRegionId) return;
    this.setActiveRegion(regionId, true);
    this.activeTab = 'all';
    this.searchQuery = '';
    this.skip = 0;
    this.resetCountSnapshot();
    this.clearSelectionState();
    this.loadQueue();
    this.notifyStateChanged();
  }

  selectTab(tab: QueueTab): void {
    if (tab === this.activeTab) return;
    this.activeTab = tab;
    this.skip = 0;
    this.clearSelectionState();
    this.loadQueue();
    this.notifyStateChanged();
  }

  onSearchChange(): void {
    if (this.searchDebounceHandle) {
      clearTimeout(this.searchDebounceHandle);
    }

    this.searchDebounceHandle = setTimeout(() => {
      this.skip = 0;
      this.clearSelectionState();
      this.loadQueue();
      this.notifyStateChanged();
    }, 250);
  }

  setSort(sort: RequestQueueSort): void {
    this.currentSort = sort;
    this.sortOpen = false;
    this.skip = 0;
    this.clearSelectionState();
    this.loadQueue();
    this.notifyStateChanged();
  }

  toggleSelectAll(event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    if (checked) {
      this.pendingVisibleRequests.forEach(request => this.selectedIds.add(request.requestKey));
    } else {
      this.pendingVisibleRequests.forEach(request => this.selectedIds.delete(request.requestKey));
    }
    this.notifyStateChanged();
  }

  toggleSelect(request: RegionalRequestQueueItem, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    if (checked) this.selectedIds.add(request.requestKey);
    else this.selectedIds.delete(request.requestKey);
    this.notifyStateChanged();
  }

  approveSelected(): void {
    const requestKeys = this.selectedPendingRequestKeys();
    if (!this.activeRegionId || requestKeys.length === 0) return;

    this.actionLoading = true;
    this.notifyStateChanged();
    this.requestsService
      .bulkApprove(this.activeRegionId, requestKeys)
      .pipe(
        finalize(() => {
          this.actionLoading = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: result => {
          this.feedbackMessage = `${result.succeededCount} request${result.succeededCount === 1 ? '' : 's'} approved.`;
          this.selectedIds.clear();
          this.refreshSidebarBadge();
          this.loadQueue();
        },
        error: error => this.handleActionError(error),
      });
  }

  openRejectSelected(): void {
    const requestKeys = this.selectedPendingRequestKeys();
    if (requestKeys.length === 0) return;
    this.rejectReason = '';
    this.rejectNote = '';
    this.rejectModal = { requestKeys };
    this.notifyStateChanged();
  }

  approveRequest(request: RegionalRequestQueueItem): void {
    if (!this.activeRegionId || request.status !== 'Pending' || this.isProcessing(request)) return;

    this.setProcessing(request, true);
    this.requestsService
      .approve(this.activeRegionId, request)
      .pipe(finalize(() => this.setProcessing(request, false)))
      .subscribe({
        next: () => {
          this.feedbackMessage = 'Request approved.';
          this.selectedIds.delete(request.requestKey);
          this.refreshSidebarBadge();
          this.loadQueue();
          this.notifyStateChanged();
        },
        error: error => this.handleActionError(error),
      });
  }

  openRejectRequest(request: RegionalRequestQueueItem): void {
    if (request.status !== 'Pending' || this.isProcessing(request)) return;
    this.rejectReason = '';
    this.rejectNote = '';
    this.rejectModal = { requestKeys: [request.requestKey], item: request };
    this.notifyStateChanged();
  }

  confirmReject(): void {
    if (!this.activeRegionId || !this.rejectModal) return;

    const modal = this.rejectModal;
    const reason = this.rejectReason;
    const note = this.rejectNote;
    this.rejectModal = null;
    this.notifyStateChanged();

    if (modal.item) {
      this.setProcessing(modal.item, true);
      this.requestsService
        .reject(this.activeRegionId, modal.item, reason, note)
        .pipe(finalize(() => this.setProcessing(modal.item!, false)))
        .subscribe({
          next: () => {
            this.feedbackMessage = 'Request rejected.';
            this.selectedIds.delete(modal.item!.requestKey);
            this.refreshSidebarBadge();
            this.loadQueue();
            this.notifyStateChanged();
          },
          error: error => this.handleActionError(error),
        });
      return;
    }

    this.actionLoading = true;
    this.notifyStateChanged();
    this.requestsService
      .bulkReject(this.activeRegionId, modal.requestKeys, reason, note)
      .pipe(
        finalize(() => {
          this.actionLoading = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: result => {
          this.feedbackMessage = `${result.succeededCount} request${result.succeededCount === 1 ? '' : 's'} rejected.`;
          this.selectedIds.clear();
          this.refreshSidebarBadge();
          this.loadQueue();
        },
        error: error => this.handleActionError(error),
      });
  }

  cancelReject(): void {
    this.rejectModal = null;
    this.rejectReason = '';
    this.rejectNote = '';
    this.notifyStateChanged();
  }

  previewRequest(request: RegionalRequestQueueItem): void {
    if (!this.activeRegionId) return;
    this.router.navigate(['/request/detail', 'contribution', request.requestId], {
      queryParams: { regionId: this.activeRegionId },
    });
  }

  previousPage(): void {
    if (!this.canGoPrevious) return;
    this.skip = Math.max(0, this.skip - this.pageSize);
    this.clearSelectionState();
    this.loadQueue();
    this.notifyStateChanged();
  }

  nextPage(): void {
    if (!this.canGoNext) return;
    this.skip += this.pageSize;
    this.clearSelectionState();
    this.loadQueue();
    this.notifyStateChanged();
  }

  isProcessing(request: RegionalRequestQueueItem): boolean {
    return this.processingIds.has(request.requestKey);
  }

  statusClass(status: RequestStatus): string {
    switch (status) {
      case 'Approved':
        return 'text-blue-600';
      case 'Rejected':
        return 'text-red-500';
      default:
        return 'text-amber-500';
    }
  }

  typeBadgeClass(type: RequestEntityType): string {
    const map: Record<string, string> = {
      Destination: 'bg-purple-50 text-purple-700 border border-purple-200',
      Location: 'bg-indigo-50 text-indigo-700 border border-indigo-200',
      Place: 'bg-blue-50 text-blue-700 border border-blue-200',
      Event: 'bg-yellow-50 text-yellow-700 border border-yellow-200',
    };
    return map[type] ?? 'bg-gray-100 text-gray-600';
  }

  hasThumbnail(request: RegionalRequestQueueItem): boolean {
    return Boolean(request.thumbnailImageUrl) && !this.failedThumbnailKeys.has(this.thumbnailKey(request));
  }

  onThumbnailError(request: RegionalRequestQueueItem): void {
    this.failedThumbnailKeys.add(this.thumbnailKey(request));
    this.notifyStateChanged();
  }

  @HostListener('document:click')
  onDocumentClick(): void {
    if (this.sortOpen) {
      this.sortOpen = false;
      this.notifyStateChanged();
    }
  }

  private loadQueue(): void {
    if (!this.activeRegionId) {
      this.queueRequestSequence++;
      this.queueSubscription?.unsubscribe();
      this.loadingQueue = false;
      this.queue = null;
      this.notifyStateChanged();
      return;
    }

    const filters = this.currentFilters();
    const requestId = ++this.queueRequestSequence;
    this.queueSubscription?.unsubscribe();
    this.loadingQueue = true;
    this.errorMessage = null;
    this.authRequired = false;
    this.queue = this.emptyQueueSnapshot();
    this.notifyStateChanged();

    this.queueSubscription = this.requestsService
      .getQueue({
        regionId: this.activeRegionId,
        type: filters.type,
        status: filters.status,
        search: this.searchQuery,
        sort: this.currentSort,
        skip: this.skip,
        take: this.pageSize,
      })
      .pipe(
        finalize(() => {
          if (this.queueRequestSequence === requestId) {
            this.loadingQueue = false;
            this.notifyStateChanged();
          }
        }),
      )
      .subscribe({
        next: queue => {
          if (this.queueRequestSequence !== requestId) return;

          try {
            this.queue = this.normalizeQueue(queue);
            this.rememberCounts(this.queue);
          } catch {
            this.loadingQueue = false;
            this.handleLoadError(null, 'Unable to load request queue.');
            return;
          }

          this.selectedIds.forEach(requestKey => {
            if (!this.pendingVisibleRequests.some(request => request.requestKey === requestKey)) {
              this.selectedIds.delete(requestKey);
            }
          });
        },
        error: error => {
          if (this.queueRequestSequence === requestId) {
            this.loadingQueue = false;
            this.handleLoadError(error, 'Unable to load request queue.');
          }
        },
      });
  }

  private currentFilters(): { type: RequestQueueTypeFilter; status: RequestQueueStatusFilter } {
    return TAB_QUERY_MAP[this.activeTab];
  }

  private normalizeQueue(queue: RegionalRequestQueueResponse): RegionalRequestQueueResponse {
    if (!queue || !Array.isArray(queue.items)) {
      throw new Error('Invalid request queue response.');
    }

    return {
      ...queue,
      items: queue.items ?? [],
      totalCount: this.toSafeCount(queue.totalCount),
      pendingCount: this.toSafeCount(queue.pendingCount),
      approvedCount: this.toSafeCount(queue.approvedCount),
      rejectedCount: this.toSafeCount(queue.rejectedCount),
      countsByType: this.normalizeCounts(queue.countsByType),
      skip: this.toSafeCount(queue.skip),
      take: this.toSafeCount(queue.take),
    };
  }

  private selectedPendingRequestKeys(): string[] {
    return this.pendingVisibleRequests
      .filter(request => this.selectedIds.has(request.requestKey))
      .map(request => request.requestKey);
  }

  private refreshSidebarBadge(): void {
    if (this.activeRegionId) {
      this.sidebarState.refresh(this.activeRegionId);
    }
  }

  private applyNavigationFeedback(): void {
    const state = history.state as { feedbackMessage?: unknown } | null;
    const feedbackMessage = typeof state?.feedbackMessage === 'string'
      ? state.feedbackMessage.trim()
      : '';

    if (feedbackMessage) {
      this.feedbackMessage = feedbackMessage;
    }
  }

  private setProcessing(request: RegionalRequestQueueItem, value: boolean): void {
    if (value) this.processingIds.add(request.requestKey);
    else this.processingIds.delete(request.requestKey);
    this.notifyStateChanged();
  }

  private clearSelectionState(): void {
    this.selectedIds.clear();
    this.rejectModal = null;
    this.rejectReason = '';
    this.rejectNote = '';
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
    this.notifyStateChanged();
  }

  private handleLoadError(error: unknown, fallback: string): void {
    this.loadingQueue = false;
    this.queue = this.emptyQueueSnapshot();
    if (error instanceof HttpErrorResponse) {
      if (error.status === 401) {
        this.authRequired = true;
        this.errorMessage = 'Sign in with an authenticated Regional Manager account to view the queue.';
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

  private emptyQueueSnapshot(): RegionalRequestQueueResponse {
    return {
      items: [],
      totalCount: 0,
      pendingCount: this.lastPendingCount ?? 0,
      approvedCount: this.lastApprovedCount ?? 0,
      rejectedCount: this.lastRejectedCount ?? 0,
      countsByType: this.lastCountsByType ?? { ...EMPTY_COUNTS },
      skip: this.skip,
      take: this.pageSize,
    };
  }

  private rememberCounts(queue: RegionalRequestQueueResponse): void {
    this.lastCountsByType = { ...queue.countsByType };
    this.lastPendingCount = queue.pendingCount;
    this.lastApprovedCount = queue.approvedCount;
    this.lastRejectedCount = queue.rejectedCount;
  }

  private resetCountSnapshot(): void {
    this.lastCountsByType = null;
    this.lastPendingCount = null;
    this.lastApprovedCount = null;
    this.lastRejectedCount = null;
  }

  private normalizeCounts(counts: RegionalRequestQueueCountsByType | null | undefined): RegionalRequestQueueCountsByType {
    return {
      destinations: this.toSafeCount(counts?.destinations),
      locations: this.toSafeCount(counts?.locations),
      places: this.toSafeCount(counts?.places),
      events: this.toSafeCount(counts?.events),
    };
  }

  private toSafeCount(value: unknown): number {
    return typeof value === 'number' && Number.isFinite(value) && value >= 0 ? value : 0;
  }

  private thumbnailKey(request: RegionalRequestQueueItem): string {
    return `${request.requestKey}|${request.thumbnailImageUrl ?? ''}`;
  }

  private handleActionError(error: unknown): void {
    if (error instanceof HttpErrorResponse) {
      this.errorMessage = error.error?.message ?? 'Unable to process this request.';
      this.notifyStateChanged();
      return;
    }

    this.errorMessage = 'Unable to process this request.';
    this.notifyStateChanged();
  }

  private notifyStateChanged(): void {
    this.cdr.markForCheck();
  }

  private readPersistedRegionId(): number | null {
    try {
      const value = sessionStorage.getItem(ReviewQueueComponent.activeRegionStorageKey);
      const parsed = value ? Number(value) : NaN;
      return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
    } catch {
      return null;
    }
  }

  private persistRegion(regionId: number): void {
    try {
      sessionStorage.setItem(ReviewQueueComponent.activeRegionStorageKey, String(regionId));
    } catch {
      // Session persistence is a convenience only.
    }
  }

  private clearPersistedRegion(): void {
    try {
      sessionStorage.removeItem(ReviewQueueComponent.activeRegionStorageKey);
    } catch {
      // Session persistence is a convenience only.
    }
  }
}
