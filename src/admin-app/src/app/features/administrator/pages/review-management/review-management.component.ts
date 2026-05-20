import {
  Component, OnInit, OnDestroy, HostListener,
  ChangeDetectionStrategy, ChangeDetectorRef, inject,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { Router } from '@angular/router';
import { Subject } from 'rxjs';
import { takeUntil, debounceTime, distinctUntilChanged } from 'rxjs/operators';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { AdminReviewsService } from '../../../../core/services/administrator/admin-reviews.service';
import { AppUsersService } from '../../../../core/services/administrator/app-users.service';
import {
  AdminReview, AdminReviewsPageRequest,
  ReviewSortField, ReviewSortDir, ReviewStatusFilter,
} from '../../../../core/models/administrator/admin-review.model';

const PAGE_SIZE_INITIAL = 12;
const PAGE_SIZE_STEP    = 8;

@Component({
  selector: 'app-review-management',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './review-management.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReviewManagementComponent implements OnInit, OnDestroy {
  private readonly revSvc   = inject(AdminReviewsService);
  private readonly usersSvc = inject(AppUsersService);
  private readonly cdr      = inject(ChangeDetectorRef);
  private readonly route    = inject(ActivatedRoute);
  private readonly router   = inject(Router);
  private destroy$      = new Subject<void>();
  private searchSubject = new Subject<string>();

  // ── List state ────────────────────────────────────────────────────────────
  reviews: AdminReview[]   = [];
  totalReviews             = 0;
  reportedCount            = 0;
  isLoading                = false;
  isLoadingMore            = false;
  displayCount             = PAGE_SIZE_INITIAL;
  highlightId: string | null = null;

  searchQuery                  = '';
  statusFilter: ReviewStatusFilter = 'reported';
  sortField: ReviewSortField   = 'createdAt';
  sortDir: ReviewSortDir       = 'desc';
  sortOpen                     = false;

  readonly reviewTypes: AdminReview['targetType'][] = ['Destination', 'Place', 'Location', 'Event'];
  typeFilter: string[]         = [];
  typeFilterOpen               = false;

  sortOptions: { label: string; field: ReviewSortField; dir: ReviewSortDir }[] = [
    { label: 'Newest First',    field: 'createdAt',   dir: 'desc' },
    { label: 'Oldest First',    field: 'createdAt',   dir: 'asc'  },
    { label: 'Most Reports',    field: 'reportCount', dir: 'desc' },
  ];

  get currentSortLabel(): string {
    return this.sortOptions.find(o => o.field === this.sortField && o.dir === this.sortDir)?.label ?? 'Newest First';
  }

  get typeFilterLabel(): string {
    if (this.typeFilter.length === 0) return 'All types';
    if (this.typeFilter.length === 1) return this.typeFilter[0];
    return `${this.typeFilter.length} types`;
  }

  // ── Dropdown ─────────────────────────────────────────────────────────────
  openDropdownId: string | null = null;
  dropdownPos = { top: 0, right: 0 };

  // ── Delete review confirm ─────────────────────────────────────────────────
  deleteReview: AdminReview | null = null;
  isDeleting                       = false;

  // ── Delete user from review ───────────────────────────────────────────────
  deleteUserFromReview: AdminReview | null = null;
  isDeletingUser                           = false;

  // ── Suspend user from review ──────────────────────────────────────────────
  suspendUserFromReview: AdminReview | null = null;
  isSuspendingUser                          = false;

  // ── Remove review + delete user ───────────────────────────────────────────
  removeReviewAndDeleteUser: AdminReview | null = null;
  isRemoveAndDeleting                           = false;

  // ── Remove review + suspend user ─────────────────────────────────────────
  removeReviewAndSuspendUser: AdminReview | null = null;
  isRemoveAndSuspending                          = false;

  // ── Detail view ───────────────────────────────────────────────────────────
  detailReview: AdminReview | null = null;

  ngOnInit(): void {
    this.route.queryParams.pipe(takeUntil(this.destroy$)).subscribe(params => {
      if (params['highlight']) {
        this.highlightId    = params['highlight'];
        this.statusFilter   = 'reported';
        this.router.navigate([], { queryParams: {}, replaceUrl: true });
      }
      this.loadReviews();
    });

    this.searchSubject.pipe(
      debounceTime(300),
      distinctUntilChanged(),
      takeUntil(this.destroy$),
    ).subscribe(() => {
      this.loadReviews();
    });

    this.revSvc.notifications$.pipe(takeUntil(this.destroy$)).subscribe(() => {
      this.reportedCount = this.revSvc.getReportedCount();
      this.cdr.detectChanges();
    });
  }

  ngOnDestroy(): void { this.destroy$.next(); this.destroy$.complete(); }

  @HostListener('document:click')
  onDocumentClick(): void {
    let changed = false;
    if (this.openDropdownId)  { this.openDropdownId = null;  changed = true; }
    if (this.sortOpen)        { this.sortOpen = false;        changed = true; }
    if (this.typeFilterOpen)  { this.typeFilterOpen = false;  changed = true; }
    if (changed) this.cdr.detectChanges();
  }

  buildReq(): AdminReviewsPageRequest {
    return { page: 1, pageSize: this.displayCount, search: this.searchQuery, sortField: this.sortField, sortDir: this.sortDir, statusFilter: this.statusFilter, typeFilter: this.typeFilter };
  }

  loadReviews(): void {
    this.isLoading = true;
    this.revSvc.getPage$(this.buildReq())
      .pipe(takeUntil(this.destroy$))
      .subscribe(page => {
        this.reviews      = page.items;
        this.totalReviews = page.total;
        this.reportedCount = page.reportedCount;
        this.isLoading    = false;
        this.cdr.detectChanges();

        if (this.highlightId) {
          setTimeout(() => {
            document.getElementById(`review-${this.highlightId}`)?.scrollIntoView({ behavior: 'smooth', block: 'center' });
            this.highlightId = null;
            this.cdr.detectChanges();
          }, 100);
        }
      });
  }

  loadMore(): void {
    this.isLoadingMore = true;
    this.displayCount += PAGE_SIZE_STEP;
    this.revSvc.getPage$(this.buildReq())
      .pipe(takeUntil(this.destroy$))
      .subscribe(page => {
        this.reviews       = page.items;
        this.totalReviews  = page.total;
        this.isLoadingMore = false;
        this.cdr.detectChanges();
      });
  }

  get hasMore(): boolean { return this.reviews.length < this.totalReviews; }

  onSearchChange(): void { this.searchSubject.next(this.searchQuery); }

  onStatusFilterChange(): void { this.loadReviews(); }

  toggleTypeFilter(type: string): void {
    const idx = this.typeFilter.indexOf(type);
    this.typeFilter = idx === -1 ? [...this.typeFilter, type] : this.typeFilter.filter(t => t !== type);
    this.loadReviews();
  }

  isTypeSelected(type: string): boolean { return this.typeFilter.includes(type); }

  applySortOption(opt: { label: string; field: ReviewSortField; dir: ReviewSortDir }): void {
    this.sortField = opt.field;
    this.sortDir   = opt.dir;
    this.sortOpen  = false;
    this.loadReviews();
  }

  // ── Row dropdown ─────────────────────────────────────────────────────────
  toggleDropdown(id: string, event: MouseEvent): void {
    event.stopPropagation();
    if (this.openDropdownId === id) {
      this.openDropdownId = null;
    } else {
      const rect           = (event.currentTarget as HTMLElement).getBoundingClientRect();
      const dropdownHeight = 200;
      const spaceBelow     = window.innerHeight - rect.bottom;
      const top = spaceBelow < dropdownHeight ? rect.top - dropdownHeight : rect.bottom + 4;
      this.dropdownPos    = { top, right: window.innerWidth - rect.right };
      this.openDropdownId = id;
    }
    this.cdr.detectChanges();
  }

  // ── Detail modal ──────────────────────────────────────────────────────────
  openDetail(review: AdminReview, event?: MouseEvent): void {
    event?.stopPropagation();
    this.openDropdownId = null;
    this.detailReview   = { ...review };
    this.cdr.detectChanges();
  }

  closeDetail(): void { this.detailReview = null; this.cdr.detectChanges(); }

  // ── Delete review ─────────────────────────────────────────────────────────
  openDeleteReview(review: AdminReview, event?: MouseEvent): void {
    event?.stopPropagation();
    this.openDropdownId = null;
    this.deleteReview   = { ...review };
    this.cdr.detectChanges();
  }

  closeDeleteReview(): void { this.deleteReview = null; this.cdr.detectChanges(); }

  confirmDeleteReview(): void {
    if (!this.deleteReview) return;
    this.isDeleting = true;
    this.revSvc.deleteReview$(this.deleteReview.id)
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => {
        this.isDeleting   = false;
        this.deleteReview = null;
        this.loadReviews();
      });
  }

  // ── Delete user from review context ──────────────────────────────────────
  openDeleteUser(review: AdminReview, event?: MouseEvent): void {
    event?.stopPropagation();
    this.openDropdownId      = null;
    this.deleteUserFromReview = { ...review };
    this.cdr.detectChanges();
  }

  closeDeleteUser(): void { this.deleteUserFromReview = null; this.cdr.detectChanges(); }

  confirmDeleteUser(): void {
    if (!this.deleteUserFromReview) return;
    this.isDeletingUser = true;
    this.usersSvc.delete$(this.deleteUserFromReview.authorId)
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => {
        this.isDeletingUser       = false;
        this.deleteUserFromReview = null;
        this.loadReviews();
      });
  }

  // ── Suspend user from review context ──────────────────────────────────────
  openSuspendUser(review: AdminReview, event?: MouseEvent): void {
    event?.stopPropagation();
    this.openDropdownId       = null;
    this.suspendUserFromReview = { ...review };
    this.cdr.detectChanges();
  }

  closeSuspendUser(): void { this.suspendUserFromReview = null; this.cdr.detectChanges(); }

  confirmSuspendUser(): void {
    if (!this.suspendUserFromReview) return;
    this.isSuspendingUser = true;
    this.usersSvc.suspend$(this.suspendUserFromReview.authorId)
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => {
        this.isSuspendingUser      = false;
        this.suspendUserFromReview = null;
        this.loadReviews();
      });
  }

  // ── Remove review + delete user ───────────────────────────────────────────
  openRemoveReviewAndDeleteUser(review: AdminReview, event?: MouseEvent): void {
    event?.stopPropagation();
    this.openDropdownId           = null;
    this.removeReviewAndDeleteUser = { ...review };
    this.cdr.detectChanges();
  }

  closeRemoveReviewAndDeleteUser(): void { this.removeReviewAndDeleteUser = null; this.cdr.detectChanges(); }

  confirmRemoveReviewAndDeleteUser(): void {
    if (!this.removeReviewAndDeleteUser) return;
    this.isRemoveAndDeleting = true;
    const review = this.removeReviewAndDeleteUser;
    this.revSvc.deleteReview$(review.id)
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => {
        this.usersSvc.delete$(review.authorId)
          .pipe(takeUntil(this.destroy$))
          .subscribe(() => {
            this.isRemoveAndDeleting      = false;
            this.removeReviewAndDeleteUser = null;
            this.loadReviews();
          });
      });
  }

  // ── Remove review + suspend user ──────────────────────────────────────────
  openRemoveReviewAndSuspendUser(review: AdminReview, event?: MouseEvent): void {
    event?.stopPropagation();
    this.openDropdownId            = null;
    this.removeReviewAndSuspendUser = { ...review };
    this.cdr.detectChanges();
  }

  closeRemoveReviewAndSuspendUser(): void { this.removeReviewAndSuspendUser = null; this.cdr.detectChanges(); }

  confirmRemoveReviewAndSuspendUser(): void {
    if (!this.removeReviewAndSuspendUser) return;
    this.isRemoveAndSuspending = true;
    const review = this.removeReviewAndSuspendUser;
    this.revSvc.deleteReview$(review.id)
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => {
        this.usersSvc.suspend$(review.authorId)
          .pipe(takeUntil(this.destroy$))
          .subscribe(() => {
            this.isRemoveAndSuspending      = false;
            this.removeReviewAndSuspendUser = null;
            this.loadReviews();
          });
      });
  }

  // ── Helpers ───────────────────────────────────────────────────────────────
  stars(rating: number): number[] { return Array.from({ length: 5 }, (_, i) => i + 1); }

  ratingColor(rating: number): string {
    if (rating >= 4) return 'text-emerald-500';
    if (rating >= 3) return 'text-amber-400';
    return 'text-red-500';
  }

  statusClass(status: string): string {
    const map: Record<string, string> = {
      Active:   'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200',
      Reported: 'bg-red-50 text-red-600 ring-1 ring-red-200',
      Deleted:  'bg-gray-100 text-gray-500 ring-1 ring-gray-200',
    };
    return map[status] ?? 'bg-gray-100 text-gray-500';
  }

  targetTypeClass(type: string): string {
    const map: Record<string, string> = {
      Destination: 'bg-blue-50 text-blue-700 ring-1 ring-blue-200',
      Place:       'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200',
      Location:    'bg-violet-50 text-violet-700 ring-1 ring-violet-200',
      Event:       'bg-amber-50 text-amber-700 ring-1 ring-amber-200',
    };
    return map[type] ?? 'bg-gray-50 text-gray-500';
  }

  formatDate(iso: string): string { return this.revSvc.formatDate(iso); }
  formatDatetime(iso: string): string { return this.revSvc.formatDatetime(iso); }

  trackById(_: number, r: AdminReview): string { return r.id; }
}
