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
  sortField: ReviewSortField   = 'reportedAt';
  sortDir: ReviewSortDir       = 'desc';
  sortOpen                     = false;

  sortOptions: { label: string; field: ReviewSortField; dir: ReviewSortDir }[] = [
    { label: 'Reported: Newest First', field: 'reportedAt', dir: 'desc' },
    { label: 'Reported: Oldest First', field: 'reportedAt', dir: 'asc'  },
    { label: 'Most Reports First',     field: 'reportCount', dir: 'desc' },
    { label: 'Highest Rating',         field: 'rating',     dir: 'desc' },
    { label: 'Lowest Rating',          field: 'rating',     dir: 'asc'  },
    { label: 'Newest Review',          field: 'createdAt',  dir: 'desc' },
  ];

  get currentSortLabel(): string {
    return this.sortOptions.find(o => o.field === this.sortField && o.dir === this.sortDir)?.label ?? 'Sort';
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
      this.displayCount = PAGE_SIZE_INITIAL;
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
    if (this.openDropdownId) { this.openDropdownId = null; changed = true; }
    if (this.sortOpen)       { this.sortOpen = false;       changed = true; }
    if (changed) this.cdr.detectChanges();
  }

  buildReq(): AdminReviewsPageRequest {
    return { pageSize: this.displayCount, search: this.searchQuery, sortField: this.sortField, sortDir: this.sortDir, statusFilter: this.statusFilter };
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

  onStatusFilterChange(): void { this.displayCount = PAGE_SIZE_INITIAL; this.sortField = this.statusFilter === 'reported' ? 'reportedAt' : 'createdAt'; this.loadReviews(); }

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
      const spaceBelow     = window.innerHeight - rect.bottom;
      const top = spaceBelow < 140 ? rect.top - 144 : rect.bottom + 4;
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
