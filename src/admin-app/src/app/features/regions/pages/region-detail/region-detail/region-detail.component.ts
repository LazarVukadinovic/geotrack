import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  HostListener,
  OnDestroy,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router } from '@angular/router';
import { Subscription, finalize } from 'rxjs';
import { SidebarComponent } from '../../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../../shared/components/topbar/topbar.component';
import { RegionsService } from '../../../../../core/services/national/regions.service';
import { AuthService } from '../../../../../core/services/auth.service';
import { UserAppLinkService } from '../../../../../core/services/user-app-link.service';
import {
  TerritoryHealthInsight,
  TerritoryRegionContentItem,
  TerritoryRegionContentType,
  TerritoryRegionDetail,
  TerritoryRegionManager,
} from '../../../../../core/models/dashboard.model';

const CONTENT_PAGE_SIZE = 25;

interface DetailTab {
  label: string;
  value: TerritoryRegionContentType;
}

interface ContentSortOption {
  label: string;
  value: string;
}

@Component({
  selector: 'app-region-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './region-detail.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RegionDetailComponent implements OnInit, OnDestroy {
  detail: TerritoryRegionDetail | null = null;
  isLoading = false;
  errorMessage: string | null = null;

  activeTab: TerritoryRegionContentType = 'destinations';
  contentItems: TerritoryRegionContentItem[] = [];
  contentTotal = 0;
  isContentLoading = false;
  isContentLoadingMore = false;
  contentErrorMessage: string | null = null;
  contentSearch = '';
  contentSort = 'name';
  contentSortOpen = false;
  showAllHealthDetails = false;

  readonly tabs: DetailTab[] = [
    { label: 'Destinations', value: 'destinations' },
    { label: 'Locations', value: 'locations' },
    { label: 'Places', value: 'places' },
    { label: 'Stays', value: 'stays' },
    { label: 'Events', value: 'events' },
    { label: 'All', value: 'all' },
  ];

  readonly contentSortOptions: ContentSortOption[] = [
    { label: 'Name', value: 'name' },
    { label: 'Newest', value: 'newest' },
    { label: 'Upcoming', value: 'upcoming' },
    { label: 'Most Views', value: 'views' },
    { label: 'Most Saves', value: 'saves' },
    { label: 'Highest Rating', value: 'rating' },
    { label: 'Health High', value: 'health-high' },
    { label: 'Health Low', value: 'health-low' },
  ];

  private contentSearchHandle: ReturnType<typeof setTimeout> | null = null;
  private detailSubscription: Subscription | null = null;
  private contentSubscription: Subscription | null = null;
  private contentRequestSequence = 0;
  private expandedDestinationIds = new Set<number>();
  private failedManagerAvatarKeys = new Set<string>();

  constructor(
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly svc: RegionsService,
    private readonly auth: AuthService,
    private readonly userAppLinks: UserAppLinkService,
    private readonly cdr: ChangeDetectorRef,
  ) {}

  ngOnInit(): void {
    this.loadDetail();
  }

  ngOnDestroy(): void {
    this.contentRequestSequence++;
    this.detailSubscription?.unsubscribe();
    this.contentSubscription?.unsubscribe();

    if (this.contentSearchHandle) {
      clearTimeout(this.contentSearchHandle);
    }
  }

  get userName(): string {
    return this.auth.fullName;
  }

  get userRole(): string {
    return this.auth.getUser()?.role ?? '';
  }

  get regionDisplayName(): string {
    return this.detail?.region.territoryDisplayName ?? '';
  }

  get healthInsights(): TerritoryHealthInsight[] {
    const drivers = this.detail?.health.drivers ?? [];
    const issues = this.detail?.health.issues ?? [];
    const recommendations = this.detail?.health.recommendations ?? [];
    const all = [...drivers, ...issues, ...recommendations];
    return this.showAllHealthDetails ? all : all.slice(0, 5);
  }

  get hiddenHealthInsightCount(): number {
    const all =
      (this.detail?.health.drivers.length ?? 0) +
      (this.detail?.health.issues.length ?? 0) +
      (this.detail?.health.recommendations.length ?? 0);
    return Math.max(0, all - this.healthInsights.length);
  }

  get hasMoreContent(): boolean {
    return this.contentItems.length < this.contentTotal;
  }

  get nextContentPageCount(): number {
    return Math.min(CONTENT_PAGE_SIZE, Math.max(0, this.contentTotal - this.contentItems.length));
  }

  get currentContentSortLabel(): string {
    return this.contentSortOptions.find(option => option.value === this.contentSort)?.label ?? 'Name';
  }

  @HostListener('document:click')
  onDocumentClick(): void {
    if (this.contentSortOpen) {
      this.contentSortOpen = false;
      this.notifyStateChanged();
    }
  }

  goBack(): void {
    this.router.navigate(['/territory/regions']);
  }

  onTerritoryChange(territoryId: number): void {
    this.svc.setActiveTerritoryId(territoryId);
    this.router.navigate(['/territory/regions']);
  }

  setActiveTab(tab: TerritoryRegionContentType): void {
    if (tab === this.activeTab) return;

    this.activeTab = tab;
    this.contentSearch = '';
    this.contentSort = tab === 'events' ? 'upcoming' : 'name';
    this.expandedDestinationIds.clear();
    this.resetContent();
    this.loadContent(true);
    this.notifyStateChanged();
  }

  onContentSearchChange(): void {
    if (this.contentSearchHandle) {
      clearTimeout(this.contentSearchHandle);
    }

    this.contentSearchHandle = setTimeout(() => {
      this.resetContent();
      this.loadContent(true);
      this.notifyStateChanged();
    }, 300);
  }

  clearContentSearch(): void {
    this.contentSearch = '';
    this.onContentSearchChange();
  }

  applyContentSort(option: ContentSortOption): void {
    if (option.value === this.contentSort) {
      this.contentSortOpen = false;
      this.notifyStateChanged();
      return;
    }

    this.contentSort = option.value;
    this.contentSortOpen = false;
    this.resetContent();
    this.loadContent(true);
    this.notifyStateChanged();
  }

  loadMoreContent(): void {
    if (!this.hasMoreContent || this.isContentLoadingMore) return;
    this.loadContent(false);
  }

  toggleDestinationHealth(item: TerritoryRegionContentItem, event: MouseEvent): void {
    event.stopPropagation();

    if (this.expandedDestinationIds.has(item.id)) {
      this.expandedDestinationIds.delete(item.id);
    } else {
      this.expandedDestinationIds.add(item.id);
    }

    this.notifyStateChanged();
  }

  isDestinationExpanded(item: TerritoryRegionContentItem): boolean {
    return this.expandedDestinationIds.has(item.id);
  }

  hasDestinationHealth(item: TerritoryRegionContentItem): boolean {
    return item.entityType === 'destination' || item.entityType === 'destinations';
  }

  publicPreviewUrl(item: TerritoryRegionContentItem): string | null {
    return this.userAppLinks.toUserAppUrl(item.publicDetailRoute);
  }

  openPublicPreview(item: TerritoryRegionContentItem, event?: MouseEvent): void {
    if (event?.defaultPrevented || this.isInteractivePreviewTarget(event?.target)) {
      return;
    }

    const publicUrl = this.publicPreviewUrl(item);
    if (!publicUrl) {
      return;
    }

    globalThis.open?.(publicUrl, '_blank', 'noopener,noreferrer');
  }

  openPublicPreviewFromKeyboard(item: TerritoryRegionContentItem, event: Event): void {
    if (event.defaultPrevented || this.isInteractivePreviewTarget(event.target)) {
      return;
    }

    const publicUrl = this.publicPreviewUrl(item);
    if (!publicUrl) {
      return;
    }

    event.preventDefault();
    globalThis.open?.(publicUrl, '_blank', 'noopener,noreferrer');
  }

  formatDate(value?: string | null): string {
    if (!value) return '-';
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return value;
    return date.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
  }

  formatRating(value?: number | null): string {
    return value === null || value === undefined ? '-' : value.toFixed(1);
  }

  managerAvatarUrl(manager: TerritoryRegionManager): string {
    const avatarUrl = manager.avatarUrl?.trim() ?? '';
    return avatarUrl && !this.failedManagerAvatarKeys.has(this.managerAvatarKey(manager, avatarUrl))
      ? avatarUrl
      : '';
  }

  managerInitials(manager: TerritoryRegionManager): string {
    const initials = manager.initials?.trim();
    if (initials) return initials;

    return this.buildInitials(manager.displayName || manager.email);
  }

  managerAvatarAlt(manager: TerritoryRegionManager): string {
    const label = manager.displayName || manager.email || 'Regional Manager';
    return `${label} profile image`;
  }

  onManagerAvatarError(manager: TerritoryRegionManager): void {
    const avatarUrl = manager.avatarUrl?.trim() ?? '';
    if (avatarUrl) {
      this.failedManagerAvatarKeys.add(this.managerAvatarKey(manager, avatarUrl));
      this.notifyStateChanged();
    }
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

  statusClass(status?: string | null): string {
    switch ((status ?? '').toLowerCase()) {
      case 'active':
      case 'published':
        return 'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200';
      case 'draft':
        return 'bg-amber-50 text-amber-700 ring-1 ring-amber-200';
      case 'archived':
      case 'inactive':
        return 'bg-gray-100 text-gray-500 ring-1 ring-gray-200';
      default:
        return 'bg-gray-100 text-gray-600 ring-1 ring-gray-200';
    }
  }

  insightClass(severity?: string | null): string {
    switch ((severity ?? '').toLowerCase()) {
      case 'positive':
        return 'bg-emerald-50 text-emerald-700 border-emerald-100';
      case 'warning':
        return 'bg-amber-50 text-amber-700 border-amber-100';
      case 'action':
        return 'bg-red-50 text-red-700 border-red-100';
      default:
        return 'bg-blue-50 text-blue-700 border-blue-100';
    }
  }

  trackByContentId(_: number, item: TerritoryRegionContentItem): string {
    return `${item.entityType}-${item.id}`;
  }

  private isInteractivePreviewTarget(target?: EventTarget | null): boolean {
    return target instanceof HTMLElement
      && Boolean(target.closest('a, button, input, select, textarea, [role="button"]'));
  }

  private loadDetail(): void {
    const regionId = this.route.snapshot.paramMap.get('id') ?? '';
    const state = history.state as { territoryId?: number } | undefined;

    this.isLoading = true;
    this.errorMessage = null;
    this.notifyStateChanged();

    this.detailSubscription = this.svc
      .getDetail$(regionId, state?.territoryId ?? this.svc.getActiveTerritoryId())
      .pipe(
        finalize(() => {
          this.isLoading = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: detail => {
          this.detail = detail;
          this.failedManagerAvatarKeys.clear();
          this.svc.setActiveTerritoryId(detail.region.territoryId);
          this.resetContent();
          this.loadContent(true);
        },
        error: error => this.handleDetailError(error),
      });
  }

  private loadContent(reset: boolean): void {
    if (!this.detail) return;

    const requestId = ++this.contentRequestSequence;
    const skip = reset ? 0 : this.contentItems.length;
    this.contentSubscription?.unsubscribe();

    if (reset) {
      this.isContentLoading = true;
      this.contentItems = [];
      this.contentTotal = 0;
      this.expandedDestinationIds.clear();
    } else {
      this.isContentLoadingMore = true;
    }

    this.contentErrorMessage = null;
    this.notifyStateChanged();

    this.contentSubscription = this.svc
      .getContent$({
        regionId: this.detail.region.id,
        territoryId: this.detail.region.territoryId,
        entityType: this.activeTab,
        search: this.contentSearch,
        sort: this.contentSort,
        skip,
        take: CONTENT_PAGE_SIZE,
      })
      .pipe(
        finalize(() => {
          if (this.contentRequestSequence === requestId) {
            this.isContentLoading = false;
            this.isContentLoadingMore = false;
            this.notifyStateChanged();
          }
        }),
      )
      .subscribe({
        next: response => {
          if (this.contentRequestSequence !== requestId) return;
          this.contentItems = reset ? response.items : [...this.contentItems, ...response.items];
          this.contentTotal = this.toSafeCount(response.totalCount);
        },
        error: error => {
          if (this.contentRequestSequence === requestId) {
            this.handleContentError(error);
          }
        },
      });
  }

  private resetContent(): void {
    this.contentItems = [];
    this.contentTotal = 0;
    this.contentErrorMessage = null;
    this.expandedDestinationIds.clear();
  }

  private handleDetailError(error: unknown): void {
    this.detail = null;

    if (error instanceof HttpErrorResponse) {
      if (error.status === 401) {
        this.errorMessage = 'Sign in with a Territory Manager account to view region details.';
        this.notifyStateChanged();
        return;
      }

      if (error.status === 403) {
        this.errorMessage = 'This account is not authorized to view that region.';
        this.notifyStateChanged();
        return;
      }

      if (error.status === 404) {
        this.errorMessage = 'Region not found.';
        this.notifyStateChanged();
        return;
      }
    }

    this.errorMessage = 'Unable to load region details.';
    this.notifyStateChanged();
  }

  private handleContentError(error: unknown): void {
    this.isContentLoading = false;
    this.isContentLoadingMore = false;
    this.resetContent();

    if (error instanceof HttpErrorResponse && error.status === 403) {
      this.contentErrorMessage = 'This account is not authorized to view this content.';
      this.notifyStateChanged();
      return;
    }

    this.contentErrorMessage = 'Unable to load region content.';
    this.notifyStateChanged();
  }

  private toSafeCount(value: unknown): number {
    return typeof value === 'number' && Number.isFinite(value) && value >= 0 ? value : 0;
  }

  private managerAvatarKey(manager: TerritoryRegionManager, avatarUrl: string): string {
    return `${manager.userId}:${avatarUrl}`;
  }

  private buildInitials(value: string): string {
    const parts = value
      .trim()
      .split(/[\s._-]+/)
      .filter(Boolean);

    if (parts.length === 0) {
      return '?';
    }

    return parts
      .slice(0, 2)
      .map(part => part.charAt(0).toUpperCase())
      .join('');
  }

  private notifyStateChanged(): void {
    this.cdr.markForCheck();
  }
}
