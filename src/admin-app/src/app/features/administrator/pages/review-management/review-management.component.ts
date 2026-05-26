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
import { ActivatedRoute, Router } from '@angular/router';
import { Observable, Subject, finalize } from 'rxjs';
import { debounceTime, distinctUntilChanged, takeUntil } from 'rxjs/operators';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { AdminReviewsService } from '../../../../core/services/administrator/admin-reviews.service';
import { AppUsersService } from '../../../../core/services/administrator/app-users.service';
import { AdminDashboardService } from '../../../../core/services/administrator/admin-dashboard.service';
import {
  AdminReviewActionResult,
  AdminReviewDetail,
  AdminReviewListCounts,
  AdminReviewListItem,
  AdminReviewSort,
  AdminReviewStatusFilter,
  AdminReviewTypeFilter,
} from '../../../../core/models/administrator/admin-review.model';
import { AppUser } from '../../../../core/models/administrator/app-user.model';

const PAGE_SIZE = 25;

type ReviewAction =
  | 'deleteReview'
  | 'suspendAuthor'
  | 'unsuspendAuthor'
  | 'deleteAuthor'
  | 'removeAndSuspend'
  | 'removeAndDelete';

type ReviewActionTarget = AdminReviewListItem | AdminReviewDetail;

interface ConfirmationState {
  action: ReviewAction;
  review: ReviewActionTarget;
}

