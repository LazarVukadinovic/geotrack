import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  HostListener,
  OnDestroy,
  OnInit,
  ViewChild,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { Subscription, finalize } from 'rxjs';
import { SidebarComponent } from '../../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../../shared/components/topbar/topbar.component';
import { AuthService } from '../../../../../core/services/auth.service';
import {
  TerritoryUser,
  TerritoryUserAvailableRegion,
  UserSortValue,
  UsersService,
} from '../../../../../core/services/national/users.service';
import { TerritoryDashboardService } from '../../../../../core/services/territory-dashboard.service';
import { TerritoryAssignedTerritory } from '../../../../../core/models/dashboard.model';

const PAGE_SIZE = 8;

interface SortOption {
  label: string;
  value: UserSortValue;
}

interface EditUserForm {
  firstName: string;
  lastName: string;
  displayName: string;
}

@Component({
  selector: 'app-user-administration',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './user-administration.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserAdministrationComponent implements OnInit, OnDestroy {
  @ViewChild('scrollContainer') scrollContainer?: ElementRef<HTMLDivElement>;

  assignedTerritories: TerritoryAssignedTerritory[] = [];
  activeTerritoryId: number | null = null;
  territoryLoading = false;

  items: TerritoryUser[] = [];
  totalItems = 0;
  isLoading = false;
  isLoadingMore = false;
  errorMessage: string | null = null;

  searchQuery = '';
  currentSort: UserSortValue = 'status_active_first';
  sortOpen = false;
  readonly sortOptions: SortOption[] = [
    { label: 'Status: Active First', value: 'status_active_first' },
    { label: 'Name: A-Z', value: 'name_asc' },
    { label: 'Name: Z-A', value: 'name_desc' },
    { label: 'Last Login: Newest', value: 'last_login_desc' },
    { label: 'Last Login: Oldest', value: 'last_login_asc' },
    { label: 'Role', value: 'role' },
    { label: 'Most Regions', value: 'regions_count_desc' },
  ];

  openDropdownUserId: number | null = null;
  dropdownPos = { top: 0, right: 0 };

  viewUser: TerritoryUser | null = null;
  editUser: TerritoryUser | null = null;
  editForm: EditUserForm = { firstName: '', lastName: '', displayName: '' };
  editError: string | null = null;
  isSavingEdit = false;

  addRegionTarget: TerritoryUser | null = null;
  availableRegions: TerritoryUserAvailableRegion[] = [];
  selectedRegionId: number | null = null;
  regionSearch = '';
  isLoadingRegions = false;
  isAddingRegion = false;
  addRegionError: string | null = null;

  removeTarget: TerritoryUser | null = null;
  isRemovingManagerRole = false;
  removeError: string | null = null;

  private searchDebounceHandle: ReturnType<typeof setTimeout> | null = null;
  private regionSearchDebounceHandle: ReturnType<typeof setTimeout> | null = null;
  private listRequestSequence = 0;
  private listSubscription: Subscription | null = null;
  private territoriesSubscription: Subscription | null = null;
  private regionsSubscription: Subscription | null = null;
  private failedAvatarIds = new Set<number>();

  constructor(
    private readonly router: Router,
    private readonly auth: AuthService,
    private readonly svc: UsersService,
    private readonly territoryDashboard: TerritoryDashboardService,
    private readonly cdr: ChangeDetectorRef,
  ) {}

  ngOnInit(): void {
    this.loadAssignedTerritories();
  }

  ngOnDestroy(): void {
    this.listRequestSequence++;
    this.listSubscription?.unsubscribe();
    this.territoriesSubscription?.unsubscribe();
    this.regionsSubscription?.unsubscribe();

    if (this.searchDebounceHandle) {
      clearTimeout(this.searchDebounceHandle);
    }

    if (this.regionSearchDebounceHandle) {
      clearTimeout(this.regionSearchDebounceHandle);
    }
  }

  get userName(): string {
    return this.auth.fullName;
  }

  get userRole(): string {
    return this.auth.getUser()?.role ?? '';
  }

  get activeTerritoryName(): string {
    return this.assignedTerritories.find(item => item.id === this.activeTerritoryId)?.displayName ?? '';
  }

  get currentSortLabel(): string {
    return this.sortOptions.find(option => option.value === this.currentSort)?.label ?? 'Status: Active First';
  }

  get hasMore(): boolean {
    return this.items.length < this.totalItems;
  }

  get nextPageCount(): number {
    return Math.min(PAGE_SIZE, Math.max(0, this.totalItems - this.items.length));
  }

  get emptyMessage(): string {
    if (!this.activeTerritoryId) {
      return 'No assigned territory is active.';
    }

    if (this.searchQuery.trim()) {
      return 'No users match this search.';
    }

    return 'No regional users found in this territory.';
  }

  @HostListener('document:click')
  onDocumentClick(): void {
    let changed = false;

    if (this.openDropdownUserId !== null) {
      this.openDropdownUserId = null;
      changed = true;
    }

    if (this.sortOpen) {
      this.sortOpen = false;
      changed = true;
    }

    if (changed) {
      this.notifyStateChanged();
    }
  }

  onTerritoryChange(territoryId: number): void {
    if (territoryId === this.activeTerritoryId) return;

    this.activeTerritoryId = territoryId;
    this.territoryDashboard.setActiveTerritoryId(territoryId);
    this.searchQuery = '';
    this.currentSort = 'status_active_first';
    this.resetList();
    this.loadUsers(true);
    this.notifyStateChanged();
  }

  onSearchChange(): void {
    if (this.searchDebounceHandle) {
      clearTimeout(this.searchDebounceHandle);
    }

    this.searchDebounceHandle = setTimeout(() => {
      this.resetList();
      this.loadUsers(true);
    }, 300);
  }

  clearSearch(): void {
    this.searchQuery = '';
    this.onSearchChange();
  }

  applySortOption(option: SortOption): void {
    if (option.value === this.currentSort) {
      this.sortOpen = false;
      this.notifyStateChanged();
      return;
    }

    this.currentSort = option.value;
    this.sortOpen = false;
    this.resetList();
    this.loadUsers(true);
    this.notifyStateChanged();
  }

  loadMore(): void {
    if (!this.hasMore || this.isLoadingMore) return;
    this.loadUsers(false);
  }

  goBack(): void {
    this.router.navigate(['/territory/overview']);
  }

  openViewModal(user: TerritoryUser): void {
    this.openDropdownUserId = null;
    this.viewUser = user;
    this.notifyStateChanged();
  }

  closeViewModal(): void {
    this.viewUser = null;
    this.notifyStateChanged();
  }

  toggleDropdown(user: TerritoryUser, event: MouseEvent): void {
    event.stopPropagation();

    if (this.openDropdownUserId === user.userId) {
      this.openDropdownUserId = null;
      this.notifyStateChanged();
      return;
    }

    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
    const dropdownHeight = 150;
    const spaceBelow = window.innerHeight - rect.bottom;
    const top = spaceBelow < dropdownHeight
      ? Math.max(8, rect.top - dropdownHeight - 4)
      : rect.bottom + 4;

    this.dropdownPos = { top, right: window.innerWidth - rect.right };
    this.openDropdownUserId = user.userId;
    this.notifyStateChanged();
  }

  hasRowActions(user: TerritoryUser): boolean {
    return user.canEdit || user.canAddRegion || user.canRemoveManagerRole;
  }

  openEditModal(user: TerritoryUser, event?: MouseEvent): void {
    event?.stopPropagation();
    if (!user.canEdit) return;

    this.openDropdownUserId = null;
    this.editUser = user;
    this.editForm = {
      firstName: user.firstName,
      lastName: user.lastName,
      displayName: user.displayName,
    };
    this.editError = null;
    this.notifyStateChanged();
  }

  closeEditModal(): void {
    if (this.isSavingEdit) return;
    this.editUser = null;
    this.editError = null;
    this.notifyStateChanged();
  }

  saveEdit(): void {
    if (!this.editUser || !this.activeTerritoryId || this.isSavingEdit) return;

    this.isSavingEdit = true;
    this.editError = null;
    this.notifyStateChanged();

    this.svc.updateUser$(this.editUser.userId, {
      territoryId: this.activeTerritoryId,
      firstName: this.editForm.firstName,
      lastName: this.editForm.lastName,
      displayName: this.editForm.displayName,
    }).pipe(
      finalize(() => {
        this.isSavingEdit = false;
        this.notifyStateChanged();
      }),
    ).subscribe({
      next: updated => {
        this.syncItemInList(updated);
        this.editUser = updated;
        this.viewUser = this.viewUser?.userId === updated.userId ? updated : this.viewUser;
      },
      error: error => {
        this.editError = this.toSafeMessage(error, 'Unable to update this user.');
      },
    });
  }

  openAddRegionModal(user: TerritoryUser, event?: MouseEvent): void {
    event?.stopPropagation();
    if (!user.canAddRegion || !this.activeTerritoryId) return;

    this.openDropdownUserId = null;
    this.addRegionTarget = user;
    this.availableRegions = [];
    this.selectedRegionId = null;
    this.regionSearch = '';
    this.addRegionError = null;
    this.loadAvailableRegions();
    this.notifyStateChanged();
  }

  closeAddRegionModal(): void {
    if (this.isAddingRegion) return;
    this.addRegionTarget = null;
    this.availableRegions = [];
    this.selectedRegionId = null;
    this.addRegionError = null;
    this.notifyStateChanged();
  }

  onRegionSearchChange(): void {
    if (this.regionSearchDebounceHandle) {
      clearTimeout(this.regionSearchDebounceHandle);
    }

    this.regionSearchDebounceHandle = setTimeout(() => this.loadAvailableRegions(), 250);
  }

  addSelectedRegion(): void {
    if (!this.addRegionTarget || !this.activeTerritoryId || !this.selectedRegionId || this.isAddingRegion) {
      return;
    }

    this.isAddingRegion = true;
    this.addRegionError = null;
    this.notifyStateChanged();

    this.svc.addRegion$(this.addRegionTarget.userId, this.activeTerritoryId, this.selectedRegionId)
      .pipe(
        finalize(() => {
          this.isAddingRegion = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: updated => {
          this.syncItemInList(updated);
          this.addRegionTarget = updated;
          this.editUser = this.editUser?.userId === updated.userId ? updated : this.editUser;
          this.viewUser = this.viewUser?.userId === updated.userId ? updated : this.viewUser;
          this.closeAddRegionModal();
        },
        error: error => {
          this.addRegionError = this.toSafeMessage(error, 'Unable to add this region.');
        },
      });
  }

  openRemoveManagerConfirm(user: TerritoryUser, event?: MouseEvent): void {
    event?.stopPropagation();
    if (!user.canRemoveManagerRole) return;

    this.openDropdownUserId = null;
    this.removeTarget = user;
    this.removeError = null;
    this.notifyStateChanged();
  }

  closeRemoveManagerConfirm(): void {
    if (this.isRemovingManagerRole) return;
    this.removeTarget = null;
    this.removeError = null;
    this.notifyStateChanged();
  }

  confirmRemoveManagerRole(): void {
    if (!this.removeTarget || !this.activeTerritoryId || this.isRemovingManagerRole) return;

    this.isRemovingManagerRole = true;
    this.removeError = null;
    this.notifyStateChanged();

    this.svc.removeManagerRole$(this.removeTarget.userId, this.activeTerritoryId)
      .pipe(
        finalize(() => {
          this.isRemovingManagerRole = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: updated => {
          this.syncItemInList(updated);
          this.editUser = this.editUser?.userId === updated.userId ? updated : this.editUser;
          this.viewUser = this.viewUser?.userId === updated.userId ? updated : this.viewUser;
          this.closeRemoveManagerConfirm();
        },
        error: error => {
          this.removeError = this.toSafeMessage(error, 'Unable to remove the manager role.');
        },
      });
  }

  formatLastLogin(user: TerritoryUser): string {
    return this.svc.formatLastLogin(user.lastLoginAt, user.lastLoginDisplay);
  }

  avatarUrl(user: TerritoryUser): string | null {
    const url = user.avatarUrl?.trim() ?? '';
    return url && !this.failedAvatarIds.has(user.userId) ? url : null;
  }

  avatarAlt(user: TerritoryUser): string {
    return `${user.displayName || user.email} profile image`;
  }

  onAvatarError(user: TerritoryUser): void {
    this.failedAvatarIds.add(user.userId);
    this.notifyStateChanged();
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

  trackByUserId(_: number, user: TerritoryUser): number {
    return user.userId;
  }

  trackByRegionId(_: number, region: TerritoryUserAvailableRegion): number {
    return region.regionId;
  }

  private loadAssignedTerritories(): void {
    this.territoryLoading = true;
    this.errorMessage = null;
    this.notifyStateChanged();

    this.territoriesSubscription = this.territoryDashboard
      .getAssignedTerritories()
      .pipe(
        finalize(() => {
          this.territoryLoading = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: territories => {
          this.assignedTerritories = territories ?? [];
          const persistedId = this.territoryDashboard.getActiveTerritoryId();
          const selected =
            this.assignedTerritories.find(territory => territory.id === persistedId) ??
            this.assignedTerritories[0] ??
            null;

          this.activeTerritoryId = selected?.id ?? null;
          this.territoryDashboard.setActiveTerritoryId(this.activeTerritoryId);

          if (!this.activeTerritoryId) {
            this.resetList();
            return;
          }

          this.loadUsers(true);
        },
        error: error => {
          this.handleLoadError(error, 'Unable to load assigned territories.');
        },
      });
  }

  private loadUsers(reset: boolean): void {
    if (!this.activeTerritoryId) {
      this.listRequestSequence++;
      this.listSubscription?.unsubscribe();
      this.resetList();
      this.isLoading = false;
      this.isLoadingMore = false;
      this.notifyStateChanged();
      return;
    }

    const requestId = ++this.listRequestSequence;
    const skip = reset ? 0 : this.items.length;
    this.listSubscription?.unsubscribe();

    if (reset) {
      this.isLoading = true;
      this.resetList();
    } else {
      this.isLoadingMore = true;
    }

    this.errorMessage = null;
    this.notifyStateChanged();

    this.listSubscription = this.svc.getPage$({
      territoryId: this.activeTerritoryId,
      search: this.searchQuery,
      sort: this.currentSort,
      skip,
      take: PAGE_SIZE,
    }).pipe(
      finalize(() => {
        if (this.listRequestSequence === requestId) {
          this.isLoading = false;
          this.isLoadingMore = false;
          this.notifyStateChanged();
        }
      }),
    ).subscribe({
      next: page => {
        if (this.listRequestSequence !== requestId) return;

        this.items = reset ? page.items : [...this.items, ...page.items];
        this.totalItems = this.toSafeCount(page.totalCount);
      },
      error: error => {
        if (this.listRequestSequence === requestId) {
          this.handleLoadError(error, 'Unable to load users.');
        }
      },
    });
  }

  private loadAvailableRegions(): void {
    if (!this.addRegionTarget || !this.activeTerritoryId) return;

    this.regionsSubscription?.unsubscribe();
    this.isLoadingRegions = true;
    this.addRegionError = null;
    this.notifyStateChanged();

    this.regionsSubscription = this.svc.getAvailableRegions$(
      this.addRegionTarget.userId,
      this.activeTerritoryId,
      this.regionSearch,
    ).pipe(
      finalize(() => {
        this.isLoadingRegions = false;
        this.notifyStateChanged();
      }),
    ).subscribe({
      next: regions => {
        this.availableRegions = regions;
        if (this.selectedRegionId && !regions.some(region => region.regionId === this.selectedRegionId)) {
          this.selectedRegionId = null;
        }
      },
      error: error => {
        this.availableRegions = [];
        this.selectedRegionId = null;
        this.addRegionError = this.toSafeMessage(error, 'Unable to load available regions.');
      },
    });
  }

  private syncItemInList(updated: TerritoryUser): void {
    const index = this.items.findIndex(item => item.userId === updated.userId);
    if (index === -1) return;

    this.items = [
      ...this.items.slice(0, index),
      updated,
      ...this.items.slice(index + 1),
    ];
  }

  private resetList(): void {
    this.items = [];
    this.totalItems = 0;
    this.failedAvatarIds.clear();
  }

  private handleLoadError(error: unknown, fallback: string): void {
    this.isLoading = false;
    this.isLoadingMore = false;
    this.resetList();

    if (error instanceof HttpErrorResponse) {
      if (error.status === 401) {
        this.errorMessage = 'Sign in with a Territory Manager account to view users.';
        this.notifyStateChanged();
        return;
      }

      if (error.status === 403) {
        this.errorMessage = 'This account is not authorized for the selected territory.';
        this.notifyStateChanged();
        return;
      }
    }

    this.errorMessage = this.toSafeMessage(error, fallback);
    this.notifyStateChanged();
  }

  private toSafeMessage(error: unknown, fallback: string): string {
    if (error instanceof HttpErrorResponse) {
      const message = error.error?.message;
      return typeof message === 'string' && message.trim() ? message : fallback;
    }

    return fallback;
  }

  private toSafeCount(value: unknown): number {
    return typeof value === 'number' && Number.isFinite(value) && value >= 0 ? value : 0;
  }

  private notifyStateChanged(): void {
    this.cdr.markForCheck();
  }
}
