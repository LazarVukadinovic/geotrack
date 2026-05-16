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
import { TerritoryService, CONTINENTS } from '../../../../core/services/administrator/territory.service';
import { Territory, Country, TerritoryManager, CreateTerritoryRequest } from '../../../../core/models/administrator/territory.model';

type ViewTab = 'map' | 'list';

@Component({
  selector: 'app-territory-management',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './territory-management.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TerritoryManagementComponent implements OnInit, OnDestroy {
  private readonly svc = inject(TerritoryService);
  private readonly cdr = inject(ChangeDetectorRef);
  private destroy$ = new Subject<void>();
  private searchSubject = new Subject<string>();

  // ── View state ────────────────────────────────────────────────────────────
  activeTab: ViewTab = 'map';
  activeContinent = 'Europe';
  continents = [...CONTINENTS];

  // ── Country map / selector ────────────────────────────────────────────────
  countriesByContinent: Record<string, Country[]> = {};
  selectedCodes = new Set<string>();
  assignedCodes = new Set<string>();
  countrySearch = '';

  get filteredCountries(): Country[] {
    const list = this.countriesByContinent[this.activeContinent] ?? [];
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

  // ── Delete confirm ────────────────────────────────────────────────────────
  deleteTarget: Territory | null = null;
  isDeleting = false;

  // ── Dropdown ─────────────────────────────────────────────────────────────
  openDropdownId: string | null = null;
  dropdownPos = { top: 0, right: 0 };

  ngOnInit(): void {
    this.countriesByContinent = this.svc.getCountriesByContinent();
    this.managers       = this.svc.getAllManagers();
    this.activeManagers = this.svc.getActiveManagers();
    this.assignedCodes  = this.svc.getAssignedCountryCodes();
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
    if (this.openDropdownId) { this.openDropdownId = null; this.cdr.detectChanges(); }
  }

  loadTerritories(): void {
    this.isLoading = true;
    this.svc.getPage$(this.listSearch).pipe(takeUntil(this.destroy$)).subscribe(page => {
      this.territories      = page.items;
      this.totalTerritories = page.total;
      this.assignedCodes    = this.svc.getAssignedCountryCodes();
      this.isLoading        = false;
      this.cdr.detectChanges();
    });
  }

  onListSearchChange(): void { this.searchSubject.next(this.listSearch); }

  // ── Country selection ────────────────────────────────────────────────────
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

  countryClass(code: string): string {
    if (this.isSelected(code))       return 'country-chip selected';
    if (this.isAlreadyAssigned(code)) return 'country-chip assigned';
    return 'country-chip';
  }

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
    this.openDropdownId = null;
    this.assignTarget    = { ...territory };
    this.assignManagerId = territory.assignedManagerId ?? '';
    this.cdr.detectChanges();
  }

  closeAssignModal(): void { this.assignTarget = null; this.cdr.detectChanges(); }

  confirmAssign(): void {
    if (!this.assignTarget) return;
    this.isAssigning = true;
    this.svc.assign$(this.assignTarget.id, this.assignManagerId || null)
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => {
        this.isAssigning = false;
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
      this.isDeleting = false;
      this.deleteTarget = null;
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

  trackById(_: number, t: Territory): string { return t.id; }
  trackByCode(_: number, c: Country): string { return c.code; }
}
