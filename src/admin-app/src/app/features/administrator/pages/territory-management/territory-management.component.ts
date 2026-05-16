import {
  Component, OnInit, OnDestroy, HostListener,
  ChangeDetectionStrategy, ChangeDetectorRef, inject,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, switchMap } from 'rxjs';
import { takeUntil, debounceTime, distinctUntilChanged } from 'rxjs/operators';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { TerritoryService, CONTINENTS } from '../../../../core/services/administrator/territory.service';
import { AdminReviewsService } from '../../../../core/services/administrator/admin-reviews.service';
import { Territory, Country, TerritoryManager, CreateTerritoryRequest } from '../../../../core/models/administrator/territory.model';

type ViewTab = 'map' | 'list';
type ListSortField = 'name' | 'status' | 'countries' | 'createdAt';
type SortDir = 'asc' | 'desc';

@Component({
  selector: 'app-territory-management',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
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
  activeContinent = 'All';
  continents = ['All', ...CONTINENTS];

  // ── Country map / selector ────────────────────────────────────────────────
  countriesByContinent: Record<string, Country[]> = {};
  allCountriesList: Country[] = [];
  selectedCodes = new Set<string>();
  assignedCodes = new Set<string>();
  countrySearch = '';

  get filteredCountries(): Country[] {
    const list = this.activeContinent === 'All'
      ? this.allCountriesList
      : (this.countriesByContinent[this.activeContinent] ?? []);
    if (!this.countrySearch) return list;
    const q = this.countrySearch.toLowerCase();
    return list.filter(c => c.name.toLowerCase().includes(q));
  }

  get selectedCountries(): Country[] {
    return this.svc.getAllCountries().filter(c => this.selectedCodes.has(c.code));
  }

  // ── Territory list ────────────────────────────────────────────────────────
  territories: Territory[] = [];
  totalTerritories = 0;
  isLoading = false;
  listSearch = '';

  // ── List sort ─────────────────────────────────────────────────────────────
  listSortField: ListSortField = 'createdAt';
  listSortDir: SortDir = 'desc';
  listSortOpen = false;

  listSortOptions: { label: string; field: ListSortField; dir: SortDir }[] = [
    { label: 'Newest first',           field: 'createdAt', dir: 'desc' },
    { label: 'Oldest first',           field: 'createdAt', dir: 'asc'  },
    { label: 'Status: Active first',   field: 'status',    dir: 'asc'  },
    { label: 'Status: Unassigned first', field: 'status',  dir: 'desc' },
    { label: 'Most countries',         field: 'countries', dir: 'desc' },
    { label: 'Fewest countries',       field: 'countries', dir: 'asc'  },
    { label: 'A – Z',                  field: 'name',      dir: 'asc'  },
    { label: 'Z – A',                  field: 'name',      dir: 'desc' },
  ];

  get currentSortLabel(): string {
    return this.listSortOptions.find(o => o.field === this.listSortField && o.dir === this.listSortDir)?.label ?? 'Newest first';
  }

  // ── Country filter (multi-country search) ─────────────────────────────────
  filterCountryCodes: string[] = [];
  showCountryFilter = false;
  countryFilterQuery = '';

  get countryFilterSuggestions(): Country[] {
    if (!this.countryFilterQuery.trim()) return [];
    const q = this.countryFilterQuery.toLowerCase();
    return this.allCountriesList
      .filter(c => c.name.toLowerCase().includes(q) && !this.filterCountryCodes.includes(c.code))
      .slice(0, 8);
  }

  get countryFilterLabels(): string {
    if (this.filterCountryCodes.length === 0) return '';
    return this.filterCountryCodes
      .map(code => this.svc.getCountryByCode(code)?.name ?? code)
      .join(', ');
  }

  // ── Managers ─────────────────────────────────────────────────────────────
  managers: TerritoryManager[] = [];
  activeManagers: TerritoryManager[] = [];

  // ── Create territory panel ────────────────────────────────────────────────
  showCreatePanel = false;
  newName = '';
  newManagerId = '';
  isCreating = false;

  // ── Assign modal ─────────────────────────────────────────────────────────
  assignTarget: Territory | null = null;
  assignManagerId = '';
  isAssigning = false;

  // ── Unassign confirm ──────────────────────────────────────────────────────
  unassignTarget: Territory | null = null;
  isUnassigning = false;

  // ── Delete confirm ────────────────────────────────────────────────────────
  deleteTarget: Territory | null = null;
  isDeleting = false;

  // ── Detail modal ──────────────────────────────────────────────────────────
  detailTarget: Territory | null = null;

  // ── Edit modal ────────────────────────────────────────────────────────────
  editTarget: Territory | null = null;
  editName = '';
  editCountryCodes = new Set<string>();
  editActiveContinent = 'All';
  editCountrySearch = '';
  isEditing = false;

  get editFilteredCountries(): Country[] {
    const list = this.editActiveContinent === 'All'
      ? this.allCountriesList
      : (this.countriesByContinent[this.editActiveContinent] ?? []);
    if (!this.editCountrySearch) return list;
    const q = this.editCountrySearch.toLowerCase();
    return list.filter(c => c.name.toLowerCase().includes(q));
  }

  get editSelectedCountries(): Country[] {
    return this.allCountriesList.filter(c => this.editCountryCodes.has(c.code));
  }

  get canSaveEdit(): boolean {
    return this.editName.trim().length > 0 && this.editCountryCodes.size > 0;
  }

  // ── Dropdown ─────────────────────────────────────────────────────────────
  openDropdownId: string | null = null;
  dropdownPos        = { top: 0, right: 0 };
  filterDropdownPos  = { top: 0, left: 0 };
  sortDropdownPos    = { top: 0, right: 0 };

  ngOnInit(): void {
    this.countriesByContinent = this.svc.getCountriesByContinent();
    this.allCountriesList     = this.svc.getAllCountries();
    this.managers             = this.svc.getAllManagers();
    this.activeManagers       = this.svc.getActiveManagers();
    this.assignedCodes        = this.svc.getAssignedCountryCodes();
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
    if (this.showCountryFilter) { this.showCountryFilter = false; changed = true; }
    if (changed) this.cdr.detectChanges();
  }

  loadTerritories(): void {
    this.isLoading = true;
    this.svc.getPage$(
      this.listSearch,
      50,
      this.listSortField,
      this.listSortDir,
      this.filterCountryCodes,
    ).pipe(takeUntil(this.destroy$)).subscribe(page => {
      this.territories      = page.items;
      this.totalTerritories = page.total;
      this.assignedCodes    = this.svc.getAssignedCountryCodes();
      this.isLoading        = false;
      this.cdr.detectChanges();
    });
  }

  onListSearchChange(): void { this.searchSubject.next(this.listSearch); }

  // ── Filter / Sort dropdown openers ───────────────────────────────────────
  openCountryFilter(event: MouseEvent): void {
    event.stopPropagation();
    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
    this.filterDropdownPos = { top: rect.bottom + 4, left: rect.left };
    this.showCountryFilter = !this.showCountryFilter;
    this.listSortOpen = false;
    this.cdr.detectChanges();
  }

  openSortDropdown(event: MouseEvent): void {
    event.stopPropagation();
    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
    this.sortDropdownPos = { top: rect.bottom + 4, right: window.innerWidth - rect.right };
    this.listSortOpen = !this.listSortOpen;
    this.showCountryFilter = false;
    this.cdr.detectChanges();
  }

  // ── Sort ─────────────────────────────────────────────────────────────────
  applySortOption(opt: { field: ListSortField; dir: SortDir }): void {
    this.listSortField = opt.field;
    this.listSortDir   = opt.dir;
    this.listSortOpen  = false;
    this.loadTerritories();
    this.cdr.detectChanges();
  }

  // ── Country filter ────────────────────────────────────────────────────────
  addFilterCountry(code: string): void {
    if (!this.filterCountryCodes.includes(code)) {
      this.filterCountryCodes = [...this.filterCountryCodes, code];
    }
    this.countryFilterQuery = '';
    this.loadTerritories();
    this.cdr.detectChanges();
  }

  removeFilterCountry(code: string): void {
    this.filterCountryCodes = this.filterCountryCodes.filter(c => c !== code);
    this.loadTerritories();
    this.cdr.detectChanges();
  }

  clearCountryFilter(): void {
    this.filterCountryCodes = [];
    this.countryFilterQuery = '';
    this.showCountryFilter  = false;
    this.loadTerritories();
    this.cdr.detectChanges();
  }

  // ── Country selection (map tab) ───────────────────────────────────────────
  toggleCountry(code: string): void {
    if (this.selectedCodes.has(code)) {
      this.selectedCodes.delete(code);
    } else {
      this.selectedCodes.add(code);
    }
    this.cdr.detectChanges();
  }

  removeSelected(code: string): void {
    this.selectedCodes.delete(code);
    this.cdr.detectChanges();
  }

  clearSelection(): void {
    this.selectedCodes.clear();
    this.cdr.detectChanges();
  }

  isSelected(code: string): boolean { return this.selectedCodes.has(code); }
  isAlreadyAssigned(code: string): boolean { return this.assignedCodes.has(code); }

  // ── Create territory ─────────────────────────────────────────────────────
  openCreatePanel(): void {
    this.showCreatePanel = true;
    this.newName = '';
    this.newManagerId = '';
    this.cdr.detectChanges();
  }

  closeCreatePanel(): void {
    this.showCreatePanel = false;
    this.cdr.detectChanges();
  }

  get canCreate(): boolean {
    return this.newName.trim().length > 0 && this.selectedCodes.size > 0;
  }

  createTerritory(): void {
    if (!this.canCreate) return;
    this.isCreating = true;
    const req: CreateTerritoryRequest = {
      name: this.newName.trim(),
      countries: [...this.selectedCodes],
      assignedManagerId: this.newManagerId || null,
    };
    this.svc.create$(req).pipe(takeUntil(this.destroy$)).subscribe(() => {
      this.isCreating = false;
      this.showCreatePanel = false;
      this.selectedCodes.clear();
      this.newName = '';
      this.newManagerId = '';
      this.loadTerritories();
    });
  }

  // ── Assign manager ────────────────────────────────────────────────────────
  openAssignModal(territory: Territory, event?: MouseEvent): void {
    event?.stopPropagation();
    this.openDropdownId  = null;
    this.assignTarget    = { ...territory };
    this.assignManagerId = '';
    this.cdr.detectChanges();
  }

  closeAssignModal(): void { this.assignTarget = null; this.cdr.detectChanges(); }

  confirmAssign(): void {
    if (!this.assignTarget || !this.assignManagerId) return;
    this.isAssigning = true;
    this.svc.assign$(this.assignTarget.id, this.assignManagerId)
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => {
        this.isAssigning = false;
        this.assignTarget = null;
        this.loadTerritories();
      });
  }

  // ── Unassign manager ──────────────────────────────────────────────────────
  openUnassignConfirm(territory: Territory, event?: MouseEvent): void {
    event?.stopPropagation();
    this.openDropdownId = null;
    this.unassignTarget = { ...territory };
    this.cdr.detectChanges();
  }

  closeUnassignConfirm(): void { this.unassignTarget = null; this.cdr.detectChanges(); }

  confirmUnassign(): void {
    if (!this.unassignTarget) return;
    this.isUnassigning = true;
    this.svc.assign$(this.unassignTarget.id, null)
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => {
        this.isUnassigning = false;
        this.unassignTarget = null;
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
      this.isDeleting = false;
      this.deleteTarget = null;
      this.loadTerritories();
    });
  }

  // ── Detail modal ──────────────────────────────────────────────────────────
  openDetail(territory: Territory, event: MouseEvent): void {
    const target = event.target as HTMLElement;
    if (target.closest('button[data-action]')) return;
    this.detailTarget = { ...territory };
    this.cdr.detectChanges();
  }

  closeDetail(): void { this.detailTarget = null; this.cdr.detectChanges(); }

  // ── Edit modal ────────────────────────────────────────────────────────────
  openEdit(territory: Territory, event?: MouseEvent): void {
    event?.stopPropagation();
    this.openDropdownId      = null;
    this.detailTarget        = null;
    this.editTarget          = { ...territory };
    this.editName            = territory.name;
    this.editCountryCodes    = new Set(territory.countries);
    this.editActiveContinent = 'All';
    this.editCountrySearch   = '';
    this.cdr.detectChanges();
  }

  closeEdit(): void { this.editTarget = null; this.cdr.detectChanges(); }

  toggleEditCountry(code: string): void {
    if (this.editCountryCodes.has(code)) {
      this.editCountryCodes.delete(code);
    } else {
      this.editCountryCodes.add(code);
    }
    this.cdr.detectChanges();
  }

  removeEditCountry(code: string): void {
    this.editCountryCodes.delete(code);
    this.cdr.detectChanges();
  }

  saveEdit(): void {
    if (!this.editTarget || !this.canSaveEdit) return;
    this.isEditing = true;
    const id         = this.editTarget.id;
    const managerId  = this.editTarget.assignedManagerId;
    this.svc.updateCountries$(id, this.editName.trim(), [...this.editCountryCodes]).pipe(
      takeUntil(this.destroy$),
      switchMap(() => this.svc.assign$(id, managerId)),
    ).subscribe(() => {
      this.isEditing  = false;
      this.editTarget = null;
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
  getManagerName(id: string): string {
    return this.managers.find(m => m.id === id)?.name ?? 'Unknown';
  }

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
  trackByCode(_: number, c: Country): string { return c.code; }
  editManagerId = '';

  editAssignManager(managerId: string): void {
    if (!this.editTarget || !managerId) return;
    const mgr = this.activeManagers.find(m => m.id === managerId);
    if (!mgr) return;
    this.editTarget = {
      ...this.editTarget,
      assignedManagerId: mgr.id,
      assignedManagerName: mgr.name,
      assignedManagerEmail: mgr.email,
      status: 'Active',
    };
    this.editManagerId = '';
    this.cdr.detectChanges();
  }

  editUnassignManager(): void {
    if (!this.editTarget) return;
    this.editTarget = {
      ...this.editTarget,
      assignedManagerId: null,
      assignedManagerName: null,
      assignedManagerEmail: null,
      status: 'Unassigned',
    };
    this.cdr.detectChanges();
  }
}
