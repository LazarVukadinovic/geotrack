import {
  Component, OnInit, OnDestroy, HostListener,
  ChangeDetectionStrategy, ChangeDetectorRef, inject,
} from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject } from 'rxjs';
import { takeUntil, debounceTime, distinctUntilChanged, finalize } from 'rxjs/operators';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { TerritoryService } from '../../../../core/services/administrator/territory.service';
import { AdminDashboardService } from '../../../../core/services/administrator/admin-dashboard.service';
import { Territory, TerritoryDetail, Country, TerritoryManager, GeoJsonGeometry, TerritoryMapFeatureCollection } from '../../../../core/models/administrator/territory.model';
import { TerritoryWorldMapComponent, CountryMapAssignment, CountryMapClick } from './territory-world-map.component';
import { CountryMiniMapComponent } from './country-mini-map.component';

type ViewTab = 'map' | 'list';
type ListSortField = 'name' | 'status' | 'countries' | 'createdAt';
type SortDir = 'asc' | 'desc';

@Component({
  selector: 'app-territory-management',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent, TerritoryWorldMapComponent, CountryMiniMapComponent],
  templateUrl: './territory-management.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TerritoryManagementComponent implements OnInit, OnDestroy {
  private readonly svc    = inject(TerritoryService);
  private readonly dashboardSvc = inject(AdminDashboardService);
  private readonly cdr    = inject(ChangeDetectorRef);
  private destroy$    = new Subject<void>();
  private searchSubject = new Subject<string>();
  private mapManagerSearchSubject = new Subject<string>();
  private editManagerSearchSubject = new Subject<string>();

  supportReportsCount = 0;

  // ── View state ────────────────────────────────────────────────────────────
  activeTab: ViewTab = 'map';

  // ── Territory list ────────────────────────────────────────────────────────
  territories: Territory[] = [];
  totalTerritories = 0;
  isLoading = false;
  listSearch = '';

  allCountriesList: Country[] = [];
  managers: TerritoryManager[] = [];
  activeManagers: TerritoryManager[] = [];

  // ── World map assignments ─────────────────────────────────────────────────
  countryAssignments = new Map<string, CountryMapAssignment>();
  highlightCountryCode = '';
  mapFeatureCollection: TerritoryMapFeatureCollection | null = null;
  isMapLoading = false;
  mapLoadFailed = false;
  mapAttribution = 'Boundary data provided by geoBoundaries (gbOpen), licensed under CC BY 4.0.';

  // ── Assign from map (create new territory for a country) ──────────────────
  mapClickedCountry: Country | null = null;
  mapClickedTerritoryId: number | null = null;
  mapClickedCountryBoundary: GeoJsonGeometry | null = null;
  mapManagerSearch = '';
  mapManagerUsers: TerritoryManager[] = [];
  selectedMapManager: TerritoryManager | null = null;
  isLoadingMapManagers = false;
  mapAssignError = '';
  isCreatingFromMap = false;

  // ── List sort ─────────────────────────────────────────────────────────────
  listSortField: ListSortField = 'createdAt';
  listSortDir: SortDir = 'desc';
  listSortOpen = false;

  listSortOptions: { label: string; field: ListSortField; dir: SortDir }[] = [
    { label: 'Newest first',             field: 'createdAt', dir: 'desc' },
    { label: 'Oldest first',             field: 'createdAt', dir: 'asc'  },
    { label: 'Status: Active first',     field: 'status',    dir: 'asc'  },
    { label: 'Status: Unassigned first', field: 'status',    dir: 'desc' },
    { label: 'A – Z',                    field: 'name',      dir: 'asc'  },
    { label: 'Z – A',                    field: 'name',      dir: 'desc' },
  ];

  get currentSortLabel(): string {
    return this.listSortOptions.find(o => o.field === this.listSortField && o.dir === this.listSortDir)?.label ?? 'Newest first';
  }

  // ── Assign modal (existing unassigned territory from list) ────────────────
  assignTarget: Territory | null = null;
  assignManagerId = '';
  isAssigning = false;

  // ── Unassign confirm ──────────────────────────────────────────────────────
  unassignTarget: Territory | null = null;
  isUnassigning = false;

  // ── Delete confirm ────────────────────────────────────────────────────────
  deleteTarget: Territory | null = null;
  isDeleting = false;

  // ── Edit modal (change country and/or manager) ────────────────────────────
  editTarget: TerritoryDetail | null = null;
  editAssignedManagerId = '';
  editCountryCode = '';
  editUnavailableCodes: string[] = [];
  editManagerSearch = '';
  editManagerUsers: TerritoryManager[] = [];
  selectedEditManager: TerritoryManager | null = null;
  isLoadingEditManagers = false;
  editError = '';
  isEditing = false;

  // ── Detail modal ──────────────────────────────────────────────────────────
  detailTarget: TerritoryDetail | null = null;
  detailError = '';
  isDetailLoading = false;

  // ── Dropdown ─────────────────────────────────────────────────────────────
  openDropdownId: string | null = null;
  dropdownPos     = { top: 0, right: 0 };
  sortDropdownPos = { top: 0, right: 0 };

  ngOnInit(): void {
    this.loadPendingReportsBadge();
    this.allCountriesList = this.svc.getAllCountries();
    this.managers         = this.svc.getAllManagers();
    this.activeManagers   = this.svc.getActiveManagers();
    this.loadTerritoryMap();
    this.loadTerritories();
    this.refreshManagers();

    this.searchSubject.pipe(
      debounceTime(300),
      distinctUntilChanged(),
      takeUntil(this.destroy$),
    ).subscribe(() => this.loadTerritories());

    this.mapManagerSearchSubject.pipe(
      debounceTime(250),
      distinctUntilChanged(),
      takeUntil(this.destroy$),
    ).subscribe(() => this.loadMapManagerCandidates());

    this.editManagerSearchSubject.pipe(
      debounceTime(250),
      distinctUntilChanged(),
      takeUntil(this.destroy$),
    ).subscribe(() => this.loadEditManagerCandidates());
  }

  ngOnDestroy(): void { this.destroy$.next(); this.destroy$.complete(); }

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

  private loadTerritoryMap(forceRefresh = false): void {
    this.isMapLoading = true;
    this.mapLoadFailed = false;
    const request$ = forceRefresh ? this.svc.refreshMap$() : this.svc.getMap$();
    request$
      .pipe(
        takeUntil(this.destroy$),
        finalize(() => {
          this.isMapLoading = false;
          this.cdr.markForCheck();
        }),
      )
      .subscribe({
        next: collection => {
          this.mapFeatureCollection = collection;
          this.mapAttribution = collection.attribution || this.mapAttribution;
          this.mapLoadFailed = false;
          this.cdr.markForCheck();
        },
        error: () => {
          this.mapFeatureCollection = null;
          this.mapLoadFailed = true;
          this.cdr.markForCheck();
        },
      });
  }

  @HostListener('document:click')
  onDocumentClick(): void {
    let changed = false;
    if (this.openDropdownId) { this.openDropdownId = null; changed = true; }
    if (this.listSortOpen)   { this.listSortOpen = false; changed = true; }
    if (changed) this.cdr.detectChanges();
  }

  @HostListener('document:keydown.escape')
  onEscapeKey(): void {
    if (this.editTarget) {
      this.closeEdit();
      return;
    }
    if (this.detailTarget) {
      this.closeDetail();
      return;
    }
    if (this.mapClickedCountry) {
      this.closeMapAssignModal();
      return;
    }
    if (this.deleteTarget) {
      this.closeDeleteConfirm();
    }
  }

  loadTerritories(): void {
    this.isLoading = true;
    this.svc.getPage$({
      page: 1,
      pageSize: 100,
      search: this.listSearch,
      sortField: this.listSortField,
      sortDir: this.listSortDir,
      countryFilter: [],
    }).pipe(takeUntil(this.destroy$)).subscribe({
      next: page => {
        this.territories      = page.items;
        this.totalTerritories = page.total;
        this.isLoading        = false;
        this.buildCountryAssignments();
        this.cdr.detectChanges();
      },
      error: () => {
        this.territories = [];
        this.totalTerritories = 0;
        this.isLoading = false;
        this.buildCountryAssignments();
        this.cdr.detectChanges();
      },
    });
  }

  private buildCountryAssignments(): void {
    const map = new Map<string, CountryMapAssignment>();
    for (const t of this.territories) {
      if (!t.assignedManagerId && !t.assignedManagerUserId) {
        continue;
      }

      const managerName = t.assignedManagerName ?? 'Unassigned';
      for (const code of t.countries) {
        if (!map.has(code)) {
          map.set(code, { managerId: t.assignedManagerId ?? '', managerName, managerColor: '#3b82f6' });
        }
      }
    }
    this.countryAssignments = map;
  }

  onListSearchChange(): void { this.searchSubject.next(this.listSearch); }

  retryTerritoryMap(): void {
    this.loadTerritoryMap(true);
  }

  private refreshTerritoryMapStatus(): void {
    this.svc.refreshMapStatus$()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: collection => {
          this.mapFeatureCollection = collection;
          this.mapAttribution = collection.attribution || this.mapAttribution;
          this.mapLoadFailed = false;
          this.cdr.markForCheck();
        },
        error: () => {
          this.mapLoadFailed = true;
          this.cdr.markForCheck();
        },
      });
  }

  // ── World map ─────────────────────────────────────────────────────────────
  onMapCountryClick(event: CountryMapClick | string): void {
    const code = typeof event === 'string' ? event : event.code;
    const territoryId = typeof event === 'string' ? null : event.territoryId;
    const assignment = this.countryAssignments.get(code);
    const isDisputedFeature = typeof event !== 'string' && this.isDisputedMapClick(event);
    if (isDisputedFeature) {
      const target = territoryId
        ? this.territories.find(t => (t.territoryId ?? Number(t.id)) === territoryId)
        : null;

      this.tabBeforeDetail = this.activeTab;
      this.openTerritoryDetail(target ?? this.mapClickToTerritoryFallback(event));
      return;
    }

    const isAssignedFeature = typeof event !== 'string' && event.status === 'Assigned';
    if (assignment || isAssignedFeature) {
      const territory = territoryId
        ? this.territories.find(t => (t.territoryId ?? Number(t.id)) === territoryId)
        : this.territories.find(t => t.countries.includes(code));
      const target = territory ?? (typeof event === 'string' || !territoryId
        ? null
        : this.mapClickToTerritoryFallback(event));
      if (!target) return;

      this.tabBeforeDetail = this.activeTab;
      this.openTerritoryDetail(target);
      return;
    }

    if (typeof event !== 'string' && !event.canAssign) {
      this.mapAssignError = event.boundaryMissingReason || event.boundaryRequiredMessage || 'Boundary data is missing. Run the backend ADM0 boundary setup before assigning this country.';
      this.cdr.detectChanges();
      return;
    }

    const country = this.svc.getCountryByCode(code) ?? {
      code,
      iso3: typeof event === 'string' ? null : event.iso3,
      name: typeof event === 'string' ? code : event.name,
      continent: '',
      flag: '',
    };
    this.mapClickedCountry  = {
      ...country,
      name: typeof event === 'string' ? country.name : (event.name || country.name),
    };
    this.mapClickedTerritoryId = territoryId;
    this.mapClickedCountryBoundary = typeof event === 'string' ? null : event.geometry;
    this.selectedMapManager = null;
    this.mapManagerSearch = '';
    this.mapAssignError = this.mapClickedTerritoryId && this.mapClickedCountryBoundary
      ? ''
      : 'Boundary data is missing. Run the backend ADM0 boundary setup before assigning this country.';
    this.loadMapManagerCandidates();
    this.cdr.detectChanges();
  }

  closeMapAssignModal(): void {
    this.mapClickedCountry = null;
    this.mapClickedTerritoryId = null;
    this.mapClickedCountryBoundary = null;
    this.mapManagerSearch = '';
    this.mapManagerUsers = [];
    this.selectedMapManager = null;
    this.mapAssignError = '';
    this.cdr.detectChanges();
  }

  onMapManagerSearchChange(): void {
    this.mapManagerSearchSubject.next(this.mapManagerSearch);
  }

  selectMapManager(user: TerritoryManager): void {
    if (user.canAssign === false) {
      this.mapAssignError = user.ineligibilityReason || 'This user cannot be assigned to another country.';
      this.cdr.detectChanges();
      return;
    }

    this.selectedMapManager = user;
    this.mapAssignError = '';
    this.cdr.detectChanges();
  }

  confirmMapAssign(): void {
    if (!this.mapClickedCountry || !this.selectedMapManager || !this.mapClickedTerritoryId || !this.mapClickedCountryBoundary) {
      this.mapAssignError = !this.mapClickedTerritoryId || !this.mapClickedCountryBoundary
        ? 'Boundary data is missing. Run the backend ADM0 boundary setup before assigning this country.'
        : 'Select a Territory Manager before assigning this country.';
      this.cdr.detectChanges();
      return;
    }
    this.isCreatingFromMap = true;
    this.mapAssignError = '';
    this.svc.updateManager$(this.mapClickedTerritoryId, this.selectedMapManager.userId ?? this.selectedMapManager.id)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
      next: detail => {
        this.isCreatingFromMap = false;
        this.applyTerritoryUpdate(detail);
        this.closeMapAssignModal();
        this.refreshManagers();
        this.loadTerritories();
        this.refreshTerritoryMapStatus();
      },
      error: error => {
        this.isCreatingFromMap = false;
        this.mapAssignError = this.extractErrorMessage(error, 'Territory Manager could not be assigned.');
        this.cdr.detectChanges();
      },
    });
  }

  // ── Sort dropdown ─────────────────────────────────────────────────────────
  openSortDropdown(event: MouseEvent): void {
    event.stopPropagation();
    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
    this.sortDropdownPos = { top: rect.bottom + 4, right: window.innerWidth - rect.right };
    this.listSortOpen = !this.listSortOpen;
    this.cdr.detectChanges();
  }

  applySortOption(opt: { field: ListSortField; dir: SortDir }): void {
    this.listSortField = opt.field;
    this.listSortDir   = opt.dir;
    this.listSortOpen  = false;
    this.loadTerritories();
    this.cdr.detectChanges();
  }

  // ── Assign manager (from list for existing territory) ─────────────────────
  openAssignModal(territory: Territory, event?: MouseEvent): void {
    event?.stopPropagation();
    if (this.isDisputedTerritory(territory)) {
      this.tabBeforeDetail = this.activeTab;
      this.openTerritoryDetail(territory);
      return;
    }

    this.openDropdownId  = null;
    this.assignTarget    = { ...territory };
    this.assignManagerId = '';
    this.activeManagers  = this.managers.filter(manager => manager.isActive !== false && manager.status !== 'Inactive');
    this.cdr.detectChanges();
  }

  closeAssignModal(): void { this.assignTarget = null; this.cdr.detectChanges(); }

  confirmAssign(): void {
    if (!this.assignTarget || !this.assignManagerId) return;
    this.isAssigning = true;
    this.svc.assign$(this.assignTarget.id, this.assignManagerId)
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => {
        this.isAssigning  = false;
        this.assignTarget = null;
        this.loadTerritories();
        this.refreshTerritoryMapStatus();
      });
  }

  // ── Delete territory ──────────────────────────────────────────────────────
  openDeleteConfirm(territory: Territory, event?: MouseEvent): void {
    event?.stopPropagation();
    this.openDropdownId = null;
    this.deleteTarget = { ...territory };
    this.cdr.detectChanges();
  }

  closeDeleteConfirm(): void { this.deleteTarget = null; this.cdr.detectChanges(); }

  confirmDelete(): void {
    if (!this.deleteTarget) return;
    this.isDeleting = true;
    this.svc.delete$(this.deleteTarget.id).pipe(takeUntil(this.destroy$)).subscribe(() => {
      this.isDeleting   = false;
      this.deleteTarget = null;
      this.loadTerritories();
      this.loadTerritoryMap(true);
    });
  }

  // ── Detail modal ──────────────────────────────────────────────────────────
  private tabBeforeDetail: ViewTab = 'list';

  openDetail(territory: Territory, event: MouseEvent): void {
    const target = event.target as HTMLElement;
    if (target.closest('button[data-action]')) return;
    const code = territory.countries[0];
    this.tabBeforeDetail = this.activeTab;
    this.highlightCountryCode = '';
    this.activeTab = 'map';
    this.cdr.detectChanges();
    // let the map tab render, then trigger zoom
    setTimeout(() => {
      this.highlightCountryCode = code;
      this.openTerritoryDetail(territory);
      this.cdr.detectChanges();
    }, 50);
  }

  closeDetail(): void {
    this.detailTarget = null;
    this.detailError = '';
    this.isDetailLoading = false;
    this.highlightCountryCode = '';
    this.activeTab = this.tabBeforeDetail;
    this.cdr.detectChanges();
  }

  private openTerritoryDetail(territory: Territory): void {
    this.detailError = '';
    this.isDetailLoading = true;
    this.detailTarget = this.asDetailFallback(territory);
    this.cdr.markForCheck();

    this.svc.getDetail$(territory.territoryId ?? territory.id)
      .pipe(
        takeUntil(this.destroy$),
        finalize(() => {
          this.isDetailLoading = false;
          this.cdr.markForCheck();
        }),
      )
      .subscribe({
        next: detail => {
          this.detailTarget = detail;
          this.applyTerritoryUpdate(detail);
          this.cdr.markForCheck();
        },
        error: error => {
          this.detailError = this.extractErrorMessage(error, 'Territory details could not be loaded.');
          this.cdr.markForCheck();
        },
      });
  }

  // ── Edit modal (country + manager change) ─────────────────────────────────
  private tabBeforeEdit: ViewTab = 'list';

  openEdit(territory: Territory | TerritoryDetail, event?: MouseEvent): void {
    event?.stopPropagation();
    if (this.isDisputedTerritory(territory)) {
      this.editError = 'This disputed boundary is shown for map completeness and cannot be assigned as a country territory.';
      this.openDropdownId = null;
      this.cdr.markForCheck();
      return;
    }

    this.tabBeforeEdit = this.detailTarget ? this.tabBeforeDetail : this.activeTab;
    this.openDropdownId = null;
    this.editError = '';

    if ('boundaryGeoJson' in territory || this.detailTarget?.territoryId === territory.territoryId) {
      this.startEdit((this.detailTarget?.territoryId === territory.territoryId ? this.detailTarget : territory) as TerritoryDetail);
      return;
    }

    this.isDetailLoading = true;
    this.svc.getDetail$(territory.territoryId ?? territory.id)
      .pipe(
        takeUntil(this.destroy$),
        finalize(() => {
          this.isDetailLoading = false;
          this.cdr.markForCheck();
        }),
      )
      .subscribe({
        next: detail => {
          this.detailTarget = detail;
          this.applyTerritoryUpdate(detail);
          this.startEdit(detail);
        },
        error: error => {
          this.detailError = this.extractErrorMessage(error, 'Territory details could not be loaded.');
          this.cdr.markForCheck();
        },
      });
  }

  closeEdit(): void {
    this.activeTab = this.tabBeforeEdit;
    this.highlightCountryCode = '';
    this.editTarget = null;
    this.editManagerUsers = [];
    this.selectedEditManager = null;
    this.editManagerSearch = '';
    this.editError = '';
    this.cdr.detectChanges();
  }

  onEditMapCountryClick(event: CountryMapClick | string): void {
    this.editCountryCode = typeof event === 'string' ? event : event.code;
    this.cdr.detectChanges();
  }

  onEditManagerSearchChange(): void {
    this.editManagerSearchSubject.next(this.editManagerSearch);
  }

  selectEditManager(user: TerritoryManager | null): void {
    if (user?.canAssign === false) {
      this.editError = user.ineligibilityReason || 'This user cannot be assigned to this country.';
      this.cdr.markForCheck();
      return;
    }

    this.selectedEditManager = user;
    this.editAssignedManagerId = user?.id ?? '';
    this.editError = '';
    this.cdr.markForCheck();
  }

  isCurrentEditManager(user: TerritoryManager): boolean {
    return !!this.editTarget?.assignedManagerId && this.editTarget.assignedManagerId === user.id;
  }

  isSelectedEditManager(user: TerritoryManager): boolean {
    return this.editAssignedManagerId === user.id;
  }

  saveEdit(): void {
    if (!this.editTarget) return;
    if (this.isDisputedTerritory(this.editTarget)) {
      this.editError = 'This disputed boundary is shown for map completeness and cannot be assigned as a country territory.';
      this.cdr.markForCheck();
      return;
    }

    if (this.editAssignedManagerId && !this.editTarget.hasBoundary) {
      this.editError = this.editTarget.boundaryRequiredMessage
        || 'This country cannot be assigned until its boundary geometry is available.';
      this.cdr.markForCheck();
      return;
    }

    this.isEditing = true;
    const managerId = this.editAssignedManagerId || null;

    this.svc.updateManager$(this.editTarget.territoryId ?? this.editTarget.id, managerId)
      .pipe(
        takeUntil(this.destroy$),
        finalize(() => {
          this.isEditing = false;
          this.cdr.markForCheck();
        }),
      )
      .subscribe({
        next: detail => {
          this.editTarget = null;
          this.detailTarget = detail;
          this.applyTerritoryUpdate(detail);
          this.refreshManagers();
          this.loadTerritories();
          this.refreshTerritoryMapStatus();
        },
        error: error => {
          this.editError = this.extractErrorMessage(error, 'Territory Manager assignment could not be saved.');
          this.cdr.markForCheck();
        },
      });
  }

  // ── Row dropdown ─────────────────────────────────────────────────────────
  toggleDropdown(id: string, event: MouseEvent): void {
    event.stopPropagation();
    if (this.openDropdownId === id) {
      this.openDropdownId = null;
    } else {
      const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
      this.dropdownPos = { top: rect.bottom + 4, right: window.innerWidth - rect.right };
      this.openDropdownId = id;
    }
    this.cdr.detectChanges();
  }

  // ── Helpers ───────────────────────────────────────────────────────────────
  private refreshManagers(): void {
    this.svc.searchManagerCandidates$('', 50)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: managers => {
          this.managers = managers;
          this.activeManagers = managers.filter(manager => manager.isActive !== false && manager.status !== 'Inactive');
          this.cdr.markForCheck();
        },
        error: () => {
          this.managers = [];
          this.activeManagers = [];
          this.cdr.markForCheck();
        },
      });
  }

  private loadMapManagerCandidates(): void {
    this.isLoadingMapManagers = true;
    this.svc.searchManagerCandidates$(this.mapManagerSearch, 25)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: users => {
          this.mapManagerUsers = users;
          this.isLoadingMapManagers = false;
          if (this.selectedMapManager) {
            this.selectedMapManager = users.find(user => user.id === this.selectedMapManager?.id) ?? this.selectedMapManager;
          }
          this.cdr.markForCheck();
        },
        error: error => {
          this.mapManagerUsers = [];
          this.isLoadingMapManagers = false;
          this.mapAssignError = this.extractErrorMessage(error, 'Users could not be loaded.');
          this.cdr.markForCheck();
        },
      });
  }

  displayRoles(user: TerritoryManager): string[] {
    return this.displayRoleLabels(user.roles);
  }

  displayRoleLabels(roles: string[] | null | undefined): string[] {
    const labels = (roles ?? [])
      .map(role => this.roleLabel(role))
      .filter(role => role !== 'Registered User');

    const distinct = Array.from(new Set(labels));
    return distinct.length > 0 ? distinct : ['Viewer'];
  }

  roleClass(role: string): string {
    const normalized = this.roleLabel(role);
    if (normalized === 'Admin') return 'bg-purple-50 text-purple-700 ring-purple-200';
    if (normalized === 'Territory Manager') return 'bg-slate-100 text-slate-700 ring-slate-200';
    if (normalized === 'Territory Associate') return 'bg-amber-50 text-amber-700 ring-amber-200';
    if (normalized === 'Regional Manager') return 'bg-blue-50 text-blue-700 ring-blue-200';
    return 'bg-gray-100 text-gray-600 ring-gray-200';
  }

  roleLabel(role: string): string {
    const normalized = role.replace(/[\s_-]/g, '').toLowerCase();
    if (normalized === 'admin') return 'Admin';
    if (normalized === 'territorymanager' || normalized === 'nationalmanager') return 'Territory Manager';
    if (normalized === 'territoryassociate') return 'Territory Associate';
    if (normalized === 'regionalmanager') return 'Regional Manager';
    if (normalized === 'registereduser') return 'Registered User';
    if (normalized === 'viewer') return 'Viewer';
    return role;
  }

  onAvatarError(event: Event): void {
    const image = event.target as HTMLImageElement | null;
    if (image) {
      image.style.display = 'none';
    }
  }

  private extractErrorMessage(error: unknown, fallback: string): string {
    if (error instanceof HttpErrorResponse) {
      const body = error.error as { message?: unknown } | string | null;
      if (typeof body === 'string' && body.trim()) {
        return body.trim();
      }
      if (body && typeof body === 'object' && typeof body.message === 'string' && body.message.trim()) {
        return body.message.trim();
      }
    }
    return fallback;
  }

  countryName(code: string): string {
    return this.svc.getCountryByCode(code)?.name ?? code;
  }

  countryFlag(code: string): string {
    return this.svc.getCountryByCode(code)?.flag ?? '';
  }

  statusClass(status: string): string {
    if (status === 'DisputedViewOnly' || status === 'DisputedAssigned') {
      return 'bg-amber-50 text-amber-700 ring-1 ring-amber-200';
    }

    return status === 'Assigned' || status === 'Active'
      ? 'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200'
      : 'bg-amber-50 text-amber-700 ring-1 ring-amber-200';
  }

  territoryKindLabel(territory: Territory | TerritoryDetail): string {
    return this.isDisputedTerritory(territory) ? 'Disputed Territory' : 'Country / ADM0';
  }

  boundaryTypeLabel(territory: Territory | TerritoryDetail): string {
    return territory.boundaryAdmLevel || territory.boundaryShapeType || 'Boundary';
  }

  isDisputedTerritory(territory: Territory | TerritoryDetail | null | undefined): boolean {
    return territory?.territoryKind === 'DisputedTerritory' ||
      territory?.status === 'DisputedViewOnly' ||
      territory?.status === 'DisputedAssigned' ||
      territory?.boundaryAdmLevel === 'DISP' ||
      territory?.boundaryShapeType === 'DISP';
  }

  managerInitials(managerId: string | null): string {
    if (!managerId) return '?';
    return this.managers.find(m => m.id === managerId)?.initials ?? '?';
  }

  managerColor(managerId: string | null): string {
    if (!managerId) return '#9ca3af';
    return this.managers.find(m => m.id === managerId)?.avatarColor ?? '#9ca3af';
  }

  formatDate(iso: string): string {
    return new Date(iso).toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
  }

  trackById(_: number, t: Territory): string { return t.id; }

  private startEdit(detail: TerritoryDetail): void {
    this.editTarget = { ...detail };
    this.editAssignedManagerId = detail.assignedManagerId ?? '';
    this.editCountryCode = detail.countries[0] ?? detail.countryCode ?? '';
    this.editUnavailableCodes = this.territories
      .filter(t => t.id !== detail.id)
      .flatMap(t => t.countries);
    this.editManagerSearch = '';
    this.editManagerUsers = [];
    this.selectedEditManager = null;
    this.editError = !detail.hasBoundary
      ? detail.boundaryRequiredMessage || 'This country cannot be assigned until its boundary geometry is available.'
      : '';
    this.loadEditManagerCandidates();
    this.cdr.markForCheck();
  }

  private loadEditManagerCandidates(): void {
    if (!this.editTarget) return;

    this.isLoadingEditManagers = true;
    this.svc.getAssignableManagers$(this.editTarget.territoryId ?? this.editTarget.id, this.editManagerSearch, 25)
      .pipe(
        takeUntil(this.destroy$),
        finalize(() => {
          this.isLoadingEditManagers = false;
          this.cdr.markForCheck();
        }),
      )
      .subscribe({
        next: users => {
          this.editManagerUsers = users;
          this.selectedEditManager = this.editAssignedManagerId
            ? users.find(user => user.id === this.editAssignedManagerId) ?? null
            : null;
          this.cdr.markForCheck();
        },
        error: error => {
          this.editManagerUsers = [];
          this.editError = this.extractErrorMessage(error, 'Users could not be loaded.');
          this.cdr.markForCheck();
        },
      });
  }

  private mapClickToTerritoryFallback(event: CountryMapClick): Territory {
    const territoryId = event.territoryId ?? 0;
    const id = `TERR-${String(territoryId).padStart(3, '0')}`;
    const now = new Date().toISOString();
    const isDisputed = this.isDisputedMapClick(event);
    return {
      id,
      territoryId,
      countryId: territoryId,
      name: event.name,
      displayName: event.name,
      internalReference: id,
      territoryKind: event.territoryKind,
      countryCode: isDisputed ? null : event.iso2 ?? event.code,
      iso2: isDisputed ? null : event.iso2 ?? event.code,
      iso3: isDisputed ? null : event.iso3,
      countries: isDisputed ? [] : [event.iso2 ?? event.code],
      assignedManagerUserId: null,
      assignedManagerId: null,
      assignedManagerName: null,
      assignedManagerEmail: null,
      status: isDisputed
        ? (event.status === 'DisputedAssigned' ? 'DisputedAssigned' : 'DisputedViewOnly')
        : event.status === 'Assigned' ? 'Assigned' : 'Unassigned',
      boundaryShapeType: event.shapeType,
      boundaryShapeGroup: event.shapeGroup,
      boundaryShapeId: event.shapeId,
      boundaryAdmLevel: isDisputed ? 'DISP' : null,
      boundarySource: 'geoBoundaries',
      boundaryReleaseType: 'gbOpen',
      boundaryLicense: 'CC BY 4.0',
      hasBoundary: event.hasBoundary,
      zone: event.geometry
        ? { type: 'Feature', geometry: event.geometry, properties: null }
        : null,
      createdAt: now,
      updatedAt: now,
    };
  }

  private asDetailFallback(territory: Territory): TerritoryDetail {
    return {
      ...territory,
      assignedManager: territory.assignedManagerId
        ? {
            id: territory.assignedManagerId,
            userId: Number(territory.assignedManagerId),
            displayName: territory.assignedManagerName ?? 'Assigned manager',
            name: territory.assignedManagerName ?? 'Assigned manager',
            email: territory.assignedManagerEmail ?? '',
            initials: this.managerInitials(territory.assignedManagerId),
            roles: [],
            assignedAt: territory.updatedAt,
          }
        : null,
      boundaryGeoJson: territory.zone?.geometry ?? null,
      viewport: null,
      boundaryRequiredMessage: this.isDisputedTerritory(territory)
        ? 'This disputed boundary is shown for map completeness and cannot be assigned as a country territory.'
        : territory.hasBoundary ? null : 'This country cannot be assigned until its boundary geometry is available.',
      canAssign: this.isDisputedTerritory(territory) ? false : !!territory.hasBoundary,
    };
  }

  private applyTerritoryUpdate(updated: Territory): void {
    const index = this.territories.findIndex(item => item.id === updated.id || item.territoryId === updated.territoryId);
    if (index >= 0) {
      this.territories = [
        ...this.territories.slice(0, index),
        { ...this.territories[index], ...updated },
        ...this.territories.slice(index + 1),
      ];
    } else {
      this.territories = [updated, ...this.territories];
      this.totalTerritories += 1;
    }
    this.buildCountryAssignments();
    this.cdr.markForCheck();
  }

  private isDisputedMapClick(event: CountryMapClick): boolean {
    return event.territoryKind === 'DisputedTerritory' ||
      event.status === 'DisputedViewOnly' ||
      event.status === 'DisputedAssigned' ||
      event.shapeType === 'DISP';
  }
}
