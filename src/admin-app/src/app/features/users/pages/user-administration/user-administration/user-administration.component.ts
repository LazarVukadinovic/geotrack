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
import { ActivatedRoute, Router } from '@angular/router';
import { Subscription, finalize } from 'rxjs';
import { SidebarComponent } from '../../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../../shared/components/topbar/topbar.component';
import { AuthService } from '../../../../../core/services/auth.service';
import {
  TerritoryAssignableUser,
  TerritoryUser,
  TerritoryUserAssignedRegion,
  TerritoryUserAvailableRegion,
  UserSortValue,
  UsersService,
} from '../../../../../core/services/national/users.service';
import { TerritoryDashboardService } from '../../../../../core/services/territory-dashboard.service';
import { TerritoryAssignedTerritory } from '../../../../../core/models/dashboard.model';
import { buildAvatarDisplayUrl } from '../../../../../core/utils/avatar-url';

const PAGE_SIZE = 8;

interface SortOption {
  label: string;
  value: UserSortValue;
}

interface EditUserForm {
  firstName: string;
  lastName: string;
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
  editForm: EditUserForm = { firstName: '', lastName: '' };
  editError: string | null = null;
  isSavingEdit = false;

  availableRegions: TerritoryUserAvailableRegion[] = [];
  pendingRegions: TerritoryUserAvailableRegion[] = [];
  regionSearch = '';
  isLoadingRegions = false;
  regionsError: string | null = null;

  removeTarget: TerritoryUser | null = null;
  isRemovingManagerRole = false;
  removeError: string | null = null;
  removeRegionTarget: { user: TerritoryUser; region: TerritoryUserAssignedRegion } | null = null;
  isRemovingRegion = false;
  removeRegionError: string | null = null;

  addRegionalManagerOpen = false;
  regionalManagerUserSearch = '';
  regionalManagerUsers: TerritoryAssignableUser[] = [];
  selectedRegionalManagerUser: TerritoryAssignableUser | null = null;
  regionalManagerPreselectedUser: TerritoryAssignableUser | null = null;
  regionalManagerRegions: TerritoryUserAvailableRegion[] = [];
  selectedRegionalManagerRegions: TerritoryUserAvailableRegion[] = [];
  regionalManagerRegionSearch = '';
  isLoadingRegionalManagerUsers = false;
  isLoadingRegionalManagerRegions = false;
  isSavingRegionalManager = false;
  addRegionalManagerError: string | null = null;
  addRegionalManagerSubmitted = false;

  addTerritoryAssociateOpen = false;
  associateUserSearch = '';
  associateUsers: TerritoryAssignableUser[] = [];
  selectedAssociateUser: TerritoryAssignableUser | null = null;
  associatePreselectedUser: TerritoryAssignableUser | null = null;
  isLoadingAssociateUsers = false;
  isSavingAssociate = false;
  addAssociateError: string | null = null;
  addAssociateSubmitted = false;

  removeAssociateTarget: TerritoryUser | null = null;
  isRemovingAssociate = false;
  removeAssociateError: string | null = null;

  successMessage: string | null = null;

  private searchDebounceHandle: ReturnType<typeof setTimeout> | null = null;
  private regionSearchDebounceHandle: ReturnType<typeof setTimeout> | null = null;
  private regionalManagerUserSearchDebounceHandle: ReturnType<typeof setTimeout> | null = null;
  private regionalManagerRegionSearchDebounceHandle: ReturnType<typeof setTimeout> | null = null;
  private associateUserSearchDebounceHandle: ReturnType<typeof setTimeout> | null = null;
  private listRequestSequence = 0;
  private listSubscription: Subscription | null = null;
  private territoriesSubscription: Subscription | null = null;
  private regionsSubscription: Subscription | null = null;
  private regionalManagerUsersSubscription: Subscription | null = null;
  private regionalManagerRegionsSubscription: Subscription | null = null;
  private associateUsersSubscription: Subscription | null = null;
  private failedAvatarIds = new Set<number>();

