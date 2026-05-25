import {
  Component, OnInit, OnDestroy, HostListener,
  ChangeDetectionStrategy, ChangeDetectorRef, inject,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject } from 'rxjs';
import { takeUntil, debounceTime, distinctUntilChanged } from 'rxjs/operators';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { AppUsersService } from '../../../../core/services/administrator/app-users.service';
import { AppUser, AppUsersPageRequest, AppUserSortField, AppUserSortDir } from '../../../../core/models/administrator/app-user.model';
import { TerritoryService } from '../../../../core/services/administrator/territory.service';
import { AdminDashboardService } from '../../../../core/services/administrator/admin-dashboard.service';
import { TerritoryManager, Territory } from '../../../../core/models/administrator/territory.model';

type ActiveTab = 'appUsers' | 'managers';

const PAGE_SIZE_INITIAL = 10;
const PAGE_SIZE_STEP    = 5;

@Component({
  selector: 'app-user-management',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './user-management.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserManagementComponent implements OnInit, OnDestroy {
  private readonly appUsersSvc  = inject(AppUsersService);
  private readonly territorySvc = inject(TerritoryService);
  private readonly dashboardSvc = inject(AdminDashboardService);
  private readonly cdr          = inject(ChangeDetectorRef);

  reportedReviewsCount = 0;
  private destroy$      = new Subject<void>();
  private searchSubject = new Subject<string>();

  // ── Tabs ─────────────────────────────────────────────────────────────────
  activeTab: ActiveTab = 'appUsers';

  // ── App users ─────────────────────────────────────────────────────────────
  appUsers: AppUser[]  = [];
  totalAppUsers        = 0;
  isLoading            = false;
  isLoadingMore        = false;
  displayCount         = PAGE_SIZE_INITIAL;
  searchQuery          = '';
  statusFilter: 'all' | 'active' | 'suspended' = 'all';
  statusFilterOpen     = false;
  sortField: AppUserSortField = 'joinedAt';
  sortDir: AppUserSortDir     = 'desc';
  sortOpen                    = false;

  sortOptions: { label: string; field: AppUserSortField; dir: AppUserSortDir }[] = [
    { label: 'Most Reported First',  field: 'reportedReviewCount', dir: 'desc' },
    { label: 'Most Reviews First',   field: 'reviewCount',         dir: 'desc' },
    { label: 'Newest Members',       field: 'joinedAt',            dir: 'desc' },
    { label: 'Oldest Members',       field: 'joinedAt',            dir: 'asc'  },
    { label: 'Status: Active First', field: 'status',              dir: 'asc'  },
    { label: 'A–Z',                  field: 'name',                dir: 'asc'  },
    { label: 'Z–A',                  field: 'name',                dir: 'desc' },
  ];

  get currentSortLabel(): string {
    return this.sortOptions.find(o => o.field === this.sortField && o.dir === this.sortDir)?.label ?? 'Newest Members';
  }

  get statusFilterLabel(): string {
    if (this.statusFilter === 'all')       return 'All statuses';
    if (this.statusFilter === 'active')    return 'Active';
    return 'Suspended';
  }

  // ── National Managers ────────────────────────────────────────────────────
  managers: TerritoryManager[]     = [];
  allTerritories: Territory[]      = [];
  managerSearch                    = '';
  managerStatusFilter: 'all' | 'active' | 'inactive' = 'all';
  managerStatusFilterOpen          = false;

  get filteredManagers(): TerritoryManager[] {
    return this.managers.filter(m => {
      const matchSearch = !this.managerSearch
        || [m.name, m.email].join(' ').toLowerCase().includes(this.managerSearch.toLowerCase());
      const matchStatus = this.managerStatusFilter === 'all'
        || (this.managerStatusFilter === 'active'   && m.status === 'Active')
        || (this.managerStatusFilter === 'inactive' && m.status === 'Inactive');
      return matchSearch && matchStatus;
    });
  }

  get managerStatusFilterLabel(): string {
    if (this.managerStatusFilter === 'all')      return 'All statuses';
    if (this.managerStatusFilter === 'active')   return 'Active';
    return 'Inactive';
  }

  // ── Manager detail ────────────────────────────────────────────────────────
  selectedManager: TerritoryManager | null = null;
  managerTerritoryTarget: Territory | null  = null;

  get selectedManagerTerritories(): Territory[] {
    if (!this.selectedManager) return [];
    return this.allTerritories.filter(t => t.assignedManagerId === this.selectedManager!.id);
  }

  // ── Dropdowns ────────────────────────────────────────────────────────────
  openDropdownId: string | null = null;
  dropdownPos = { top: 0, right: 0 };

  // ── Delete confirm ────────────────────────────────────────────────────────
  deleteAppUser:  AppUser | null          = null;
  deleteManager:  TerritoryManager | null = null;
  isDeleting                              = false;

  // ── Suspend confirm ───────────────────────────────────────────────────────
  suspendTarget: AppUser | null = null;
  isSuspending                  = false;

  // ── Make Territory Manager ────────────────────────────────────────────────
  makeTerritoryManagerTarget: AppUser | null = null;
  isMakingManager                            = false;

  // ── Remove Territory Manager role ─────────────────────────────────────────
  removeTerritoryManagerTarget: TerritoryManager | null = null;
  isRemovingManagerRole                                 = false;

  // ── Manager status change ─────────────────────────────────────────────────
  activateTarget:   TerritoryManager | null = null;
  deactivateTarget: TerritoryManager | null = null;
  isChangingStatus                          = false;

  ngOnInit(): void {
    this.loadPendingReportsBadge();
    this.managers = this.territorySvc.getAllManagers();

    this.territorySvc.getPage$({ page: 1, pageSize: 1000, search: '', sortField: 'createdAt', sortDir: 'desc', countryFilter: [] })
      .pipe(takeUntil(this.destroy$))
      .subscribe(page => {
        this.allTerritories = page.items;
        this.cdr.detectChanges();
      });

    this.searchSubject.pipe(
      debounceTime(300),
      distinctUntilChanged(),
      takeUntil(this.destroy$),
    ).subscribe(() => {
      this.loadAppUsers();
    });

    this.loadAppUsers();
  }

  ngOnDestroy(): void { this.destroy$.next(); this.destroy$.complete(); }

  private loadPendingReportsBadge(): void {
    this.dashboardSvc.loadDashboard()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: dashboard => {
          this.reportedReviewsCount = dashboard.badges.pendingReportsCount;
          this.cdr.markForCheck();
        },
        error: () => {
          this.reportedReviewsCount = 0;
          this.cdr.markForCheck();
        },
      });
  }

  @HostListener('document:click')
  onDocumentClick(): void {
    let changed = false;
    if (this.openDropdownId)          { this.openDropdownId = null;              changed = true; }
    if (this.sortOpen)                { this.sortOpen = false;                   changed = true; }
    if (this.statusFilterOpen)        { this.statusFilterOpen = false;           changed = true; }
    if (this.managerStatusFilterOpen) { this.managerStatusFilterOpen = false;    changed = true; }
    if (changed) this.cdr.detectChanges();
  }

  // ── App users ─────────────────────────────────────────────────────────────
  buildReq(): AppUsersPageRequest {
    return { page: 1, pageSize: this.displayCount, search: this.searchQuery, sortField: this.sortField, sortDir: this.sortDir, statusFilter: this.statusFilter };
  }

  loadAppUsers(): void {
    this.isLoading = true;
    this.appUsersSvc.getPage$(this.buildReq())
      .pipe(takeUntil(this.destroy$))
      .subscribe(page => {
        this.appUsers      = page.items;
        this.totalAppUsers = page.total;
        this.isLoading     = false;
        this.cdr.detectChanges();
      });
  }

  loadMore(): void {
    this.isLoadingMore = true;
    this.displayCount += PAGE_SIZE_STEP;
    this.appUsersSvc.getPage$(this.buildReq())
      .pipe(takeUntil(this.destroy$))
      .subscribe(page => {
        this.appUsers      = page.items;
        this.totalAppUsers = page.total;
        this.isLoadingMore = false;
        this.cdr.detectChanges();
      });
  }

  get hasMore(): boolean { return this.appUsers.length < this.totalAppUsers; }

  onSearchChange(): void { this.searchSubject.next(this.searchQuery); }

  applyStatusFilter(value: string): void {
    this.statusFilter     = value as 'all' | 'active' | 'suspended';
    this.statusFilterOpen = false;
    this.loadAppUsers();
  }

  applyManagerStatusFilter(value: string): void {
    this.managerStatusFilter     = value as 'all' | 'active' | 'inactive';
    this.managerStatusFilterOpen = false;
    this.cdr.detectChanges();
  }

  applySortOption(opt: { label: string; field: AppUserSortField; dir: AppUserSortDir }): void {
    this.sortField = opt.field;
    this.sortDir   = opt.dir;
    this.sortOpen  = false;
    this.loadAppUsers();
  }

  // ── Row dropdown ─────────────────────────────────────────────────────────
  toggleDropdown(id: string, event: MouseEvent): void {
    event.stopPropagation();
    if (this.openDropdownId === id) {
      this.openDropdownId = null;
    } else {
      const rect           = (event.currentTarget as HTMLElement).getBoundingClientRect();
      const dropdownHeight = 140;
      const spaceBelow     = window.innerHeight - rect.bottom;
      const top = spaceBelow < dropdownHeight ? rect.top - dropdownHeight - 4 : rect.bottom + 4;
      this.dropdownPos    = { top, right: window.innerWidth - rect.right };
      this.openDropdownId = id;
    }
    this.cdr.detectChanges();
  }

  // ── Suspend ───────────────────────────────────────────────────────────────
  openSuspendConfirm(user: AppUser, event?: MouseEvent): void {
    event?.stopPropagation();
    this.openDropdownId = null;
    this.suspendTarget  = { ...user };
    this.cdr.detectChanges();
  }

  closeSuspendConfirm(): void { this.suspendTarget = null; this.cdr.detectChanges(); }

  confirmSuspend(): void {
    if (!this.suspendTarget) return;
    this.isSuspending = true;
    this.appUsersSvc.suspend$(this.suspendTarget.id)
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => {
        this.isSuspending  = false;
        this.suspendTarget = null;
        this.loadAppUsers();
      });
  }

  // ── Delete app user ───────────────────────────────────────────────────────
  openDeleteAppUser(user: AppUser, event?: MouseEvent): void {
    event?.stopPropagation();
    this.openDropdownId = null;
    this.deleteAppUser  = { ...user };
    this.cdr.detectChanges();
  }

  closeDeleteAppUser(): void { this.deleteAppUser = null; this.cdr.detectChanges(); }

  confirmDeleteAppUser(): void {
    if (!this.deleteAppUser) return;
    this.isDeleting = true;
    this.appUsersSvc.delete$(this.deleteAppUser.id)
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => {
        this.isDeleting    = false;
        this.deleteAppUser = null;
        this.loadAppUsers();
      });
  }

  // ── Make Territory Manager ────────────────────────────────────────────────
  openMakeTerritoryManager(user: AppUser, event?: MouseEvent): void {
    event?.stopPropagation();
    this.openDropdownId = null;
    this.makeTerritoryManagerTarget = { ...user };
    this.cdr.detectChanges();
  }

  closeMakeTerritoryManager(): void { this.makeTerritoryManagerTarget = null; this.cdr.detectChanges(); }

  confirmMakeTerritoryManager(): void {
    if (!this.makeTerritoryManagerTarget) return;
    this.isMakingManager = true;
    const user = this.makeTerritoryManagerTarget;
    this.territorySvc.addManager$({ id: user.id, firstName: user.firstName, lastName: user.lastName, email: user.email })
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => {
        this.managers = this.territorySvc.getAllManagers();
        this.appUsers = this.appUsers.filter(u => u.id !== user.id);
        this.totalAppUsers = Math.max(0, this.totalAppUsers - 1);
        this.isMakingManager = false;
        this.makeTerritoryManagerTarget = null;
        this.cdr.detectChanges();
      });
  }

  isAlreadyManager(userId: string): boolean {
    return this.territorySvc.isAlreadyManager(userId);
  }

  // ── Remove Territory Manager role ─────────────────────────────────────────
  openRemoveTerritoryManager(mgr: TerritoryManager, event?: MouseEvent): void {
    event?.stopPropagation();
    this.openDropdownId = null;
    this.removeTerritoryManagerTarget = { ...mgr };
    this.cdr.detectChanges();
  }

  closeRemoveTerritoryManager(): void { this.removeTerritoryManagerTarget = null; this.cdr.detectChanges(); }

  confirmRemoveTerritoryManager(): void {
    if (!this.removeTerritoryManagerTarget) return;
    this.isRemovingManagerRole = true;
    const id = this.removeTerritoryManagerTarget.id;
    this.territorySvc.removeManager$(id)
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => {
        this.managers = this.territorySvc.getAllManagers();
        this.isRemovingManagerRole = false;
        this.removeTerritoryManagerTarget = null;
        this.loadAppUsers();
      });
  }

  // ── Delete manager ────────────────────────────────────────────────────────
  openDeleteManager(mgr: TerritoryManager, event?: MouseEvent): void {
    event?.stopPropagation();
    this.openDropdownId = null;
    this.deleteManager  = { ...mgr };
    this.cdr.detectChanges();
  }

  closeDeleteManager(): void { this.deleteManager = null; this.cdr.detectChanges(); }

  confirmDeleteManager(): void {
    if (!this.deleteManager) return;
    this.isDeleting = true;
    setTimeout(() => {
      this.managers      = this.managers.filter(m => m.id !== this.deleteManager!.id);
      this.isDeleting    = false;
      this.deleteManager = null;
      this.cdr.detectChanges();
    }, 500);
  }

  // ── Activate manager ──────────────────────────────────────────────────────
  openActivateManager(mgr: TerritoryManager, event?: MouseEvent): void {
    event?.stopPropagation();
    this.openDropdownId = null;
    this.activateTarget = { ...mgr };
    this.cdr.detectChanges();
  }

  closeActivateManager(): void { this.activateTarget = null; this.cdr.detectChanges(); }

  confirmActivateManager(): void {
    if (!this.activateTarget) return;
    this.isChangingStatus = true;
    const id = this.activateTarget.id;
    this.territorySvc.setManagerStatus$(id, 'Active')
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => {
        const idx = this.managers.findIndex(m => m.id === id);
        if (idx !== -1) this.managers[idx] = { ...this.managers[idx], status: 'Active' };
        this.isChangingStatus = false;
        this.activateTarget   = null;
        this.cdr.detectChanges();
      });
  }

  // ── Deactivate manager ────────────────────────────────────────────────────
  openDeactivateManager(mgr: TerritoryManager, event?: MouseEvent): void {
    event?.stopPropagation();
    this.openDropdownId   = null;
    this.deactivateTarget = { ...mgr };
    this.cdr.detectChanges();
  }

  closeDeactivateManager(): void { this.deactivateTarget = null; this.cdr.detectChanges(); }

  confirmDeactivateManager(): void {
    if (!this.deactivateTarget) return;
    this.isChangingStatus = true;
    const id = this.deactivateTarget.id;
    this.territorySvc.setManagerStatus$(id, 'Inactive')
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => {
        const idx = this.managers.findIndex(m => m.id === id);
        if (idx !== -1) this.managers[idx] = { ...this.managers[idx], status: 'Inactive' };
        this.isChangingStatus = false;
        this.deactivateTarget = null;
        this.cdr.detectChanges();
      });
  }

  // ── Manager detail ────────────────────────────────────────────────────────
  openManagerDetail(mgr: TerritoryManager, event: MouseEvent): void {
    const target = event.target as HTMLElement;
    if (target.closest('[data-action="menu"]')) return;
    this.selectedManager = mgr;
    this.cdr.detectChanges();
  }

  closeManagerDetail(): void { this.selectedManager = null; this.cdr.detectChanges(); }

  openTerritoryFromManager(t: Territory, event: MouseEvent): void {
    event.stopPropagation();
    this.managerTerritoryTarget = t;
    this.cdr.detectChanges();
  }

  closeTerritoryFromManager(): void { this.managerTerritoryTarget = null; this.cdr.detectChanges(); }

  // ── Helpers ───────────────────────────────────────────────────────────────
  formatDate(iso: string): string { return this.appUsersSvc.formatDate(iso); }

  formatTerritoryDate(iso: string): string {
    return new Date(iso).toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
  }

  statusClass(status: string): string {
    return status === 'Active'
      ? 'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200'
      : 'bg-red-50 text-red-600 ring-1 ring-red-200';
  }

  territoryStatusClass(status: string): string {
    return status === 'Active'
      ? 'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200'
      : 'bg-amber-50 text-amber-700 ring-1 ring-amber-200';
  }

  countryName(code: string): string { return this.territorySvc.getCountryByCode(code)?.name ?? code; }
  countryFlag(code: string): string { return this.territorySvc.getCountryByCode(code)?.flag ?? ''; }

  managerInitials(managerId: string | null): string {
    if (!managerId) return '?';
    return this.managers.find(m => m.id === managerId)?.initials ?? '?';
  }

  managerColor(managerId: string | null): string {
    if (!managerId) return '#9ca3af';
    return this.managers.find(m => m.id === managerId)?.avatarColor ?? '#9ca3af';
  }

  trackById(_: number, u: AppUser): string { return u.id; }
  trackByMgrId(_: number, m: TerritoryManager): string { return m.id; }
}
