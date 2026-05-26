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
import { Observable, Subject, finalize } from 'rxjs';
import { debounceTime, distinctUntilChanged, takeUntil } from 'rxjs/operators';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { AppUsersService } from '../../../../core/services/administrator/app-users.service';
import {
  AdminUserListCounts,
  AdminUserModerationHistoryItem,
  AdminUserPreview,
  AdminUserRecentReview,
  AdminUserReportPreview,
  AdminUserSort,
  AdminUserStatusFilter,
  AdminUserTab,
  AppUser,
} from '../../../../core/models/administrator/app-user.model';
import { TerritoryService } from '../../../../core/services/administrator/territory.service';
import { AdminDashboardService } from '../../../../core/services/administrator/admin-dashboard.service';
import { Territory } from '../../../../core/models/administrator/territory.model';

const PAGE_SIZE = 25;

type ConfirmAction = 'suspend' | 'unsuspend' | 'delete';
type AdminUserPreviewTab = 'overview' | 'reports' | 'reviews' | 'history';

interface ConfirmationState {
  action: ConfirmAction;
  user: AppUser;
}

@Component({
  selector: 'app-user-management',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './user-management.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserManagementComponent implements OnInit, OnDestroy {
  private readonly appUsersSvc = inject(AppUsersService);
  private readonly territorySvc = inject(TerritoryService);
  private readonly dashboardSvc = inject(AdminDashboardService);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly destroy$ = new Subject<void>();
  private readonly searchSubject = new Subject<string>();

  private loadSequence = 0;
  private previewLoadSequence = 0;
  private successTimer: ReturnType<typeof setTimeout> | null = null;

  supportReportsCount = 0;
  activeTab: AdminUserTab = 'app-users';
  users: AppUser[] = [];
  counts: AdminUserListCounts = {
    appUsersCount: 0,
    territoryManagersCount: 0,
    activeCount: 0,
    suspendedCount: 0,
    deletedCount: 0,
  };

  totalCount = 0;
  nextSkip: number | null = null;
  hasMore = false;
  isLoading = false;
  isLoadingMore = false;
  errorMessage = '';
  successMessage = '';

  searchQuery = '';
  statusFilter: AdminUserStatusFilter = 'all';
  sort: AdminUserSort = 'newest';
  statusFilterOpen = false;
  sortOpen = false;

  openDropdownId: string | null = null;
  dropdownPos = { top: 0, right: 0 };

  confirmation: ConfirmationState | null = null;
  isActionSaving = false;

  assignTarget: AppUser | null = null;
  assignTerritoryId: number | null = null;
  allTerritories: Territory[] = [];
  isLoadingTerritories = false;
  isAssigning = false;

  previewTarget: AppUser | null = null;
  preview: AdminUserPreview | null = null;
  activePreviewTab: AdminUserPreviewTab = 'overview';
  isPreviewLoading = false;
  previewErrorMessage = '';

  readonly pageSize = PAGE_SIZE;
  readonly statusOptions: { label: string; value: AdminUserStatusFilter }[] = [
    { label: 'All statuses', value: 'all' },
    { label: 'Active', value: 'active' },
    { label: 'Suspended', value: 'suspended' },
    { label: 'Deleted', value: 'deleted' },
  ];
  readonly sortOptions: { label: string; value: AdminUserSort }[] = [
    { label: 'Newest Members', value: 'newest' },
    { label: 'Oldest Members', value: 'oldest' },
    { label: 'Last Active', value: 'lastActiveDesc' },
    { label: 'Name A-Z', value: 'nameAsc' },
    { label: 'Most Reviews', value: 'reviewsDesc' },
    { label: 'Most Reported', value: 'reportedDesc' },
  ];
  readonly previewTabs: { label: string; value: AdminUserPreviewTab }[] = [
    { label: 'Overview', value: 'overview' },
    { label: 'Reports', value: 'reports' },
    { label: 'Reviews', value: 'reviews' },
    { label: 'History', value: 'history' },
  ];

  ngOnInit(): void {
    this.loadPendingReportsBadge();
    this.loadTerritories();

    this.searchSubject.pipe(
      debounceTime(300),
      distinctUntilChanged(),
      takeUntil(this.destroy$),
    ).subscribe(() => this.resetAndLoad());

    this.resetAndLoad();
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
    if (this.statusFilterOpen) {
      this.statusFilterOpen = false;
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

    if (this.assignTarget && !this.isAssigning) {
      this.closeAssignTerritoryManager();
      return;
    }

    if (this.previewTarget) {
      this.closeUserPreview();
      return;
    }

    if (this.openDropdownId || this.sortOpen || this.statusFilterOpen) {
      this.openDropdownId = null;
      this.sortOpen = false;
      this.statusFilterOpen = false;
      this.cdr.markForCheck();
    }
  }

  get currentSortLabel(): string {
    return this.sortOptions.find(option => option.value === this.sort)?.label ?? 'Newest Members';
  }

  get statusFilterLabel(): string {
    return this.statusOptions.find(option => option.value === this.statusFilter)?.label ?? 'All statuses';
  }

  get activeTabTitle(): string {
    return this.activeTab === 'territory-managers' ? 'Territory Managers' : 'App Users';
  }

  get activeTabCount(): number {
    return this.activeTab === 'territory-managers'
      ? this.counts.territoryManagersCount
      : this.counts.appUsersCount;
  }

  get availableTerritories(): Territory[] {
    return this.allTerritories
      .filter(territory =>
        territory.territoryKind !== 'DisputedTerritory' &&
        territory.hasBoundary === true &&
        !territory.assignedManagerId &&
        territory.status !== 'Assigned')
      .sort((a, b) => a.name.localeCompare(b.name));
  }

  setTab(tab: AdminUserTab): void {
    if (this.activeTab === tab) {
      return;
    }

    this.activeTab = tab;
    this.openDropdownId = null;
    this.resetAndLoad();
  }

  onSearchChange(): void {
    this.searchSubject.next(this.searchQuery);
  }

  applyStatusFilter(value: AdminUserStatusFilter): void {
    this.statusFilter = value;
    this.statusFilterOpen = false;
    this.resetAndLoad();
  }

  applySort(value: AdminUserSort): void {
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

    this.loadUsers(false);
  }

  openUserPreview(user: AppUser, event?: Event): void {
    event?.stopPropagation();
    this.openDropdownId = null;
    this.sortOpen = false;
    this.statusFilterOpen = false;
    this.previewTarget = user;
    this.preview = null;
    this.previewErrorMessage = '';
    this.activePreviewTab = 'overview';
    this.loadUserPreview(user.userId);
    this.cdr.markForCheck();
  }

  closeUserPreview(): void {
    this.previewLoadSequence++;
    this.previewTarget = null;
    this.preview = null;
    this.isPreviewLoading = false;
    this.previewErrorMessage = '';
    this.activePreviewTab = 'overview';
    this.cdr.markForCheck();
  }

  retryUserPreview(): void {
    const target = this.previewTarget;
    if (!target) {
      return;
    }

    this.loadUserPreview(target.userId);
  }

  setPreviewTab(tab: AdminUserPreviewTab): void {
    this.activePreviewTab = tab;
  }

  toggleDropdown(userId: string, event: MouseEvent): void {
    event.stopPropagation();

    if (this.openDropdownId === userId) {
      this.openDropdownId = null;
      this.cdr.markForCheck();
      return;
    }

    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
    const dropdownHeight = 170;
    const spaceBelow = window.innerHeight - rect.bottom;
    const top = spaceBelow < dropdownHeight ? Math.max(8, rect.top - dropdownHeight - 4) : rect.bottom + 4;
    this.dropdownPos = { top, right: window.innerWidth - rect.right };
    this.openDropdownId = userId;
    this.cdr.markForCheck();
  }

  openConfirmation(action: ConfirmAction, user: AppUser, event?: MouseEvent): void {
    event?.stopPropagation();
    this.openDropdownId = null;
    this.confirmation = { action, user };
    this.cdr.markForCheck();
  }

  closeConfirmation(): void {
    if (this.isActionSaving) {
      return;
    }

    this.confirmation = null;
    this.cdr.markForCheck();
  }

  confirmAccountAction(): void {
    const state = this.confirmation;
    if (!state) {
      return;
    }

    const targetUserId = state.user.userId;
    this.isActionSaving = true;
    this.errorMessage = '';

    const request$: Observable<AppUser | void> = state.action === 'suspend'
      ? this.appUsersSvc.suspend$(state.user.userId)
      : state.action === 'unsuspend'
        ? this.appUsersSvc.unsuspend$(state.user.userId)
        : this.appUsersSvc.delete$(state.user.userId);

    request$.pipe(
      takeUntil(this.destroy$),
      finalize(() => {
        this.isActionSaving = false;
        this.cdr.markForCheck();
      }),
    ).subscribe({
      next: () => {
        const message = state.action === 'delete'
          ? 'Account deactivated.'
          : state.action === 'suspend'
            ? 'Account suspended.'
            : 'Account unsuspended.';
        this.confirmation = null;
        this.showSuccess(message);
        this.resetAndLoad();
        this.refreshPreviewIfOpen(targetUserId);
      },
      error: (error: unknown) => {
        this.errorMessage = this.resolveErrorMessage(error);
      },
    });
  }

  openAssignTerritoryManager(user: AppUser, event?: MouseEvent): void {
    event?.stopPropagation();
    this.openDropdownId = null;
    this.assignTarget = user;
    this.assignTerritoryId = this.availableTerritories[0]?.territoryId ?? null;

    if (this.allTerritories.length === 0 && !this.isLoadingTerritories) {
      this.loadTerritories();
    }

    this.cdr.markForCheck();
  }

  closeAssignTerritoryManager(): void {
    if (this.isAssigning) {
      return;
    }

    this.assignTarget = null;
    this.assignTerritoryId = null;
    this.cdr.markForCheck();
  }

  confirmAssignTerritoryManager(): void {
    if (!this.assignTarget || !this.assignTerritoryId) {
      return;
    }

    const targetUserId = this.assignTarget.userId;
    this.isAssigning = true;
    this.errorMessage = '';

    this.appUsersSvc.assignTerritoryManager$(this.assignTarget.userId, {
      territoryId: this.assignTerritoryId,
    }).pipe(
      takeUntil(this.destroy$),
      finalize(() => {
        this.isAssigning = false;
        this.cdr.markForCheck();
      }),
    ).subscribe({
      next: () => {
        this.assignTarget = null;
        this.assignTerritoryId = null;
        this.territorySvc.invalidateMapCache();
        this.loadTerritories();
        this.showSuccess('Territory Manager assigned.');
        this.resetAndLoad();
        this.refreshPreviewIfOpen(targetUserId);
      },
      error: error => {
        this.errorMessage = this.resolveErrorMessage(error);
      },
    });
  }

  hasRowActions(user: AppUser): boolean {
    return user.canAssignTerritoryManager || user.canSuspend || user.canUnsuspend || user.canDelete;
  }

  hasPreviewActions(detail: AdminUserPreview): boolean {
    const permissions = detail.permissions;
    return permissions.canAssignTerritoryManager ||
      permissions.canSuspend ||
      permissions.canUnsuspend ||
      permissions.canDelete;
  }

  previewActionUser(detail: AdminUserPreview): AppUser {
    const user = detail.user;
    const summary = detail.accountSummary;
    const permissions = detail.permissions;

    return {
      userId: user.userId,
      id: user.id,
      displayName: user.displayName,
      firstName: user.firstName,
      lastName: user.lastName,
      email: user.email,
      avatarUrl: user.avatarUrl,
      initials: user.initials,
      avatarInitials: user.avatarInitials,
      avatarColor: user.avatarColor,
      status: user.status,
      roles: user.roles,
      isAdmin: user.isAdmin,
      isCurrentUser: user.isCurrentUser,
      reviewsCount: summary.reviewsCount,
      reportedCount: summary.reportedContentCount,
      reviewCount: summary.reviewsCount,
      reportedReviewCount: summary.reportedContentCount,
      joinedAt: user.joinedAt,
      lastActiveAt: user.lastActiveAt,
      assignedTerritories: user.assignedTerritories,
      assignedTerritoryCount: user.assignedTerritories.length,
      canAssignTerritoryManager: permissions.canAssignTerritoryManager,
      canSuspend: permissions.canSuspend,
      canUnsuspend: permissions.canUnsuspend,
      canDelete: permissions.canDelete,
    };
  }

  formatDate(value: string | null | undefined): string {
    return this.appUsersSvc.formatDate(value);
  }

  formatDateTime(value: string | null | undefined): string {
    if (!value) {
      return 'Never';
    }

    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return value;
    }

    return date.toLocaleString('en-US', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  statusClass(status: AppUser['status']): string {
    if (status === 'Active') {
      return 'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200';
    }
    if (status === 'Suspended') {
      return 'bg-amber-50 text-amber-700 ring-1 ring-amber-200';
    }

    return 'bg-gray-100 text-gray-500 ring-1 ring-gray-200';
  }

  roleLabel(role: string): string {
    return this.labelize(role);
  }

  roleClass(role: string): string {
    if (role === 'Admin') {
      return 'bg-red-50 text-red-700 ring-1 ring-red-100';
    }
    if (role === 'RegionalManager' || role === 'TerritoryManager') {
      return 'bg-blue-50 text-blue-700 ring-1 ring-blue-100';
    }
    if (role === 'TerritoryAssociate') {
      return 'bg-cyan-50 text-cyan-700 ring-1 ring-cyan-100';
    }

    return 'bg-gray-100 text-gray-600 ring-1 ring-gray-200';
  }

  riskClass(level: string): string {
    if (level === 'High') {
      return 'bg-red-50 text-red-700 ring-1 ring-red-200';
    }
    if (level === 'Medium') {
      return 'bg-amber-50 text-amber-700 ring-1 ring-amber-200';
    }

    return 'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200';
  }

  reportStatusClass(status: string): string {
    if (status === 'Accepted') {
      return 'bg-red-50 text-red-700 ring-1 ring-red-100';
    }
    if (status === 'Rejected') {
      return 'bg-gray-100 text-gray-600 ring-1 ring-gray-200';
    }

    return 'bg-amber-50 text-amber-700 ring-1 ring-amber-200';
  }

  reviewStatusClass(status: string): string {
    if (status === 'Removed' || status === 'Hidden') {
      return 'bg-red-50 text-red-700 ring-1 ring-red-100';
    }

    return 'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200';
  }

  historyStatusClass(status: string): string {
    if (status === 'Deleted' || status === 'Removed' || status === 'Suspended') {
      return 'bg-red-50 text-red-700 ring-1 ring-red-100';
    }
    if (status === 'Rejected' || status === 'Dismissed') {
      return 'bg-gray-100 text-gray-600 ring-1 ring-gray-200';
    }
    if (status === 'Accepted' || status === 'Approved' || status === 'Unsuspended') {
      return 'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200';
    }

    return 'bg-blue-50 text-blue-700 ring-1 ring-blue-100';
  }

  previewTabCount(tab: AdminUserPreviewTab, detail: AdminUserPreview): number {
    if (tab === 'reports') {
      return detail.reportsMade.totalCount + detail.reportsAgainstUser.totalCount;
    }
    if (tab === 'reviews') {
      return detail.recentReviews.totalCount;
    }
    if (tab === 'history') {
      return detail.moderationHistory.totalCount;
    }

    return 0;
  }

  actionTitle(action: ConfirmAction): string {
    if (action === 'delete') {
      return 'Deactivate Account';
    }
    return action === 'suspend' ? 'Suspend Account' : 'Unsuspend Account';
  }

  actionButtonLabel(action: ConfirmAction): string {
    if (this.isActionSaving) {
      return 'Saving...';
    }
    if (action === 'delete') {
      return 'Deactivate';
    }
    return action === 'suspend' ? 'Suspend' : 'Unsuspend';
  }

  actionBody(state: ConfirmationState): string {
    if (state.action === 'delete') {
      return 'This account will be deactivated, hidden from normal active lists, and existing contributions will remain for audit history.';
    }
    if (state.action === 'suspend') {
      return 'This account will keep its data, but the user will not be able to sign in or use protected app areas until unsuspended.';
    }

    return 'This account will regain access to protected app areas.';
  }

  assignedTerritorySummary(user: AppUser): string {
    if (user.assignedTerritories.length === 0) {
      return 'No territory assigned';
    }

    const first = user.assignedTerritories[0].displayName || user.assignedTerritories[0].territoryName;
    const remaining = user.assignedTerritories.length - 1;
    return remaining > 0 ? `${first} +${remaining}` : first;
  }

  countryName(code: string | null | undefined): string {
    if (!code) {
      return '';
    }

    return this.territorySvc.getCountryByCode(code)?.name ?? code;
  }

  territoryOptionLabel(territory: Territory): string {
    const code = territory.countryCode ?? territory.countries[0] ?? territory.iso2 ?? territory.iso3 ?? '';
    const country = this.countryName(code);
    return country ? `${territory.name} (${country})` : territory.name;
  }

  trackById(_: number, user: AppUser): string {
    return user.id;
  }

  trackByTerritoryId(_: number, territory: Territory): string {
    return territory.id;
  }

  trackByRoleName(_: number, role: { roleName: string }): string {
    return role.roleName;
  }

  trackByReportId(_: number, report: AdminUserReportPreview): number {
    return report.reportId;
  }

  trackByReviewId(_: number, review: AdminUserRecentReview): string {
    return `${review.targetType}-${review.reviewId}`;
  }

  trackByHistoryId(_: number, item: AdminUserModerationHistoryItem): string {
    return item.eventId;
  }

  private loadUserPreview(userId: number, showLoading = true): void {
    const sequence = ++this.previewLoadSequence;
    this.previewErrorMessage = '';

    if (showLoading) {
      this.isPreviewLoading = true;
    }

    this.appUsersSvc.getPreview$(userId).pipe(
      takeUntil(this.destroy$),
      finalize(() => {
        if (sequence === this.previewLoadSequence) {
          this.isPreviewLoading = false;
          this.cdr.markForCheck();
        }
      }),
    ).subscribe({
      next: preview => {
        if (sequence !== this.previewLoadSequence) {
          return;
        }

        this.preview = preview;
        this.previewTarget = this.previewActionUser(preview);
      },
      error: error => {
        if (sequence !== this.previewLoadSequence) {
          return;
        }

        this.previewErrorMessage = this.resolveErrorMessage(error);
      },
    });
  }

  private refreshPreviewIfOpen(userId: number): void {
    if (this.previewTarget?.userId === userId) {
      this.loadUserPreview(userId, false);
    }
  }

  private resetAndLoad(): void {
    this.nextSkip = null;
    this.hasMore = false;
    this.users = [];
    this.loadUsers(true);
  }

  private loadUsers(reset: boolean): void {
    const skip = reset ? 0 : this.nextSkip ?? this.users.length;
    const sequence = ++this.loadSequence;
    this.errorMessage = '';

    if (reset) {
      this.isLoading = true;
    } else {
      this.isLoadingMore = true;
    }

    this.appUsersSvc.getPage$({
      tab: this.activeTab,
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

        this.users = reset ? page.items : [...this.users, ...page.items];
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

  private loadPendingReportsBadge(): void {
    this.dashboardSvc.loadDashboard()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: dashboard => {
          this.supportReportsCount = dashboard.badges.supportReportsCount;
          this.cdr.markForCheck();
        },
        error: () => {
          this.supportReportsCount = 0;
          this.cdr.markForCheck();
        },
      });
  }

  private loadTerritories(): void {
    this.isLoadingTerritories = true;
    this.territorySvc.getPage$({
      page: 1,
      pageSize: 500,
      search: '',
      sortField: 'name',
      sortDir: 'asc',
      countryFilter: [],
    }).pipe(
      takeUntil(this.destroy$),
      finalize(() => {
        this.isLoadingTerritories = false;
        this.cdr.markForCheck();
      }),
    ).subscribe({
      next: page => {
        this.allTerritories = page.items ?? [];
        if (!this.assignTerritoryId) {
          this.assignTerritoryId = this.availableTerritories[0]?.territoryId ?? null;
        }
      },
      error: () => {
        this.allTerritories = [];
      },
    });
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

  private labelize(value: string): string {
    return (value || 'User')
      .replace(/([a-z])([A-Z])/g, '$1 $2')
      .replace(/_/g, ' ');
  }
}