  constructor(
    private readonly router: Router,
    private readonly route: ActivatedRoute,
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
    this.regionalManagerUsersSubscription?.unsubscribe();
    this.regionalManagerRegionsSubscription?.unsubscribe();
    this.associateUsersSubscription?.unsubscribe();

    if (this.searchDebounceHandle) {
      clearTimeout(this.searchDebounceHandle);
    }

    if (this.regionSearchDebounceHandle) {
      clearTimeout(this.regionSearchDebounceHandle);
    }

    if (this.regionalManagerUserSearchDebounceHandle) {
      clearTimeout(this.regionalManagerUserSearchDebounceHandle);
    }

    if (this.regionalManagerRegionSearchDebounceHandle) {
      clearTimeout(this.regionalManagerRegionSearchDebounceHandle);
    }

    if (this.associateUserSearchDebounceHandle) {
      clearTimeout(this.associateUserSearchDebounceHandle);
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

  get selectableRegions(): TerritoryUserAvailableRegion[] {
    const assignedIds = new Set(this.editUser?.assignedRegions.map(region => region.regionId) ?? []);
    const pendingIds = new Set(this.pendingRegions.map(region => region.regionId));

    return this.availableRegions.filter(region =>
      !assignedIds.has(region.regionId) &&
      !pendingIds.has(region.regionId));
  }

  get canOpenAddRegionalManager(): boolean {
    return !!this.activeTerritoryId &&
      this.auth.canAccessTerritoryAdmin();
  }

  get canOpenAddTerritoryAssociate(): boolean {
    return !!this.activeTerritoryId && this.auth.isTerritoryManager();
  }

  get regionalManagerSelectableRegions(): TerritoryUserAvailableRegion[] {
    const selectedIds = new Set(this.selectedRegionalManagerRegions.map(region => region.regionId));
    return this.regionalManagerRegions.filter(region => !selectedIds.has(region.regionId));
  }

  get canSaveRegionalManager(): boolean {
    return !!this.selectedRegionalManagerUser &&
      this.selectedRegionalManagerRegions.length > 0 &&
      !this.isSavingRegionalManager;
  }

  get selectedAssociateAlreadyAssigned(): boolean {
    return !!this.selectedAssociateUser &&
      !!this.activeTerritoryId &&
      this.selectedAssociateUser.assignedTerritories
        .some(territory => territory.territoryId === this.activeTerritoryId);
  }

  get canSaveAssociate(): boolean {
    return !!this.selectedAssociateUser &&
      !this.selectedAssociateAlreadyAssigned &&
      !this.isSavingAssociate;
  }

  get isRowActionRunning(): boolean {
    return this.isSavingEdit ||
      this.isSavingRegionalManager ||
      this.isSavingAssociate ||
      this.isRemovingRegion ||
      this.isRemovingManagerRole ||
      this.isRemovingAssociate;
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
    this.closeAddRegionalManagerModal();
    this.closeAddTerritoryAssociateModal();
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
    const dropdownHeight = 292;
    const spaceBelow = window.innerHeight - rect.bottom;
    const top = spaceBelow < dropdownHeight
      ? Math.max(8, rect.top - dropdownHeight - 4)
      : rect.bottom + 4;

    this.dropdownPos = { top, right: window.innerWidth - rect.right };
    this.openDropdownUserId = user.userId;
    this.notifyStateChanged();
  }

  hasRowActions(user: TerritoryUser): boolean {
    return this.canEditUser(user) ||
      this.canAddRegionalManagerRole(user) ||
      this.canAddTerritoryAssociateRole(user) ||
      user.canAddRegion ||
      this.canRemoveRegionalManagerRole(user) ||
      !!user.canRemoveTerritoryAssociateRole;
  }

  canEditUser(user: TerritoryUser): boolean {
    return user.canEditUser ?? user.canEdit;
  }

  canAddRegionalManagerRole(user: TerritoryUser): boolean {
    return !!user.canAddRegionalManagerRole;
  }

  canAddTerritoryAssociateRole(user: TerritoryUser): boolean {
    return user.canAddTerritoryAssociateRole ?? !!user.canAddTerritoryAssociate;
  }

  canRemoveRegionalManagerRole(user: TerritoryUser): boolean {
    return user.canRemoveRegionalManagerRole ?? user.canRemoveManagerRole;
  }

  canRemoveRegion(user: TerritoryUser, region: TerritoryUserAssignedRegion): boolean {
    return !!(region.canRemove ?? user.canRemoveRegion ?? this.canRemoveRegionalManagerRole(user));
  }

  openEditModal(user: TerritoryUser, event?: MouseEvent): void {
    event?.stopPropagation();
    if (!this.canEditUser(user)) return;

    this.openDropdownUserId = null;
    this.editUser = user;
    this.editForm = {
      firstName: user.firstName,
      lastName: user.lastName,
    };
    this.editError = null;
    this.resetEditRegionState();
    if (user.canAddRegion) {
      this.loadAvailableRegions();
    }
    this.notifyStateChanged();
  }

  closeEditModal(): void {
    if (this.isSavingEdit) return;
    this.editUser = null;
    this.editError = null;
    this.resetEditRegionState();
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
      regionIdsToAdd: this.pendingRegions.map(region => region.regionId),
    }).pipe(
      finalize(() => {
        this.isSavingEdit = false;
        this.notifyStateChanged();
      }),
    ).subscribe({
      next: updated => {
        this.syncItemInList(updated);
        this.editUser = null;
        this.viewUser = this.viewUser?.userId === updated.userId ? updated : this.viewUser;
        this.resetEditRegionState();
      },
      error: error => {
        this.editError = this.toSafeMessage(error, 'Unable to update this user.');
      },
    });
  }

