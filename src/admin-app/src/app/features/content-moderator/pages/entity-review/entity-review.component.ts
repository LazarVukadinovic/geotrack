import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  HostListener,
  OnDestroy,
  OnInit,
  inject,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { Observable, Subject, finalize } from 'rxjs';
import { debounceTime, distinctUntilChanged, takeUntil } from 'rxjs/operators';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { ContentModeratorEntityReviewService } from '../../../../core/services/content-moderator/content-moderator-entity-review.service';
import { ContentModeratorDashboardService } from '../../../../core/services/content-moderator/content-moderator-dashboard.service';
import { UserAppLinkService } from '../../../../core/services/user-app-link.service';
import {
  ContentModeratorEntityReviewCounts,
  ContentModeratorEntityReviewDetail,
  ContentModeratorEntityReviewListItem,
  ContentModeratorEntitySort,
  ContentModeratorEntityStatusFilter,
  ContentModeratorEntityType,
} from '../../../../core/models/content-moderator/content-moderator-entity-review.model';

const PAGE_SIZE = 25;

type EntityAction = 'suspend' | 'unsuspend' | 'delete';
type EntityActionTarget = ContentModeratorEntityReviewListItem | ContentModeratorEntityReviewDetail;

interface EntityConfig {
  type: ContentModeratorEntityType;
  pluralLabel: string;
  singularLabel: string;
  title: string;
  subtitle: string;
  searchPlaceholder: string;
  icon: string;
}

interface ConfirmationState {
  action: EntityAction;
  entity: EntityActionTarget;
}

const ENTITY_CONFIGS: Record<ContentModeratorEntityType, EntityConfig> = {
  destination: {
    type: 'destination',
    pluralLabel: 'destinations',
    singularLabel: 'destination',
    title: 'Review Destinations',
    subtitle: 'Review and moderate destination content across the platform.',
    searchPlaceholder: 'Search by name, description, or region...',
    icon: 'flag',
  },
  location: {
    type: 'location',
    pluralLabel: 'locations',
    singularLabel: 'location',
    title: 'Review Locations',
    subtitle: 'Review and moderate location content across the platform.',
    searchPlaceholder: 'Search by name, description, destination, or region...',
    icon: 'pin',
  },
  place: {
    type: 'place',
    pluralLabel: 'places',
    singularLabel: 'place',
    title: 'Review Places',
    subtitle: 'Review and moderate place content across the platform.',
    searchPlaceholder: 'Search by name, description, address, or region...',
    icon: 'home',
  },
  event: {
    type: 'event',
    pluralLabel: 'events',
    singularLabel: 'event',
    title: 'Review Events',
    subtitle: 'Review and moderate event content across the platform.',
    searchPlaceholder: 'Search by name, description, venue, or region...',
    icon: 'calendar',
  },
};