@Component({
  selector: 'app-review-management',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './review-management.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReviewManagementComponent implements OnInit, OnDestroy {
  private readonly reviewsSvc = inject(AdminReviewsService);
  private readonly usersSvc = inject(AppUsersService);
  private readonly dashboardSvc = inject(AdminDashboardService);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroy$ = new Subject<void>();
  private readonly searchSubject = new Subject<string>();

  private loadSequence = 0;
  private detailLoadSequence = 0;
  private successTimer: ReturnType<typeof setTimeout> | null = null;

  readonly pageSize = PAGE_SIZE;
  readonly reviewTypes: { label: string; value: AdminReviewTypeFilter }[] = [
    { label: 'Destination', value: 'destination' },
    { label: 'Location', value: 'location' },
    { label: 'Place', value: 'place' },
    { label: 'Stay', value: 'stay' },
    { label: 'Event', value: 'event' },
  ];
  readonly sortOptions: { label: string; value: AdminReviewSort }[] = [
    { label: 'Newest First', value: 'newest' },
    { label: 'Oldest First', value: 'oldest' },
    { label: 'Most Reports', value: 'reportsDesc' },
    { label: 'Lowest Rating', value: 'ratingAsc' },
    { label: 'Highest Rating', value: 'ratingDesc' },
  ];

  reviews: AdminReviewListItem[] = [];
  counts: AdminReviewListCounts = {
    reportedCount: 0,
    activeCount: 0,
    allCount: 0,
  };
  totalCount = 0;
  nextSkip: number | null = null;
  hasMore = false;
  pendingReportsCount = 0;
  supportReportsCount = 0;

  isLoading = false;
  isLoadingMore = false;
  errorMessage = '';
  successMessage = '';
  highlightKey: string | null = null;

  searchQuery = '';
  statusFilter: AdminReviewStatusFilter = 'reported';
  typeFilter: AdminReviewTypeFilter[] = [];
  sort: AdminReviewSort = 'newest';
  sortOpen = false;
  typeFilterOpen = false;

  openDropdownId: string | null = null;
  dropdownPos = { top: 0, right: 0 };

  detailTarget: AdminReviewListItem | null = null;
  detailReview: AdminReviewDetail | null = null;
  isDetailLoading = false;
  detailErrorMessage = '';

  confirmation: ConfirmationState | null = null;
  isActionSaving = false;

  ngOnInit(): void {
    this.loadPendingReportsBadge();

    this.route.queryParams.pipe(takeUntil(this.destroy$)).subscribe(params => {
      const status = params['status'];
      if (status === 'reported' || status === 'pending') {
        this.statusFilter = 'reported';
      } else if (status === 'active' || status === 'all') {
        this.statusFilter = status;
      }

      if (params['highlight']) {
        this.highlightKey = String(params['highlight']);
        this.statusFilter = 'reported';
        this.router.navigate([], {
          queryParams: { status: 'reported' },
          replaceUrl: true,
        });
      }

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
    let changed = false;

    if (this.openDropdownId) {
      this.openDropdownId = null;
      changed = true;
    }
    if (this.sortOpen) {
      this.sortOpen = false;
      changed = true;
    }
    if (this.typeFilterOpen) {
      this.typeFilterOpen = false;
      changed = true;
    }

    if (changed) {
      this.cdr.markForCheck();
    }
  }

  @HostListener('document:keydown.escape')
  onEscapeKey(): void {
    if (this.confirmation && !this.isActionSaving) {
      this.closeConfirmation();
      return;
    }

    if (this.detailTarget) {
      this.closeDetail();
      return;
    }

    if (this.openDropdownId || this.sortOpen || this.typeFilterOpen) {
      this.openDropdownId = null;
      this.sortOpen = false;
      this.typeFilterOpen = false;
      this.cdr.markForCheck();
    }
  }

  get currentSortLabel(): string {
    return this.sortOptions.find(option => option.value === this.sort)?.label ?? 'Newest First';
  }

  get typeFilterLabel(): string {
    if (this.typeFilter.length === 0) {
      return 'All types';
    }

    if (this.typeFilter.length === 1) {
      return this.reviewTypes.find(type => type.value === this.typeFilter[0])?.label ?? 'Type';
    }

    return `${this.typeFilter.length} types`;
  }

  get reportedCount(): number {
    return this.counts.reportedCount || this.pendingReportsCount;
  }

  onSearchChange(): void {
    this.searchSubject.next(this.searchQuery);
  }

  setStatusFilter(status: AdminReviewStatusFilter): void {
    if (this.statusFilter === status) {
      return;
    }

    this.statusFilter = status;
    this.openDropdownId = null;
    this.resetAndLoad();
  }

  toggleTypeFilter(type: AdminReviewTypeFilter): void {
    const exists = this.typeFilter.includes(type);
    this.typeFilter = exists
      ? this.typeFilter.filter(item => item !== type)
      : [...this.typeFilter, type];
    this.resetAndLoad();
  }

  isTypeSelected(type: AdminReviewTypeFilter): boolean {
    return this.typeFilter.includes(type);
  }

  applySort(value: AdminReviewSort): void {
    this.sort = value;
    this.sortOpen = false;
    this.resetAndLoad();
  }

  retry(): void {
    this.resetAndLoad();
  }

  loadMore(): void {
    if (!this.hasMore || this.isLoadingMore || this.nextSkip === null) {
      return;
    }

    this.loadReviews(false);
  }

  openDetail(review: AdminReviewListItem, event?: Event): void {
    event?.stopPropagation();
    this.openDropdownId = null;
    this.sortOpen = false;
    this.typeFilterOpen = false;
    this.detailTarget = review;
    this.detailReview = null;
    this.detailErrorMessage = '';
    this.loadDetail(review.reviewKey);
    this.cdr.markForCheck();
  }

  closeDetail(): void {
    if (this.isActionSaving) {
      return;
    }

    this.detailLoadSequence++;
    this.detailTarget = null;
    this.detailReview = null;
    this.isDetailLoading = false;
    this.detailErrorMessage = '';
    this.cdr.markForCheck();
  }

  retryDetail(): void {
    if (!this.detailTarget) {
      return;
    }

    this.loadDetail(this.detailTarget.reviewKey);
  }

  toggleDropdown(reviewKey: string, event: MouseEvent): void {
    event.stopPropagation();

    if (this.openDropdownId === reviewKey) {
      this.openDropdownId = null;
      this.cdr.markForCheck();
      return;
    }

    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
    const dropdownHeight = 240;
    const spaceBelow = window.innerHeight - rect.bottom;
    const top = spaceBelow < dropdownHeight ? Math.max(8, rect.top - dropdownHeight - 4) : rect.bottom + 4;
    this.dropdownPos = { top, right: window.innerWidth - rect.right };
    this.openDropdownId = reviewKey;
    this.cdr.markForCheck();
  }

  openConfirmation(action: ReviewAction, review: ReviewActionTarget, event?: MouseEvent): void {
    event?.stopPropagation();
    this.openDropdownId = null;
    this.confirmation = { action, review };
    this.cdr.markForCheck();
  }

  closeConfirmation(): void {
    if (this.isActionSaving) {
      return;
    }

    this.confirmation = null;
    this.cdr.markForCheck();
  }

  confirmAction(): void {
    const state = this.confirmation;
    if (!state) {
      return;
    }

    const reviewKey = state.review.reviewKey;
    this.isActionSaving = true;
    this.errorMessage = '';
    this.detailErrorMessage = '';

    const request$ = this.actionRequest(state);
    request$.pipe(
      takeUntil(this.destroy$),
      finalize(() => {
        this.isActionSaving = false;
        this.cdr.markForCheck();
      }),
    ).subscribe({
      next: result => {
        this.confirmation = null;
        this.showSuccess(this.actionSuccessMessage(state.action, result));
        this.applyActionCountUpdate(result);

        if (state.action === 'deleteReview' || state.action === 'removeAndSuspend' || state.action === 'removeAndDelete') {
          this.closeDetailAfterAction(reviewKey);
        } else {
          this.refreshDetailIfOpen(reviewKey);
        }

        this.resetAndLoad();
        this.loadPendingReportsBadge();
      },
      error: error => {
        const message = this.resolveErrorMessage(error);
        this.errorMessage = message;
        if (this.detailTarget?.reviewKey === reviewKey || this.detailReview?.reviewKey === reviewKey) {
          this.detailErrorMessage = message;
        }
      },
    });

  }

  hasRowActions(review: AdminReviewListItem): boolean {
    const permissions = review.permissions;
    return permissions.canDeleteReview ||
      permissions.canSuspendAuthor ||
      permissions.canUnsuspendAuthor ||
      permissions.canDeleteAuthor ||
      permissions.canRemoveReviewAndSuspendAuthor ||
      permissions.canRemoveReviewAndDeleteAuthor;
  }

  hasDetailActions(review: AdminReviewDetail): boolean {
    const permissions = review.permissions;
    return permissions.canDeleteReview ||
      permissions.canSuspendAuthor ||
      permissions.canUnsuspendAuthor ||
      permissions.canDeleteAuthor ||
      permissions.canRemoveReviewAndSuspendAuthor ||
      permissions.canRemoveReviewAndDeleteAuthor;
  }

  stars(): number[] {
    return [1, 2, 3, 4, 5];
  }

  ratingColor(rating: number): string {
    if (rating >= 4) {
      return 'text-emerald-500';
    }
    if (rating >= 3) {
      return 'text-amber-500';
    }
    return 'text-red-500';
  }

  statusClass(status: string): string {
    if (status === 'Reported') {
      return 'bg-red-50 text-red-600 ring-1 ring-red-200';
    }
    if (status === 'Removed' || status === 'Hidden' || status === 'Deleted') {
      return 'bg-gray-100 text-gray-500 ring-1 ring-gray-200';
    }
    return 'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200';
  }

  authorStatusClass(status: string): string {
    if (status === 'Suspended') {
      return 'bg-amber-50 text-amber-700 ring-1 ring-amber-200';
    }
    if (status === 'Deleted') {
      return 'bg-gray-100 text-gray-500 ring-1 ring-gray-200';
    }
    return 'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200';
  }

  targetTypeClass(type: string): string {
    const normalized = type.toLowerCase();
    if (normalized === 'destination') {
      return 'bg-blue-50 text-blue-700 ring-1 ring-blue-200';
    }
    if (normalized === 'location') {
      return 'bg-violet-50 text-violet-700 ring-1 ring-violet-200';
    }
    if (normalized === 'event') {
      return 'bg-amber-50 text-amber-700 ring-1 ring-amber-200';
    }
    if (normalized === 'stay') {
      return 'bg-cyan-50 text-cyan-700 ring-1 ring-cyan-200';
    }
    return 'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200';
  }

  reportStatusClass(status: string): string {
    if (status === 'Pending') {
      return 'bg-red-50 text-red-700 ring-1 ring-red-100';
    }
    if (status === 'Actioned') {
      return 'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-100';
    }
    return 'bg-gray-100 text-gray-600 ring-1 ring-gray-200';
  }

  actionTitle(action: ReviewAction): string {
    switch (action) {
      case 'deleteReview':
        return 'Delete Review';
      case 'suspendAuthor':
        return 'Suspend Author Account';
      case 'unsuspendAuthor':
        return 'Unsuspend Author Account';
      case 'deleteAuthor':
        return 'Delete Author Account';
      case 'removeAndSuspend':
        return 'Remove Review & Suspend';
      case 'removeAndDelete':
        return 'Remove Review & Delete Author';
    }
  }

  actionBody(action: ReviewAction): string {
    switch (action) {
      case 'deleteReview':
        return 'This will remove the review from public view. Reports against this review will be marked as resolved.';
      case 'suspendAuthor':
        return 'This will suspend the author account. The user will not be able to use protected app features while suspended.';
      case 'unsuspendAuthor':
        return 'This will restore the author account access to protected app features.';
      case 'deleteAuthor':
        return 'This will deactivate the author account. Their public content may remain hidden according to moderation rules.';
      case 'removeAndSuspend':
        return 'This will remove the review and suspend the author account.';
      case 'removeAndDelete':
        return 'This will remove the review and deactivate the author account.';
    }
  }

  actionButtonLabel(action: ReviewAction): string {
    if (this.isActionSaving) {
      return 'Saving...';
    }

    switch (action) {
      case 'deleteReview':
        return 'Delete Review';
      case 'suspendAuthor':
        return 'Suspend Account';
      case 'unsuspendAuthor':
        return 'Unsuspend Account';
      case 'deleteAuthor':
        return 'Deactivate Account';
      case 'removeAndSuspend':
        return 'Remove & Suspend';
      case 'removeAndDelete':
        return 'Remove & Delete';
    }
  }

  formatDate(value: string | null | undefined): string {
    return this.reviewsSvc.formatDate(value);
  }

  formatDatetime(value: string | null | undefined): string {
    return this.reviewsSvc.formatDatetime(value);
  }

  trackByReviewKey(_: number, review: AdminReviewListItem): string {
    return review.reviewKey;
  }

  trackByReportId(_: number, report: { reportId: number }): number {
    return report.reportId;
  }

  private loadDetail(reviewKey: string, showLoading = true): void {
    const sequence = ++this.detailLoadSequence;
    this.detailErrorMessage = '';

    if (showLoading) {
      this.isDetailLoading = true;
    }

    this.reviewsSvc.getDetail$(reviewKey).pipe(
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

        this.detailReview = detail;
      },
      error: error => {
        if (sequence !== this.detailLoadSequence) {
          return;
        }

        this.detailErrorMessage = this.resolveErrorMessage(error);
      },
    });
  }

  private resetAndLoad(): void {
    this.nextSkip = null;
    this.hasMore = false;
    this.reviews = [];
    this.loadReviews(true);
  }

  private loadReviews(reset: boolean): void {
    const skip = reset ? 0 : this.nextSkip ?? this.reviews.length;
    const sequence = ++this.loadSequence;
    this.errorMessage = '';

    if (reset) {
      this.isLoading = true;
    } else {
      this.isLoadingMore = true;
    }

    this.reviewsSvc.getPage$({
      status: this.statusFilter,
      type: this.typeFilter,
      search: this.searchQuery,
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

        this.reviews = reset ? page.items : [...this.reviews, ...page.items];
        this.totalCount = page.totalCount;
        this.nextSkip = page.nextSkip;
        this.hasMore = page.hasMore;
        this.counts = page.counts;
        this.pendingReportsCount = page.counts.reportedCount;
        this.scrollToHighlightIfNeeded();
      },
      error: error => {
        if (sequence !== this.loadSequence) {
          return;
        }

        this.errorMessage = this.resolveErrorMessage(error);
      },
    });
  }

  private actionRequest(state: ConfirmationState): Observable<AdminReviewActionResult | AppUser | void> {
    switch (state.action) {
      case 'deleteReview':
        return this.reviewsSvc.deleteReview$(state.review.reviewKey);
      case 'suspendAuthor':
        return this.usersSvc.suspend$(state.review.author.userId);
      case 'unsuspendAuthor':
        return this.usersSvc.unsuspend$(state.review.author.userId);
      case 'deleteAuthor':
        return this.usersSvc.delete$(state.review.author.userId);
      case 'removeAndSuspend':
        return this.reviewsSvc.removeAndSuspendAuthor$(state.review.reviewKey);
      case 'removeAndDelete':
        return this.reviewsSvc.removeAndDeleteAuthor$(state.review.reviewKey);
    }
  }

  private actionSuccessMessage(action: ReviewAction, result: AdminReviewActionResult | AppUser | void): string {
    if (result && typeof result === 'object' && 'message' in result && typeof result.message === 'string') {
      return result.message;
    }

    switch (action) {
      case 'deleteReview':
        return 'Review removed.';
      case 'suspendAuthor':
        return 'Author account suspended.';
      case 'unsuspendAuthor':
        return 'Author account unsuspended.';
      case 'deleteAuthor':
        return 'Author account deactivated.';
      case 'removeAndSuspend':
        return 'Review removed and author suspended.';
      case 'removeAndDelete':
        return 'Review removed and author deactivated.';
    }
  }

  private applyActionCountUpdate(result: AdminReviewActionResult | AppUser | void): void {
    if (result && typeof result === 'object' && 'counts' in result) {
      this.counts = result.counts;
      this.pendingReportsCount = result.counts.reportedCount;
    }
  }

  private closeDetailAfterAction(reviewKey: string): void {
    if (this.detailTarget?.reviewKey === reviewKey || this.detailReview?.reviewKey === reviewKey) {
      this.detailTarget = null;
      this.detailReview = null;
      this.isDetailLoading = false;
      this.detailErrorMessage = '';
    }
  }

  private refreshDetailIfOpen(reviewKey: string): void {
    if (this.detailTarget?.reviewKey === reviewKey || this.detailReview?.reviewKey === reviewKey) {
      this.loadDetail(reviewKey, false);
    }
  }

  private loadPendingReportsBadge(): void {
    this.dashboardSvc.loadDashboard()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: dashboard => {
          this.pendingReportsCount = dashboard.badges.pendingReportsCount;
          this.supportReportsCount = dashboard.badges.supportReportsCount;
          if (!this.counts.reportedCount) {
            this.counts = { ...this.counts, reportedCount: dashboard.badges.pendingReportsCount };
          }
          this.cdr.markForCheck();
        },
        error: () => {
          this.pendingReportsCount = 0;
          this.supportReportsCount = 0;
          this.cdr.markForCheck();
        },
      });
  }

  private scrollToHighlightIfNeeded(): void {
    if (!this.highlightKey) {
      return;
    }

    const key = this.highlightKey;
    setTimeout(() => {
      document.getElementById(`review-${key}`)?.scrollIntoView({
        behavior: 'smooth',
        block: 'center',
      });
      this.highlightKey = null;
      this.cdr.markForCheck();
    }, 100);
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