  openAddRegionModal(user: TerritoryUser, event?: MouseEvent): void {
    event?.stopPropagation();
    if (!user.canAddRegion || !this.activeTerritoryId) return;

    this.viewUser = this.viewUser?.userId === user.userId ? null : this.viewUser;
    this.openEditModal(user);
  }

  onRegionSearchChange(): void {
    if (this.regionSearchDebounceHandle) {
      clearTimeout(this.regionSearchDebounceHandle);
    }

    this.regionSearchDebounceHandle = setTimeout(() => this.loadAvailableRegions(), 250);
  }

  addRegionToPending(region: TerritoryUserAvailableRegion): void {
    if (this.pendingRegions.some(item => item.regionId === region.regionId)) return;

    this.pendingRegions = [...this.pendingRegions, region];
    this.notifyStateChanged();
  }

  removePendingRegion(regionId: number): void {
    this.pendingRegions = this.pendingRegions.filter(region => region.regionId !== regionId);
    this.notifyStateChanged();
  }

  openRemoveRegionConfirm(
    user: TerritoryUser,
    region: TerritoryUserAssignedRegion,
    event?: MouseEvent,
  ): void {
    event?.stopPropagation();
    if (!this.canRemoveRegion(user, region)) return;

    this.openDropdownUserId = null;
    this.removeRegionTarget = { user, region };
    this.removeRegionError = null;
    this.notifyStateChanged();
  }

  closeRemoveRegionConfirm(): void {
    if (this.isRemovingRegion) return;
    this.removeRegionTarget = null;
    this.removeRegionError = null;
    this.notifyStateChanged();
  }