@Component({
  selector: 'app-content-moderator-entity-review',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './entity-review.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ContentModeratorEntityReviewComponent implements OnInit, OnDestroy {
  private readonly entityReviewSvc = inject(ContentModeratorEntityReviewService);
  private readonly dashboardSvc = inject(ContentModeratorDashboardService);
  private readonly userAppLinks = inject(UserAppLinkService);
  private readonly route = inject(ActivatedRoute);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly destroy$ = new Subject<void>();
  private readonly searchSubject = new Subject<string>();

  private loadSequence = 0;
  private detailLoadSequence = 0;
  private successTimer: ReturnType<typeof setTimeout> | null = null;

  readonly pageSize = PAGE_SIZE;
  readonly statusOptions: { label: string; value: ContentModeratorEntityStatusFilter }[] = [
    { label: 'All statuses', value: 'all' },
    { label: 'Active', value: 'active' },
    { label: 'Suspended', value: 'suspended' },
    { label: 'Deleted', value: 'deleted' },
  ];
  readonly sortOptions: { label: string; value: ContentModeratorEntitySort }[] = [
    { label: 'Newest First', value: 'newest' },
    { label: 'Oldest First', value: 'oldest' },
    { label: 'Name A-Z', value: 'nameAsc' },
    { label: 'Name Z-A', value: 'nameDesc' },
    { label: 'Status', value: 'status' },
  ];

  entityType: ContentModeratorEntityType = 'destination';
  entities: ContentModeratorEntityReviewListItem[] = [];
  counts: ContentModeratorEntityReviewCounts = {
    allCount: 0,
    activeCount: 0,
    suspendedCount: 0,
    deletedCount: 0,
  };
  totalCount = 0;
  nextSkip: number | null = null;
  hasMore = false;
  pendingReviewReportsCount = 0;

  isLoading = false;
  isLoadingMore = false;
  errorMessage = '';
  successMessage = '';

  searchQuery = '';
  statusFilter: ContentModeratorEntityStatusFilter = 'all';
  sort: ContentModeratorEntitySort = 'newest';

  openDropdownId: string | null = null;

  detailTarget: ContentModeratorEntityReviewListItem | null = null;
  detailEntity: ContentModeratorEntityReviewDetail | null = null;
  isDetailLoading = false;
  detailErrorMessage = '';

  confirmation: ConfirmationState | null = null;
  moderationReason = '';
  isActionSaving = false;

  ngOnInit(): void {
    this.loadPendingReportsBadge();

    this.route.data.pipe(takeUntil(this.destroy$)).subscribe(data => {
      this.entityType = this.normalizeEntityType(data['entityType']);
      this.resetAndLoad();
    });

    this.searchSubject.pipe(
      debounceTime(300),
      distinctUntilChanged(),
      takeUntil(this.destroy$),
    ).subscribe(() => this.resetAndLoad());
  }

  ngOnDestroy(): void {
    if (this.successTimer) {
      clearTimeout(this.successTimer);
    }

    this.destroy$.next();
    this.destroy$.complete();
  }

  @HostListener('document:click')
  onDocumentClick(): void {
    if (this.openDropdownId) {
      this.openDropdownId = null;
      this.cdr.markForCheck();
    }
  }

  @HostListener('document:keydown.escape')
  onEscapeKey(): void {
    if (this.confirmation && !this.isActionSaving) {
      this.closeConfirmation();
      return;
    }

    if (this.detailTarget && !this.isActionSaving) {
      this.closeDetail();
      return;
    }

    if (this.openDropdownId) {
      this.openDropdownId = null;
      this.cdr.markForCheck();
    }
  }

  get config(): EntityConfig {
    return ENTITY_CONFIGS[this.entityType];
  }

  get currentSortLabel(): string {
    return this.sortOptions.find(option => option.value === this.sort)?.label ?? 'Newest First';
  }

  get currentStatusLabel(): string {
    return this.statusOptions.find(option => option.value === this.statusFilter)?.label ?? 'All statuses';
  }

  get shownCount(): number {
    return this.entities.length;
  }

  onSearchChange(): void {
    this.searchSubject.next(this.searchQuery);
  }

  setStatusFilter(value: string): void {
    const normalized = this.normalizeStatusFilter(value);
    if (this.statusFilter === normalized) {
      return;
    }

    this.statusFilter = normalized;
    this.resetAndLoad();
  }

  setSort(value: string): void {
    const normalized = this.normalizeSort(value);
    if (this.sort === normalized) {
      return;
    }

    this.sort = normalized;
    this.resetAndLoad();
  }

  retry(): void {
    this.resetAndLoad();
  }

  loadMore(): void {
    if (!this.hasMore || this.isLoadingMore || this.nextSkip === null) {
      return;
    }

    this.loadEntities(false);
  }

  openDetail(entity: ContentModeratorEntityReviewListItem, event?: Event): void {
    event?.stopPropagation();
    this.openDropdownId = null;
    this.detailTarget = entity;
    this.detailEntity = null;
    this.detailErrorMessage = '';
    this.loadDetail(entity.entityId);
    this.cdr.markForCheck();
  }

  closeDetail(): void {
    if (this.isActionSaving) {
      return;
    }

    this.detailLoadSequence++;
    this.detailTarget = null;
    this.detailEntity = null;
    this.isDetailLoading = false;
    this.detailErrorMessage = '';
    this.cdr.markForCheck();
  }

  retryDetail(): void {
    if (!this.detailTarget) {
      return;
    }

    this.loadDetail(this.detailTarget.entityId);
  }

  toggleDropdown(entity: ContentModeratorEntityReviewListItem, event: MouseEvent): void {
    event.stopPropagation();
    const key = this.entityKey(entity);
    this.openDropdownId = this.openDropdownId === key ? null : key;
    this.cdr.markForCheck();
  }

  openConfirmation(action: EntityAction, entity: EntityActionTarget, event?: Event): void {
    event?.stopPropagation();
    this.openDropdownId = null;
    this.confirmation = { action, entity };
    this.moderationReason = '';
    this.cdr.markForCheck();
  }

  closeConfirmation(): void {
    if (this.isActionSaving) {
      return;
    }

    this.confirmation = null;
    this.moderationReason = '';
    this.cdr.markForCheck();
  }

  confirmAction(): void {
    const state = this.confirmation;
    if (!state) {
      return;
    }

    this.isActionSaving = true;
    this.errorMessage = '';
    this.detailErrorMessage = '';

    this.actionRequest(state).pipe(
      takeUntil(this.destroy$),
      finalize(() => {
        this.isActionSaving = false;
        this.cdr.markForCheck();
      }),
    ).subscribe({
      next: updated => {
        this.confirmation = null;
        this.moderationReason = '';
        this.showSuccess(this.actionSuccessMessage(state.action, state.entity));

        if (state.action === 'delete') {
          this.closeDetailIfOpen(state.entity);
        } else {
          this.refreshDetailIfOpen(updated);
        }

        this.resetAndLoad();
      },
      error: error => {
        const message = this.resolveErrorMessage(error);
        this.errorMessage = message;
        if (this.detailTarget?.entityId === state.entity.entityId) {
          this.detailErrorMessage = message;
        }
      },
    });
  }

  canShowActions(entity: EntityActionTarget): boolean {
    return entity.permissions.canSuspend ||
      entity.permissions.canUnsuspend ||
      entity.permissions.canDelete;
  }

  hierarchy(entity: EntityActionTarget): string {
    const parts = [
      entity.territoryName,
      entity.regionName,
      entity.destinationName,
      entity.locationName,
      entity.placeName,
    ].filter((value): value is string => Boolean(value));

    return parts.length > 0 ? parts.join(' / ') : 'No hierarchy assigned';
  }

  metadataRows(detail: ContentModeratorEntityReviewDetail): { label: string; value: string }[] {
    const rows: { label: string; value: string }[] = [];
    const metadata = detail.metadata;

    if (metadata.address) rows.push({ label: 'Address', value: metadata.address });
    if (metadata.venueName) rows.push({ label: 'Venue', value: metadata.venueName });
    if (metadata.parentEntityType) rows.push({ label: 'Parent type', value: metadata.parentEntityType });
    if (metadata.ticketPrice) rows.push({ label: 'Ticket', value: metadata.ticketPrice });
    if (metadata.isFree !== null) rows.push({ label: 'Free', value: metadata.isFree ? 'Yes' : 'No' });
    if (metadata.isAllDay !== null) rows.push({ label: 'All day', value: metadata.isAllDay ? 'Yes' : 'No' });
    if (metadata.isRecurring !== null) rows.push({ label: 'Recurring', value: metadata.isRecurring ? 'Yes' : 'No' });
    if (metadata.contactEmail) rows.push({ label: 'Contact email', value: metadata.contactEmail });
    if (metadata.phoneNumber) rows.push({ label: 'Phone', value: metadata.phoneNumber });
    if (metadata.website) rows.push({ label: 'Website', value: metadata.website });
    if (metadata.boundaryShapeType) rows.push({ label: 'Boundary', value: metadata.boundaryShapeType });
    if (metadata.boundaryNote) rows.push({ label: 'Boundary note', value: metadata.boundaryNote });
    if (typeof metadata.latitude === 'number' && typeof metadata.longitude === 'number') {
      rows.push({ label: 'Coordinates', value: `${metadata.latitude.toFixed(5)}, ${metadata.longitude.toFixed(5)}` });
    }

    return rows;
  }

  contentCountEntries(detail: ContentModeratorEntityReviewDetail): { label: string; value: number }[] {
    return Object.entries(detail.metadata.contentCounts ?? {})
      .map(([label, value]) => ({ label, value }))
      .filter(entry => Number.isFinite(entry.value));
  }

  actionTitle(action: EntityAction): string {
    switch (action) {
      case 'suspend':
        return `Suspend ${this.config.singularLabel}`;
      case 'unsuspend':
        return `Unsuspend ${this.config.singularLabel}`;
      case 'delete':
        return `Delete ${this.config.singularLabel}`;
    }
  }

  actionBody(action: EntityAction): string {
    switch (action) {
      case 'suspend':
        return 'This will hide this entity from public views until it is restored.';
      case 'unsuspend':
        return 'This will restore public visibility for this entity.';
      case 'delete':
        return 'This will remove this entity from public views. This action may affect related content.';
    }
  }

  actionButtonLabel(action: EntityAction): string {
    if (this.isActionSaving) {
      return 'Saving...';
    }

    switch (action) {
      case 'suspend':
        return 'Suspend';
      case 'unsuspend':
        return 'Unsuspend';
      case 'delete':
        return 'Delete';
    }
  }

  statusClass(status: string): string {
    if (status === 'Suspended') {
      return 'bg-amber-50 text-amber-700 ring-1 ring-amber-200';
    }
    if (status === 'Deleted') {
      return 'bg-gray-100 text-gray-600 ring-1 ring-gray-200';
    }
    if (status === 'Active') {
      return 'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200';
    }

    return 'bg-blue-50 text-blue-700 ring-1 ring-blue-200';
  }

  iconClass(entityType = this.entityType): string {
    switch (entityType) {
      case 'destination':
        return 'bg-blue-50 text-blue-600';
      case 'location':
        return 'bg-cyan-50 text-cyan-700';
      case 'place':
        return 'bg-emerald-50 text-emerald-700';
      case 'event':
        return 'bg-amber-50 text-amber-700';
    }
  }

  formatDate(value: string | null | undefined): string {
    return this.entityReviewSvc.formatDate(value);
  }

  formatDatetime(value: string | null | undefined): string {
    return this.entityReviewSvc.formatDatetime(value);
  }

  entityKey(entity: EntityActionTarget): string {
    return `${entity.entityType}:${entity.entityId}`;
  }

  publicPreviewUrl(entity: EntityActionTarget): string | null {
    return entity.canOpenInUserApp
      ? this.userAppLinks.toUserAppUrl(entity.publicDetailRoute)
      : null;
  }

  publicPreviewAriaLabel(entity: EntityActionTarget): string {
    return `Open ${entity.name || this.config.singularLabel} ${this.config.singularLabel} in user app`;
  }

  trackByEntity(_: number, entity: ContentModeratorEntityReviewListItem): string {
    return this.entityKey(entity);
  }

  private resetAndLoad(): void {
    this.nextSkip = null;
    this.hasMore = false;
    this.entities = [];
    this.loadEntities(true);
  }

  private loadEntities(reset: boolean): void {
    const skip = reset ? 0 : this.nextSkip ?? this.entities.length;
    const sequence = ++this.loadSequence;
    this.errorMessage = '';

    if (reset) {
      this.isLoading = true;
    } else {
      this.isLoadingMore = true;
    }

    this.entityReviewSvc.getPage$({
      type: this.entityType,
      search: this.searchQuery,
      status: this.statusFilter,
      sort: this.sort,
      skip,
      take: PAGE_SIZE,
    }).pipe(
      takeUntil(this.destroy$),
      finalize(() => {
        if (sequence === this.loadSequence) {
          this.isLoading = false;
          this.isLoadingMore = false;
          this.cdr.markForCheck();
        }
      }),
    ).subscribe({
      next: page => {
        if (sequence !== this.loadSequence) {
          return;
        }

        this.entities = reset ? page.items : [...this.entities, ...page.items];
        this.totalCount = page.totalCount;
        this.nextSkip = page.nextSkip;
        this.hasMore = page.hasMore;
        this.counts = page.counts;
      },
      error: error => {
        if (sequence !== this.loadSequence) {
          return;
        }

        this.errorMessage = this.resolveErrorMessage(error);
      },
    });
  }

  private loadDetail(entityId: number, showLoading = true): void {
    const sequence = ++this.detailLoadSequence;
    this.detailErrorMessage = '';

    if (showLoading) {
      this.isDetailLoading = true;
    }

    this.entityReviewSvc.getDetail$(this.entityType, entityId).pipe(
      takeUntil(this.destroy$),
      finalize(() => {
        if (sequence === this.detailLoadSequence) {
          this.isDetailLoading = false;
          this.cdr.markForCheck();
        }
      }),
    ).subscribe({
      next: detail => {
        if (sequence !== this.detailLoadSequence) {
          return;
        }

        this.detailEntity = detail;
      },
      error: error => {
        if (sequence !== this.detailLoadSequence) {
          return;
        }

        this.detailErrorMessage = this.resolveErrorMessage(error);
      },
    });
  }

  private actionRequest(state: ConfirmationState): Observable<ContentModeratorEntityReviewDetail> {
    const request = {
      reason: this.moderationReason.trim() || undefined,
    };

    switch (state.action) {
      case 'suspend':
        return this.entityReviewSvc.suspend$(state.entity.entityType, state.entity.entityId, request);
      case 'unsuspend':
        return this.entityReviewSvc.unsuspend$(state.entity.entityType, state.entity.entityId, request);
      case 'delete':
        return this.entityReviewSvc.delete$(state.entity.entityType, state.entity.entityId, request);
    }
  }

  private refreshDetailIfOpen(updated: ContentModeratorEntityReviewDetail): void {
    if (this.detailTarget?.entityId === updated.entityId && this.detailTarget.entityType === updated.entityType) {
      this.detailTarget = updated;
      this.detailEntity = updated;
    }
  }

  private closeDetailIfOpen(entity: EntityActionTarget): void {
    if (this.detailTarget?.entityId === entity.entityId && this.detailTarget.entityType === entity.entityType) {
      this.closeDetail();
    }
  }

  private actionSuccessMessage(action: EntityAction, entity: EntityActionTarget): string {
    const name = entity.name || this.config.singularLabel;
    switch (action) {
      case 'suspend':
        return `${name} suspended.`;
      case 'unsuspend':
        return `${name} restored to public visibility.`;
      case 'delete':
        return `${name} removed from public visibility.`;
    }
  }

  private showSuccess(message: string): void {
    this.successMessage = message;
    if (this.successTimer) {
      clearTimeout(this.successTimer);
    }

    this.successTimer = setTimeout(() => {
      this.successMessage = '';
      this.cdr.markForCheck();
    }, 3500);
  }

  private loadPendingReportsBadge(): void {
    this.dashboardSvc.loadDashboard()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: dashboard => {
          this.pendingReviewReportsCount = dashboard.badges.pendingReviewReportsCount;
          this.cdr.markForCheck();
        },
        error: () => {
          this.pendingReviewReportsCount = 0;
          this.cdr.markForCheck();
        },
      });
  }

  private normalizeEntityType(value: unknown): ContentModeratorEntityType {
    return value === 'location' || value === 'place' || value === 'event'
      ? value
      : 'destination';
  }

  private normalizeStatusFilter(value: string): ContentModeratorEntityStatusFilter {
    return value === 'active' || value === 'suspended' || value === 'deleted'
      ? value
      : 'all';
  }

  private normalizeSort(value: string): ContentModeratorEntitySort {
    if (value === 'oldest' || value === 'nameAsc' || value === 'nameDesc' || value === 'status') {
      return value;
    }

    return 'newest';
  }

  private resolveErrorMessage(error: unknown): string {
    if (typeof error === 'object' && error !== null) {
      const candidate = error as { error?: { message?: string } | string; message?: string };
      if (typeof candidate.error === 'string' && candidate.error.trim()) {
        return candidate.error;
      }
      if (candidate.error && typeof candidate.error === 'object' && candidate.error.message?.trim()) {
        return candidate.error.message;
      }
      if (candidate.message?.trim()) {
        return candidate.message;
      }
    }

    return 'Something went wrong. Please try again.';
  }
}
