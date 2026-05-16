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
import { AdminReviewsService } from '../../../../core/services/administrator/admin-reviews.service';
import { TerritoryManager } from '../../../../core/models/administrator/territory.model';

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
  private readonly revSvc       = inject(AdminReviewsService);
  private readonly cdr          = inject(ChangeDetectorRef);

  get reportedReviewsCount(): number { return this.revSvc.getReportedCount(); }
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
  sortField: AppUserSortField = 'reportedReviewCount';
  sortDir: AppUserSortDir     = 'desc';
  sortOpen                    = false;

  sortOptions: { label: string; field: AppUserSortField; dir: AppUserSortDir }[] = [
    { label: 'Most Reported First',  field: 'reportedReviewCount', dir: 'desc' },
    { label: 'Most Reviews First',   field: 'reviewCount',         dir: 'desc' },
    { label: 'Newest Members',       field: 'joinedAt',            dir: 'desc' },
    { label: 'Oldest Members',       field: 'joinedAt',            dir: 'asc'  },
    { label: 'A–Z',                  field: 'name',                dir: 'asc'  },
    { label: 'Z–A',                  field: 'name',                dir: 'desc' },
    { label: 'Status: Active First', field: 'status',              dir: 'asc'  },
  ];

  get currentSortLabel(): string {
    return this.sortOptions.find(o => o.field === this.sortField && o.dir === this.sortDir)?.label ?? 'Sort';
  }

  // ── National Managers ────────────────────────────────────────────────────
  managers: TerritoryManager[]     = [];
  managerSearch                    = '';
  managerStatusFilter: 'all' | 'active' | 'inactive' = 'all';
  managerSortOpen                  = false;

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

  // ── Dropdowns ────────────────────────────────────────────────────────────
  openDropdownId: string | null = null;
  dropdownPos = { top: 0, right: 0 };

  // ── Delete confirm ────────────────────────────────────────────────────────
  deleteAppUser:  AppUser | null         = null;
  deleteManager:  TerritoryManager | null = null;
  isDeleting                             = false;

  // ── Suspend confirm ───────────────────────────────────────────────────────
  suspendTarget: AppUser | null = null;
  isSuspending                  = false;

  ngOnInit(): void {
    this.managers = this.territorySvc.getAllManagers();

    this.searchSubject.pipe(
      debounceTime(300),
      distinctUntilChanged(),
      takeUntil(this.destroy$),
    ).subscribe(() => {
      this.displayCount = PAGE_SIZE_INITIAL;
      this.loadAppUsers();
    });

    this.loadAppUsers();
  }

  ngOnDestroy(): void { this.destroy$.next(); this.destroy$.complete(); }

  @HostListener('document:click')
  onDocumentClick(): void {
    let changed = false;
    if (this.openDropdownId) { this.openDropdownId = null; changed = true; }
    if (this.sortOpen)       { this.sortOpen = false;       changed = true; }
    if (changed) this.cdr.detectChanges();
  }

  // ── App users ─────────────────────────────────────────────────────────────
  buildReq(): AppUsersPageRequest {
    return { pageSize: this.displayCount, search: this.searchQuery, sortField: this.sortField, sortDir: this.sortDir, statusFilter: this.statusFilter };
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

  onStatusFilterChange(): void { this.displayCount = PAGE_SIZE_INITIAL; this.loadAppUsers(); }

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
      const dropdownHeight = 120;
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
    // Remove from local list (mock)
    setTimeout(() => {
      this.managers       = this.managers.filter(m => m.id !== this.deleteManager!.id);
      this.isDeleting     = false;
      this.deleteManager  = null;
      this.cdr.detectChanges();
    }, 500);
  }

  // ── Helpers ───────────────────────────────────────────────────────────────
  formatDate(iso: string): string { return this.appUsersSvc.formatDate(iso); }

  statusClass(status: string): string {
    return status === 'Active'
      ? 'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200'
      : 'bg-red-50 text-red-600 ring-1 ring-red-200';
  }

  trackById(_: number, u: AppUser): string { return u.id; }
  trackByMgrId(_: number, m: TerritoryManager): string { return m.id; }
}
