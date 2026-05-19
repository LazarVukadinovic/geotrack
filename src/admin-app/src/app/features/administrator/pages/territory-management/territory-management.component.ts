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
import { TerritoryService } from '../../../../core/services/administrator/territory.service';
import { AdminReviewsService } from '../../../../core/services/administrator/admin-reviews.service';
import { Territory, Country, TerritoryManager, CreateTerritoryRequest } from '../../../../core/models/administrator/territory.model';
import { TerritoryWorldMapComponent, CountryMapAssignment } from './territory-world-map.component';
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
  private readonly revSvc = inject(AdminReviewsService);
  private readonly cdr    = inject(ChangeDetectorRef);
  private destroy$    = new Subject<void>();
  private searchSubject = new Subject<string>();

  get reportedReviewsCount(): number { return this.revSvc.getReportedCount(); }

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

  // ── Assign from map (create new territory for a country) ──────────────────
  mapClickedCountry: Country | null = null;
  mapAssignManagerId = '';
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
  editTarget: Territory | null = null;
  editAssignedManagerId = '';
  editCountryCode = '';
  editUnavailableCodes: string[] = [];
  isEditing = false;

  // ── Detail modal ──────────────────────────────────────────────────────────
  detailTarget: Territory | null = null;

  // ── Dropdown ─────────────────────────────────────────────────────────────
  openDropdownId: string | null = null;
  dropdownPos     = { top: 0, right: 0 };
  sortDropdownPos = { top: 0, right: 0 };

  ngOnInit(): void {
    this.allCountriesList = this.svc.getAllCountries();
    this.managers         = this.svc.getAllManagers();
    this.activeManagers   = this.svc.getActiveManagers();
    this.loadTerritories();

    this.searchSubject.pipe(
      debounceTime(300),
      distinctUntilChanged(),
      takeUntil(this.destroy$),
    ).subscribe(() => this.loadTerritories());
  }

  ngOnDestroy(): void { this.destroy$.next(); this.destroy$.complete(); }

  @HostListener('document:click')
  onDocumentClick(): void {
    let changed = false;
    if (this.openDropdownId) { this.openDropdownId = null; changed = true; }
    if (this.listSortOpen)   { this.listSortOpen = false; changed = true; }
    if (changed) this.cdr.detectChanges();
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
    }).pipe(takeUntil(this.destroy$)).subscribe(page => {
      this.territories      = page.items;
      this.totalTerritories = page.total;
      this.isLoading        = false;
      this.buildCountryAssignments();
      this.cdr.detectChanges();
    });
  }

  private buildCountryAssignments(): void {
    const map = new Map<string, CountryMapAssignment>();
    for (const t of this.territories) {
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

  // ── World map ─────────────────────────────────────────────────────────────
  onMapCountryClick(code: string): void {
    const assignment = this.countryAssignments.get(code);
    if (assignment) {
      const territory = this.territories.find(t => t.countries.includes(code));
      if (territory) {
        this.tabBeforeDetail = this.activeTab;
        this.detailTarget = { ...territory };
        this.cdr.detectChanges();
      }
      return;
    }
    const country = this.svc.getCountryByCode(code);
    if (!country) return;
    this.mapClickedCountry  = country;
    this.mapAssignManagerId = '';
    this.activeManagers     = this.svc.getActiveManagers();
    this.cdr.detectChanges();
  }

  closeMapAssignModal(): void {
    this.mapClickedCountry  = null;
    this.mapAssignManagerId = '';
    this.cdr.detectChanges();
  }

  confirmMapAssign(): void {
    if (!this.mapClickedCountry) return;
    this.isCreatingFromMap = true;
    const req: CreateTerritoryRequest = {
      name: this.mapClickedCountry.name,
      countries: [this.mapClickedCountry.code],
      assignedManagerId: this.mapAssignManagerId || null,
    };
    this.svc.create$(req).pipe(takeUntil(this.destroy$)).subscribe(() => {
      this.isCreatingFromMap  = false;
      this.mapClickedCountry  = null;
      this.mapAssignManagerId = '';
      this.loadTerritories();
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
    this.openDropdownId  = null;
    this.assignTarget    = { ...territory };
    this.assignManagerId = '';
    this.activeManagers  = this.svc.getActiveManagers();
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
      this.detailTarget = { ...territory };
      this.cdr.detectChanges();
    }, 50);
  }

  closeDetail(): void {
    this.detailTarget = null;
    this.highlightCountryCode = '';
    this.activeTab = this.tabBeforeDetail;
    this.cdr.detectChanges();
  }

  // ── Edit modal (country + manager change) ─────────────────────────────────
  private tabBeforeEdit: ViewTab = 'list';

  openEdit(territory: Territory, event?: MouseEvent): void {
    event?.stopPropagation();
    // if called from detail modal, use the tab that was active before detail opened
    this.tabBeforeEdit         = this.detailTarget ? this.tabBeforeDetail : this.activeTab;
    this.activeTab             = this.tabBeforeEdit;
    this.openDropdownId        = null;
    this.detailTarget          = null;
    this.editTarget            = { ...territory };
    this.editAssignedManagerId = territory.assignedManagerId ?? '';
    this.editCountryCode       = territory.countries[0] ?? '';
    this.editUnavailableCodes  = this.territories
      .filter(t => t.id !== territory.id)
      .flatMap(t => t.countries);
    this.activeManagers        = this.svc.getActiveManagers();
    this.cdr.detectChanges();
  }

  closeEdit(): void {
    this.activeTab = this.tabBeforeEdit;
    this.highlightCountryCode = '';
    this.editTarget = null;
    this.cdr.detectChanges();
  }

  onEditMapCountryClick(code: string): void {
    this.editCountryCode = code;
    this.cdr.detectChanges();
  }

  saveEdit(): void {
    if (!this.editTarget) return;
    this.isEditing = true;
    const managerId = this.editAssignedManagerId || null;

    this.svc.assign$(this.editTarget.id, managerId)
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => {
        this.isEditing            = false;
        this.editTarget           = null;
        this.activeTab            = this.tabBeforeEdit;
        this.highlightCountryCode = '';
        this.loadTerritories();
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
  countryName(code: string): string {
    return this.svc.getCountryByCode(code)?.name ?? code;
  }

  countryFlag(code: string): string {
    return this.svc.getCountryByCode(code)?.flag ?? '';
  }

  statusClass(status: string): string {
    return status === 'Active'
      ? 'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200'
      : 'bg-amber-50 text-amber-700 ring-1 ring-amber-200';
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
}
