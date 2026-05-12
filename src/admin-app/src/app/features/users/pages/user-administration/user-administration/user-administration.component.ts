import {
  Component, OnInit, OnDestroy, HostListener,
  ChangeDetectionStrategy, ChangeDetectorRef,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subject } from 'rxjs';
import { debounceTime, distinctUntilChanged, takeUntil } from 'rxjs/operators';
import { SidebarComponent } from '../../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../../shared/components/topbar/topbar.component';
import { UsersService, UserSortField, UserSortDir } from '../../../../../core/services/national/users.service';
import { PlatformUser } from '../../../../../core/models/platform-user.model';

const PAGE_SIZE_INITIAL = 8;
const PAGE_SIZE_STEP    = 5;

@Component({
  selector: 'app-user-administration',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './user-administration.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserAdministrationComponent implements OnInit, OnDestroy {

  constructor(
    private router: Router,
    private svc: UsersService,
    private cdr: ChangeDetectorRef,
  ) {}

  private destroy$      = new Subject<void>();
  private searchSubject = new Subject<string>();

  userName = 'Sarah Jenkins';
  userRole = 'National Manager';

  items:        PlatformUser[] = [];
  totalItems    = 0;
  isLoading     = false;
  isLoadingMore = false;
  displayCount  = PAGE_SIZE_INITIAL;

  searchQuery = '';

  sortField: UserSortField = 'status';
  sortDir:   UserSortDir   = 'asc';
  sortOpen   = false;

  sortOptions: { label: string; field: UserSortField; dir: UserSortDir }[] = [
    { label: 'Status: Active First',   field: 'status', dir: 'asc'  },
    { label: 'Status: Inactive First', field: 'status', dir: 'desc' },
    { label: 'A–Z',                    field: 'name',   dir: 'asc'  },
    { label: 'Z–A',                    field: 'name',   dir: 'desc' },
    { label: 'Role: Viewer First',     field: 'role',   dir: 'asc'  },
    { label: 'Role: Manager First',    field: 'role',   dir: 'desc' },
  ];

  get currentSortLabel(): string {
    return this.sortOptions.find(
      o => o.field === this.sortField && o.dir === this.sortDir
    )?.label ?? 'Status: Active First';
  }

  applySortOption(opt: { label: string; field: UserSortField; dir: UserSortDir }): void {
    this.sortField    = opt.field;
    this.sortDir      = opt.dir;
    this.displayCount = PAGE_SIZE_INITIAL;
    this.loadItems();
  }

  // Dropdown per row
  openDropdownId: string | null = null;
  dropdownPos = { top: 0, right: 0 };

  // View modal (read-only)
  viewUser: PlatformUser | null = null;

  openViewModal(user: PlatformUser): void {
    this.openDropdownId = null;
    this.viewUser = { ...user };
    this.cdr.detectChanges();
  }

  closeViewModal(): void {
    this.viewUser = null;
    this.cdr.detectChanges();
  }

  // Edit modal
  editUser:      PlatformUser | null = null;
  isSaving       = false;
  isTogglingRole = false;

  // Add region modal
  showAddRegionModal = false;
  addRegionTarget:   PlatformUser | null = null;
  allRegions:        string[] = [];
  isAddingRegion     = false;

  // Create user modal
  showCreateModal = false;
  newFirstName    = '';
  newLastName     = '';
  newEmail        = '';
  newRole: 'Regional Manager' | 'Viewer' = 'Viewer';
  isCreating      = false;

  ngOnInit(): void {
    this.allRegions = this.svc.getAllRegions();

    this.searchSubject.pipe(
      debounceTime(300),
      distinctUntilChanged(),
      takeUntil(this.destroy$),
    ).subscribe(() => {
      this.displayCount = PAGE_SIZE_INITIAL;
      this.loadItems();
    });

    const cached = this.svc.lastPage$.getValue();
    if (cached) {
      this.items      = cached.items;
      this.totalItems = cached.total;
      this.isLoading  = false;
      this.cdr.detectChanges();
    }

    this.loadItems();
  }

  ngOnDestroy(): void { this.destroy$.next(); this.destroy$.complete(); }

  @HostListener('document:click')
  onDocumentClick(): void {
    let changed = false;
    if (this.openDropdownId) { this.openDropdownId = null; changed = true; }
    if (this.sortOpen)       { this.sortOpen = false;       changed = true; }
    if (changed) this.cdr.detectChanges();
  }

  loadItems(): void {
    this.isLoading = true;
    this.svc.getPage$({
      pageSize:  this.displayCount,
      search:    this.searchQuery,
      sortField: this.sortField,
      sortDir:   this.sortDir,
    }).pipe(takeUntil(this.destroy$)).subscribe(page => {
      this.items      = page.items;
      this.totalItems = page.total;
      this.isLoading  = false;
      this.cdr.detectChanges();
    });
  }

  loadMore(): void {
    this.isLoadingMore = true;
    this.displayCount += PAGE_SIZE_STEP;
    this.svc.getPage$({
      pageSize:  this.displayCount,
      search:    this.searchQuery,
      sortField: this.sortField,
      sortDir:   this.sortDir,
    }).pipe(takeUntil(this.destroy$)).subscribe(page => {
      this.items         = page.items;
      this.totalItems    = page.total;
      this.isLoadingMore = false;
      this.cdr.detectChanges();
    });
  }

  get hasMore(): boolean { return this.items.length < this.totalItems; }

  onSearchChange(): void { this.searchSubject.next(this.searchQuery); }

  formatLastLogin(iso: string): string { return this.svc.formatLastLogin(iso); }

  // ── Dropdown ────────────────────────────────────────────────────────────────

  toggleDropdown(id: string, event: MouseEvent): void {
    event.stopPropagation();
    if (this.openDropdownId === id) {
      this.openDropdownId = null;
    } else {
      const rect           = (event.currentTarget as HTMLElement).getBoundingClientRect();
      const dropdownHeight = 170;
      const spaceBelow     = window.innerHeight - rect.bottom;
      const top = spaceBelow < dropdownHeight
        ? rect.top - dropdownHeight - 4
        : rect.bottom + 4;
      this.dropdownPos = { top, right: window.innerWidth - rect.right };
      this.openDropdownId = id;
    }
    this.cdr.detectChanges();
  }

  // ── Edit modal (full-screen) ──────────────────────────────────────────────

  openEditModal(user: PlatformUser, event?: MouseEvent): void {
    event?.stopPropagation();
    this.openDropdownId = null;
    this.editUser = { ...user };
    this.cdr.detectChanges();
  }

  closeEditModal(): void {
    this.editUser = null;
    this.cdr.detectChanges();
  }

  toggleRole(): void {
    if (!this.editUser) return;
    this.isTogglingRole = true;

    const obs = this.editUser.role === 'Regional Manager'
      ? this.svc.removeManagerRole$(this.editUser.id)
      : this.svc.promoteToManager$(this.editUser.id);

    obs.pipe(takeUntil(this.destroy$)).subscribe(updated => {
      if (updated) {
        this.editUser = { ...updated };
        this.syncItemInList(updated);
      }
      this.isTogglingRole = false;
      this.cdr.detectChanges();
    });
  }

  removeRegionFromEdit(region: string): void {
    if (!this.editUser) return;
    this.svc.removeRegion$(this.editUser.id, region)
      .pipe(takeUntil(this.destroy$))
      .subscribe(updated => {
        if (updated) {
          this.editUser = { ...updated };
          this.syncItemInList(updated);
        }
        this.cdr.detectChanges();
      });
  }

  // ── Add region modal ─────────────────────────────────────────────────────

  openAddRegionModal(user: PlatformUser, event?: MouseEvent): void {
    event?.stopPropagation();
    this.openDropdownId    = null;
    this.addRegionTarget   = { ...user };
    this.showAddRegionModal = true;
    this.cdr.detectChanges();
  }

  closeAddRegionModal(): void {
    this.showAddRegionModal = false;
    this.addRegionTarget    = null;
    this.cdr.detectChanges();
  }

  availableRegionsFor(user: PlatformUser): string[] {
    return this.allRegions.filter(r => !user.assignedRegions.includes(r));
  }

  addRegion(region: string): void {
    if (!this.addRegionTarget) return;
    this.isAddingRegion = true;
    this.svc.addRegion$(this.addRegionTarget.id, region)
      .pipe(takeUntil(this.destroy$))
      .subscribe(updated => {
        if (updated) {
          this.addRegionTarget = { ...updated };
          this.syncItemInList(updated);
          // also sync edit modal if open for same user
          if (this.editUser?.id === updated.id) {
            this.editUser = { ...updated };
          }
        }
        this.isAddingRegion = false;
        this.cdr.detectChanges();
      });
  }

  // ── Create user modal ────────────────────────────────────────────────────

  openCreateModal(): void {
    this.showCreateModal = true;
    this.newFirstName = '';
    this.newLastName  = '';
    this.newEmail     = '';
    this.newRole      = 'Viewer';
    this.cdr.detectChanges();
  }

  closeCreateModal(): void {
    this.showCreateModal = false;
    this.cdr.detectChanges();
  }

  createUser(): void {
    if (!this.newFirstName || !this.newLastName || !this.newEmail) return;
    this.isCreating = true;
    this.svc.createUser$({
      firstName: this.newFirstName,
      lastName:  this.newLastName,
      email:     this.newEmail,
      role:      this.newRole,
      status:    this.newRole === 'Regional Manager' ? 'Active' : 'Inactive',
      assignedRegions: [],
    }).pipe(takeUntil(this.destroy$)).subscribe(() => {
      this.isCreating      = false;
      this.showCreateModal = false;
      this.displayCount    = PAGE_SIZE_INITIAL;
      this.loadItems();
    });
  }

  // ── Helpers ──────────────────────────────────────────────────────────────

  private syncItemInList(updated: PlatformUser): void {
    const idx = this.items.findIndex(u => u.id === updated.id);
    if (idx !== -1) {
      this.items = [
        ...this.items.slice(0, idx),
        { ...updated },
        ...this.items.slice(idx + 1),
      ];
    }
  }

  statusClass(status: string): string {
    return status === 'Active'
      ? 'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200'
      : 'bg-gray-100 text-gray-500 ring-1 ring-gray-200';
  }

  roleClass(role: string): string {
    return role === 'Regional Manager'
      ? 'bg-blue-50 text-blue-700 ring-1 ring-blue-200'
      : 'bg-gray-50 text-gray-500 ring-1 ring-gray-200';
  }

  trackById(_: number, u: PlatformUser): string { return u.id; }
}