  confirmRemoveRegion(): void {
    if (!this.removeRegionTarget || !this.activeTerritoryId || this.isRemovingRegion) return;

    const { user, region } = this.removeRegionTarget;
    this.isRemovingRegion = true;
    this.removeRegionError = null;
    this.notifyStateChanged();

    this.svc.removeRegion$(user.userId, this.activeTerritoryId, region.regionId)
      .pipe(
        finalize(() => {
          this.isRemovingRegion = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: updated => {
          this.syncItemInList(updated);
          this.editUser = this.editUser?.userId === updated.userId ? updated : this.editUser;
          this.viewUser = this.viewUser?.userId === updated.userId ? updated : this.viewUser;
          this.removeRegionTarget = null;
          this.removeRegionError = null;
          this.successMessage = `${region.displayName} was removed from ${updated.displayName}.`;
        },
        error: error => {
          this.removeRegionError = this.toSafeMessage(error, 'Unable to remove this region assignment.');
        },
      });
  }

  openRemoveManagerConfirm(user: TerritoryUser, event?: MouseEvent): void {
    event?.stopPropagation();
    if (!this.canRemoveRegionalManagerRole(user)) return;

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
          this.removeTarget = null;
          this.removeError = null;
        },
        error: error => {
          this.removeError = this.toSafeMessage(error, 'Unable to remove the manager role.');
        },
      });
  }

  openAddRegionalManagerModal(user?: TerritoryUser, event?: MouseEvent): void {
    event?.stopPropagation();
    if (this.isSavingRegionalManager || !this.activeTerritoryId) return;

    if (user) {
      if (!this.canAddRegionalManagerRole(user)) return;
      this.openDropdownUserId = null;
    } else if (!this.canOpenAddRegionalManager) {
      return;
    }

    this.successMessage = null;
    this.addRegionalManagerOpen = true;
    this.resetAddRegionalManagerState({ keepOpen: true });

    if (user) {
      const assignable = this.toAssignableUser(user);
      this.regionalManagerPreselectedUser = assignable;
      this.selectedRegionalManagerUser = assignable;
      this.regionalManagerUsers = [assignable];
      this.regionalManagerUserSearch = assignable.displayName;
      this.loadRegionalManagerRegions();
    } else {
      this.searchRegionalManagerUsers();
    }

    this.notifyStateChanged();
  }

  closeAddRegionalManagerModal(): void {
    if (this.isSavingRegionalManager) return;
    this.addRegionalManagerOpen = false;
    this.resetAddRegionalManagerState();
    this.notifyStateChanged();
  }

  onRegionalManagerUserSearchChange(): void {
    if (this.regionalManagerPreselectedUser) return;

    if (this.regionalManagerUserSearchDebounceHandle) {
      clearTimeout(this.regionalManagerUserSearchDebounceHandle);
    }

    this.regionalManagerUserSearchDebounceHandle = setTimeout(() => this.searchRegionalManagerUsers(), 250);
  }

  selectRegionalManagerUser(user: TerritoryAssignableUser): void {
    if (this.regionalManagerPreselectedUser &&
        this.regionalManagerPreselectedUser.userId !== user.userId) {
      return;
    }

    this.selectedRegionalManagerUser = user;
    this.selectedRegionalManagerRegions = [];
    this.regionalManagerRegionSearch = '';
    this.addRegionalManagerError = null;
    this.loadRegionalManagerRegions();
    this.notifyStateChanged();
  }

  isRegionalManagerUserLocked(user: TerritoryAssignableUser): boolean {
    return !!this.regionalManagerPreselectedUser &&
      this.regionalManagerPreselectedUser.userId !== user.userId;
  }

  onRegionalManagerRegionSearchChange(): void {
    if (this.regionalManagerRegionSearchDebounceHandle) {
      clearTimeout(this.regionalManagerRegionSearchDebounceHandle);
    }

    this.regionalManagerRegionSearchDebounceHandle = setTimeout(() => this.loadRegionalManagerRegions(), 250);
  }

  addRegionalManagerRegion(region: TerritoryUserAvailableRegion): void {
    if (this.selectedRegionalManagerRegions.some(item => item.regionId === region.regionId)) return;

    this.selectedRegionalManagerRegions = [...this.selectedRegionalManagerRegions, region];
    this.notifyStateChanged();
  }

  removeRegionalManagerRegion(regionId: number): void {
    this.selectedRegionalManagerRegions = this.selectedRegionalManagerRegions
      .filter(region => region.regionId !== regionId);
    this.notifyStateChanged();
  }

  saveAddRegionalManager(): void {
    this.addRegionalManagerSubmitted = true;
    if (!this.selectedRegionalManagerUser || !this.activeTerritoryId || this.selectedRegionalManagerRegions.length === 0) {
      this.notifyStateChanged();
      return;
    }

    if (this.isSavingRegionalManager) return;

    this.isSavingRegionalManager = true;
    this.addRegionalManagerError = null;
    this.notifyStateChanged();

    this.svc.addRegionalManager$(
      this.selectedRegionalManagerUser.userId,
      this.activeTerritoryId,
      this.selectedRegionalManagerRegions.map(region => region.regionId),
    ).pipe(
      finalize(() => {
        this.isSavingRegionalManager = false;
        this.notifyStateChanged();
      }),
    ).subscribe({
      next: updated => {
        this.upsertItemInList(updated);
        this.successMessage = `${updated.displayName} is now assigned as a Regional Manager.`;
        this.addRegionalManagerOpen = false;
        this.resetAddRegionalManagerState();
      },
      error: error => {
        this.addRegionalManagerError = this.toSafeMessage(error, 'Unable to add Regional Manager access.');
      },
    });
  }

  openAddTerritoryAssociateModal(user?: TerritoryUser, event?: MouseEvent): void {
    event?.stopPropagation();
    if (this.isSavingAssociate || !this.activeTerritoryId) return;

    if (user) {
      if (!this.canAddTerritoryAssociateRole(user)) return;
      this.openDropdownUserId = null;
    } else if (!this.canOpenAddTerritoryAssociate) {
      return;
    }

    this.successMessage = null;
    this.addTerritoryAssociateOpen = true;
    this.resetAddAssociateState({ keepOpen: true });

    if (user) {
      const assignable = this.toAssignableUser(user);
      this.associatePreselectedUser = assignable;
      this.selectedAssociateUser = assignable;
      this.associateUsers = [assignable];
      this.associateUserSearch = assignable.displayName;
    } else {
      this.searchAssociateUsers();
    }

    this.notifyStateChanged();
  }

  closeAddTerritoryAssociateModal(): void {
    if (this.isSavingAssociate) return;
    this.addTerritoryAssociateOpen = false;
    this.resetAddAssociateState();
    this.notifyStateChanged();
  }

  onAssociateUserSearchChange(): void {
    if (this.associatePreselectedUser) return;

    if (this.associateUserSearchDebounceHandle) {
      clearTimeout(this.associateUserSearchDebounceHandle);
    }

    this.associateUserSearchDebounceHandle = setTimeout(() => this.searchAssociateUsers(), 250);
  }

  selectAssociateUser(user: TerritoryAssignableUser): void {
    if (this.associatePreselectedUser &&
        this.associatePreselectedUser.userId !== user.userId) {
      return;
    }

    this.selectedAssociateUser = user;
    this.addAssociateError = null;
    this.notifyStateChanged();
  }

  isAssociateUserLocked(user: TerritoryAssignableUser): boolean {
    return !!this.associatePreselectedUser &&
      this.associatePreselectedUser.userId !== user.userId;
  }

  saveAddTerritoryAssociate(): void {
    this.addAssociateSubmitted = true;
    if (!this.selectedAssociateUser || !this.activeTerritoryId || this.selectedAssociateAlreadyAssigned) {
      this.notifyStateChanged();
      return;
    }

    if (this.isSavingAssociate) return;

    this.isSavingAssociate = true;
    this.addAssociateError = null;
    this.notifyStateChanged();

    this.svc.addTerritoryAssociate$(this.selectedAssociateUser.userId, this.activeTerritoryId)
      .pipe(
        finalize(() => {
          this.isSavingAssociate = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: updated => {
          this.upsertItemInList(updated);
          this.successMessage = `${updated.displayName} is now assigned as a Territory Associate.`;
          this.addTerritoryAssociateOpen = false;
          this.resetAddAssociateState();
        },
        error: error => {
          this.addAssociateError = this.toSafeMessage(error, 'Unable to add Territory Associate access.');
        },
      });
  }

  openRemoveAssociateConfirm(user: TerritoryUser, event?: MouseEvent): void {
    event?.stopPropagation();
    if (!user.canRemoveTerritoryAssociateRole) return;

    this.openDropdownUserId = null;
    this.removeAssociateTarget = user;
    this.removeAssociateError = null;
    this.notifyStateChanged();
  }

  closeRemoveAssociateConfirm(): void {
    if (this.isRemovingAssociate) return;
    this.removeAssociateTarget = null;
    this.removeAssociateError = null;
    this.notifyStateChanged();
  }

  confirmRemoveTerritoryAssociate(): void {
    if (!this.removeAssociateTarget || !this.activeTerritoryId || this.isRemovingAssociate) return;

    this.isRemovingAssociate = true;
    this.removeAssociateError = null;
    this.notifyStateChanged();

    this.svc.removeTerritoryAssociate$(this.removeAssociateTarget.userId, this.activeTerritoryId)
      .pipe(
        finalize(() => {
          this.isRemovingAssociate = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: updated => {
          if (updated.isInTerritoryScope === false) {
            this.removeItemFromList(updated.userId);
          } else {
            this.syncItemInList(updated);
          }
          this.successMessage = 'Territory Associate access was removed.';
          this.removeAssociateTarget = null;
          this.removeAssociateError = null;
        },
        error: error => {
          this.removeAssociateError = this.toSafeMessage(error, 'Unable to remove Territory Associate access.');
        },
      });
  }

  formatLastLogin(user: TerritoryUser): string {
    return this.svc.formatLastLogin(user.lastLoginAt, user.lastLoginDisplay);
  }

  avatarUrl(user: TerritoryUser): string | null {
    const url = buildAvatarDisplayUrl(user.avatarUrl, null);
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
    if (role === 'Regional Manager') {
      return 'bg-blue-50 text-blue-700 ring-1 ring-blue-200';
    }

    if (role === 'Territory Associate') {
      return 'bg-amber-50 text-amber-700 ring-1 ring-amber-200';
    }

    if (role === 'Territory Manager') {
      return 'bg-slate-100 text-slate-700 ring-1 ring-slate-200';
    }

    return 'bg-gray-50 text-gray-500 ring-1 ring-gray-200';
  }

  roleLabel(role: string): string {
    const normalized = role.replace(/[\s_-]/g, '').toLowerCase();
    switch (normalized) {
      case 'territorymanager':
      case 'nationalmanager':
        return 'Territory Manager';
      case 'territoryassociate':
        return 'Territory Associate';
      case 'regionalmanager':
        return 'Regional Manager';
      case 'registereduser':
        return 'Viewer';
      default:
        return role || 'Viewer';
    }
  }

  displayRoles(user: Pick<TerritoryUser, 'roles' | 'roleDisplayName'> | TerritoryAssignableUser): string[] {
    const roles = ('roles' in user ? user.roles : [])
      .map(role => this.roleLabel(role))
      .filter(role => !!role);

    const primary = 'roleDisplayName' in user ? user.roleDisplayName : '';
    const normalized = new Set<string>();
    const ordered: string[] = [];

    for (const role of [primary, ...roles]) {
      const key = role.replace(/[\s_-]/g, '').toLowerCase();
      if (!key || normalized.has(key)) continue;
      normalized.add(key);
      ordered.push(role);
    }

    return ordered.length > 0 ? ordered : ['Viewer'];
  }

  trackByUserId(_: number, user: TerritoryUser): number {
    return user.userId;
  }

  trackByRegionId(_: number, region: TerritoryUserAvailableRegion): number {
    return region.regionId;
  }

  trackByAssignableUserId(_: number, user: TerritoryAssignableUser): number {
    return user.userId;
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

          if (this.route.snapshot.queryParamMap.get('openAddModal') === 'true') {
            this.router.navigate([], { queryParams: {}, replaceUrl: true });
            setTimeout(() => this.openAddRegionalManagerModal(), 300);
          }
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

  private searchRegionalManagerUsers(): void {
    if (!this.activeTerritoryId || !this.addRegionalManagerOpen) return;
    if (this.regionalManagerPreselectedUser) {
      this.regionalManagerUsers = [this.regionalManagerPreselectedUser];
      return;
    }

    this.regionalManagerUsersSubscription?.unsubscribe();
    this.isLoadingRegionalManagerUsers = true;
    this.addRegionalManagerError = null;
    this.notifyStateChanged();

    this.regionalManagerUsersSubscription = this.svc.searchAssignableUsers$(
      this.activeTerritoryId,
      this.regionalManagerUserSearch,
      10,
      { excludeAlreadyRegionalManager: true },
    ).pipe(
      finalize(() => {
        this.isLoadingRegionalManagerUsers = false;
        this.notifyStateChanged();
      }),
    ).subscribe({
      next: users => {
        this.regionalManagerUsers = users;
        const selectedUserId = this.selectedRegionalManagerUser?.userId;
        if (selectedUserId && !users.some(user => user.userId === selectedUserId)) {
          this.clearRegionalManagerSelection();
        }
      },
      error: error => {
        this.regionalManagerUsers = [];
        this.addRegionalManagerError = this.toSafeMessage(error, 'Unable to search users.');
      },
    });
  }

  private searchAssociateUsers(): void {
    if (!this.activeTerritoryId || !this.addTerritoryAssociateOpen) return;
    if (this.associatePreselectedUser) {
      this.associateUsers = [this.associatePreselectedUser];
      return;
    }

    this.associateUsersSubscription?.unsubscribe();
    this.isLoadingAssociateUsers = true;
    this.addAssociateError = null;
    this.notifyStateChanged();

    this.associateUsersSubscription = this.svc.searchAssignableUsers$(
      this.activeTerritoryId,
      this.associateUserSearch,
      10,
      { excludeAlreadyTerritoryAssociate: true },
    ).pipe(
      finalize(() => {
        this.isLoadingAssociateUsers = false;
        this.notifyStateChanged();
      }),
    ).subscribe({
      next: users => {
        this.associateUsers = users;
        const selectedUserId = this.selectedAssociateUser?.userId;
        if (selectedUserId && !users.some(user => user.userId === selectedUserId)) {
          this.selectedAssociateUser = null;
        }
      },
      error: error => {
        this.associateUsers = [];
        this.addAssociateError = this.toSafeMessage(error, 'Unable to search users.');
      },
    });
  }

  private loadRegionalManagerRegions(): void {
    if (!this.selectedRegionalManagerUser || !this.activeTerritoryId) return;

    this.regionalManagerRegionsSubscription?.unsubscribe();
    this.isLoadingRegionalManagerRegions = true;
    this.addRegionalManagerError = null;
    this.notifyStateChanged();

    this.regionalManagerRegionsSubscription = this.svc.getAvailableRegions$(
      this.selectedRegionalManagerUser.userId,
      this.activeTerritoryId,
      this.regionalManagerRegionSearch,
    ).pipe(
      finalize(() => {
        this.isLoadingRegionalManagerRegions = false;
        this.notifyStateChanged();
      }),
    ).subscribe({
      next: regions => {
        this.regionalManagerRegions = regions;
      },
      error: error => {
        this.regionalManagerRegions = [];
        this.addRegionalManagerError = this.toSafeMessage(error, 'Unable to load available regions.');
      },
    });
  }

  private loadAvailableRegions(): void {
    if (!this.editUser || !this.activeTerritoryId || !this.editUser.canAddRegion) return;

    this.regionsSubscription?.unsubscribe();
    this.isLoadingRegions = true;
    this.regionsError = null;
    this.notifyStateChanged();

    this.regionsSubscription = this.svc.getAvailableRegions$(
      this.editUser.userId,
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
      },
      error: error => {
        this.availableRegions = [];
        this.regionsError = this.toSafeMessage(error, 'Unable to load available regions.');
      },
    });
  }

  private resetEditRegionState(): void {
    this.regionsSubscription?.unsubscribe();
    if (this.regionSearchDebounceHandle) {
      clearTimeout(this.regionSearchDebounceHandle);
      this.regionSearchDebounceHandle = null;
    }
    this.availableRegions = [];
    this.pendingRegions = [];
    this.regionSearch = '';
    this.regionsError = null;
    this.isLoadingRegions = false;
  }

  private resetAddRegionalManagerState(options: { keepOpen?: boolean } = {}): void {
    this.regionalManagerUsersSubscription?.unsubscribe();
    this.regionalManagerRegionsSubscription?.unsubscribe();
    if (this.regionalManagerUserSearchDebounceHandle) {
      clearTimeout(this.regionalManagerUserSearchDebounceHandle);
      this.regionalManagerUserSearchDebounceHandle = null;
    }
    if (this.regionalManagerRegionSearchDebounceHandle) {
      clearTimeout(this.regionalManagerRegionSearchDebounceHandle);
      this.regionalManagerRegionSearchDebounceHandle = null;
    }
    if (!options.keepOpen) {
      this.addRegionalManagerOpen = false;
    }
    this.regionalManagerUserSearch = '';
    this.regionalManagerUsers = [];
    this.selectedRegionalManagerUser = null;
    this.regionalManagerPreselectedUser = null;
    this.regionalManagerRegions = [];
    this.selectedRegionalManagerRegions = [];
    this.regionalManagerRegionSearch = '';
    this.isLoadingRegionalManagerUsers = false;
    this.isLoadingRegionalManagerRegions = false;
    this.addRegionalManagerError = null;
    this.addRegionalManagerSubmitted = false;
  }

  private clearRegionalManagerSelection(): void {
    this.regionalManagerRegionsSubscription?.unsubscribe();
    this.selectedRegionalManagerUser = null;
    this.regionalManagerRegions = [];
    this.selectedRegionalManagerRegions = [];
    this.regionalManagerRegionSearch = '';
    this.isLoadingRegionalManagerRegions = false;
  }

  private resetAddAssociateState(options: { keepOpen?: boolean } = {}): void {
    this.associateUsersSubscription?.unsubscribe();
    if (this.associateUserSearchDebounceHandle) {
      clearTimeout(this.associateUserSearchDebounceHandle);
      this.associateUserSearchDebounceHandle = null;
    }
    if (!options.keepOpen) {
      this.addTerritoryAssociateOpen = false;
    }
    this.associateUserSearch = '';
    this.associateUsers = [];
    this.selectedAssociateUser = null;
    this.associatePreselectedUser = null;
    this.isLoadingAssociateUsers = false;
    this.addAssociateError = null;
    this.addAssociateSubmitted = false;
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

  private upsertItemInList(updated: TerritoryUser): void {
    const index = this.items.findIndex(item => item.userId === updated.userId);
    if (index === -1) {
      this.items = [updated, ...this.items];
      this.totalItems += 1;
      return;
    }

    this.items = [
      ...this.items.slice(0, index),
      updated,
      ...this.items.slice(index + 1),
    ];
  }

  private removeItemFromList(userId: number): void {
    const nextItems = this.items.filter(item => item.userId !== userId);
    if (nextItems.length === this.items.length) return;

    this.items = nextItems;
    this.totalItems = Math.max(0, this.totalItems - 1);
  }

  private toAssignableUser(user: TerritoryUser): TerritoryAssignableUser {
    return {
      userId: user.userId,
      displayName: user.displayName,
      firstName: user.firstName,
      lastName: user.lastName,
      email: user.email,
      avatarUrl: user.avatarUrl,
      initials: user.initials,
      roles: user.roles,
      assignedRegions: user.assignedRegions,
      assignedTerritories: user.assignedTerritories,
      isActive: user.isActive,
    };
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
