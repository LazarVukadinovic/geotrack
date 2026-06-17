import {
  AfterViewInit,
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  inject,
  NgZone,
  OnDestroy,
  OnInit,
  ViewChild,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router } from '@angular/router';
import { debounceTime, finalize, Subject, Subscription } from 'rxjs';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { AuthService } from '../../../../core/services/auth.service';
import { TerritoryDashboardService } from '../../../../core/services/territory-dashboard.service';
import { MapTileLayerService } from '../../../../core/services/map-tile-layer.service';
import { UsersService, TerritoryAssignableUser, TerritoryUserAvailableRegion } from '../../../../core/services/national/users.service';
import {
  TerritoryAssignedTerritory,
  TerritoryRegionGeometry,
  TerritoryRegionMapBounds,
  TerritoryRegionMapRegion,
  TerritoryRegionMapResponse,
  TerritoryRegionMapViewport,
} from '../../../../core/models/dashboard.model';

declare const L: any;

type RegionMapFilter = 'all' | 'assigned' | 'unassigned' | 'active' | 'inactive';

const territoryBoundaryGeometrySource = 'Territory.BoundaryGeometry';
const territoryBoundaryDisplayFallbackSource = 'Territory.BoundaryDisplayGeometryFallback';
const regionBoundaryGeometrySource = 'Region.BoundaryGeometry';
const regionBoundaryDisplayFallbackSource = 'Region.BoundaryDisplayGeometryFallback';

interface FilterOption {
  label: string;
  value: RegionMapFilter;
}

@Component({
  selector: 'app-territory-region-map',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './region-map.component.html',
  styleUrl: './region-map.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TerritoryRegionMapComponent implements OnInit, AfterViewInit, OnDestroy {
  @ViewChild('mapContainer') mapContainer?: ElementRef<HTMLDivElement>;

  readonly filters: FilterOption[] = [
    { label: 'All', value: 'all' },
    { label: 'Assigned', value: 'assigned' },
    { label: 'Unassigned', value: 'unassigned' },
    { label: 'Active', value: 'active' },
    { label: 'Inactive', value: 'inactive' },
  ];

  assignedTerritories: TerritoryAssignedTerritory[] = [];
  activeTerritoryId: number | null = null;
  mapData: TerritoryRegionMapResponse | null = null;

  territoryLoading = false;
  mapLoading = false;
  errorMessage: string | null = null;

  searchQuery = '';
  activeFilter: RegionMapFilter = 'all';
  selectedRegionId: number | null = null;
  hoveredRegionId: number | null = null;

  private map: any = null;
  private territoryLayerGroup: any = null;
  private regionLayerGroup: any = null;
  private territoryBoundaryLayer: any = null;
  private regionLayers = new Map<number, any>();
  private territoriesSubscription: Subscription | null = null;
  private mapSubscription: Subscription | null = null;
  private setupChangedSubscription: Subscription | null = null;
  private mapRequestSequence = 0;
  private pendingRender = false;
  private pendingFitViewport = true;
  private readonly mapTileLayer = inject(MapTileLayerService);

  // Assign Manager Modal state
  assignModalOpen = false;
  assignModalRegionName = '';
  assignModalUserSearch = '';
  assignModalUsers: TerritoryAssignableUser[] = [];
  assignModalUsersLoading = false;
  assignModalSelectedUser: TerritoryAssignableUser | null = null;
  assignModalRegionSearch = '';
  assignModalAvailableRegions: TerritoryUserAvailableRegion[] = [];
  assignModalRegionsLoading = false;
  assignModalSelectedRegions: TerritoryUserAvailableRegion[] = [];
  assignModalSaving = false;
  assignModalError: string | null = null;

  private assignModalUserSearch$ = new Subject<string>();
  private assignModalRegionSearch$ = new Subject<string>();
  private assignModalSubscriptions: Subscription[] = [];

  constructor(
    private readonly zone: NgZone,
    private readonly cdr: ChangeDetectorRef,
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly auth: AuthService,
    private readonly territoryDashboard: TerritoryDashboardService,
    private readonly usersService: UsersService,
  ) {}

  ngOnInit(): void {
    this.setupChangedSubscription = this.territoryDashboard.regionSetupChanged$
      .subscribe(() => this.refresh());
    this.loadAssignedTerritories();
  }

  ngAfterViewInit(): void {
    setTimeout(() => this.initMap(), 0);
  }

  ngOnDestroy(): void {
    this.mapRequestSequence++;
    this.territoriesSubscription?.unsubscribe();
    this.mapSubscription?.unsubscribe();
    this.setupChangedSubscription?.unsubscribe();
    this.assignModalSubscriptions.forEach(s => s.unsubscribe());

    if (this.map) {
      this.map.remove();
      this.map = null;
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

  get activeTerritoryDisplayName(): string {
    return this.mapData?.territory?.displayName || this.activeTerritoryName || 'No assigned territory';
  }

  get regions(): TerritoryRegionMapRegion[] {
    return this.mapData?.regions ?? [];
  }

  get filteredRegions(): TerritoryRegionMapRegion[] {
    const query = this.searchQuery.trim().toLowerCase();

    return this.regions.filter(region => {
      if (!this.matchesFilter(region)) return false;
      if (!query) return true;

      return [
        region.displayName,
        region.name,
        region.status,
        this.managerLabel(region),
        ...(region.assignedManagers ?? []).map(manager => manager.email ?? ''),
      ]
        .filter(Boolean)
        .some(value => String(value).toLowerCase().includes(query));
    });
  }

  get selectedRegion(): TerritoryRegionMapRegion | null {
    return this.regions.find(region => region.regionId === this.selectedRegionId) ?? null;
  }

  get totalRegions(): number {
    return this.regions.length;
  }

  get assignedRegionCount(): number {
    return this.regions.filter(region => this.isAssigned(region)).length;
  }

  get unassignedRegionCount(): number {
    return this.regions.filter(region => !this.isAssigned(region)).length;
  }

  get missingBoundaryCount(): number {
    return this.regions.filter(region => this.hasBoundaryIssue(region)).length;
  }

  get activeRegionCount(): number {
    return this.regions.filter(region => this.isActiveStatus(region)).length;
  }

  get inactiveRegionCount(): number {
    return this.regions.filter(region => !this.isActiveStatus(region)).length;
  }

  get hasVisibleRegions(): boolean {
    return this.filteredRegions.length > 0;
  }

  get emptyMessage(): string {
    if (!this.activeTerritoryId && !this.territoryLoading) {
      return 'No territory is assigned to this account.';
    }

    if (this.searchQuery.trim() || this.activeFilter !== 'all') {
      return 'No regions match the current search or filters.';
    }

    return this.mapData?.emptyStateMessage ?? 'No regions are available for this territory. Sync ADM1 regions first.';
  }

  get boundaryNotice(): string | null {
    if (this.mapData?.boundaryWarningMessage) {
      return this.mapData.boundaryWarningMessage;
    }

    if (!this.mapData) {
      return null;
    }

    if (this.mapData.territory && !this.hasPreciseTerritoryBoundary(this.mapData.territory)) {
      return 'Precise territory boundary data is missing for this map.';
    }

    return this.regions.some(region => this.hasBoundaryIssue(region))
      ? 'Precise boundary data is missing for one or more regions.'
      : null;
  }

  get mapHasRenderableRegions(): boolean {
    return this.regions.some(region => this.hasRenderableRegionBoundary(region));
  }

  onTerritorySelected(territoryId: number): void {
    if (territoryId === this.activeTerritoryId) return;
    if (!this.assignedTerritories.some(item => item.id === territoryId)) return;

    this.setActiveTerritory(territoryId, true);
    this.searchQuery = '';
    this.activeFilter = 'all';
    this.selectedRegionId = null;
    this.hoveredRegionId = null;
    this.mapData = null;
    this.clearMapLayers();
    this.loadRegionMap(territoryId, true);
    this.notifyStateChanged();
  }

  setFilter(filter: RegionMapFilter): void {
    this.activeFilter = filter;
    this.notifyStateChanged();
  }

  clearSearch(): void {
    this.searchQuery = '';
    this.notifyStateChanged();
  }

  selectRegion(region: TerritoryRegionMapRegion, fitToRegion = false): void {
    this.selectedRegionId = region.regionId;
    this.hoveredRegionId = null;
    this.updateRegionLayerStyles();

    if (fitToRegion) {
      this.fitRegion(region);
    }

    this.notifyStateChanged();
  }

  viewSelectedRegion(): void {
    const region = this.selectedRegion;
    if (!region) return;

    this.router.navigate([region.detailRoute || `/territory/regions/${region.regionId}`], {
      state: { territoryId: region.territoryId },
    });
  }

  manageSelectedAssignment(): void {
    const region = this.selectedRegion;
    if (!region) return;

    this.openAssignModal(region);
  }

  openAssignModal(region: TerritoryRegionMapRegion): void {
    this.assignModalOpen = true;
    this.assignModalRegionName = region.displayName || region.name || '';
    this.assignModalUserSearch = '';
    this.assignModalUsers = [];
    this.assignModalSelectedUser = null;
    this.assignModalRegionSearch = '';
    this.assignModalAvailableRegions = [];
    this.assignModalSelectedRegions = [];
    this.assignModalSaving = false;
    this.assignModalError = null;

    // Wire up debounced search inputs
    this.assignModalSubscriptions.forEach(s => s.unsubscribe());
    this.assignModalSubscriptions = [];

    const userSub = this.assignModalUserSearch$
      .pipe(debounceTime(300))
      .subscribe(query => this.fetchAssignModalUsers(query));
    const regionSub = this.assignModalRegionSearch$
      .pipe(debounceTime(300))
      .subscribe(query => this.fetchAssignModalRegions(query));

    this.assignModalSubscriptions.push(userSub, regionSub);

    // Pre-populate with the current manager if assigned
    if (region.assignedManagers?.length) {
      const manager = region.assignedManagers[0];
      // Pre-fill search with manager display name to give context
      this.assignModalUserSearch = manager.displayName || manager.email || '';
    }

    this.fetchAssignModalUsers(this.assignModalUserSearch);
    this.notifyStateChanged();
  }

  closeAssignModal(): void {
    this.assignModalOpen = false;
    this.assignModalSubscriptions.forEach(s => s.unsubscribe());
    this.assignModalSubscriptions = [];
    this.notifyStateChanged();
  }

  onAssignModalUserSearchChange(): void {
    this.assignModalUserSearch$.next(this.assignModalUserSearch);
  }

  onAssignModalRegionSearchChange(): void {
    this.assignModalRegionSearch$.next(this.assignModalRegionSearch);
  }

  selectAssignModalUser(user: TerritoryAssignableUser): void {
    this.assignModalSelectedUser = user;
    this.assignModalAvailableRegions = [];
    this.assignModalSelectedRegions = [];
    this.assignModalRegionSearch = '';
    this.fetchAssignModalRegions('');
    this.notifyStateChanged();
  }

  addAssignModalRegion(region: TerritoryUserAvailableRegion): void {
    if (!this.assignModalSelectedRegions.some(r => r.regionId === region.regionId)) {
      this.assignModalSelectedRegions = [...this.assignModalSelectedRegions, region];
    }
    this.assignModalAvailableRegions = this.assignModalAvailableRegions.filter(r => r.regionId !== region.regionId);
    this.notifyStateChanged();
  }

  removeAssignModalRegion(regionId: number): void {
    this.assignModalSelectedRegions = this.assignModalSelectedRegions.filter(r => r.regionId !== regionId);
    this.notifyStateChanged();
  }

  saveAssignModal(): void {
    const user = this.assignModalSelectedUser;
    const territoryId = this.activeTerritoryId;
    if (!user || !territoryId) return;

    const regionIds = this.assignModalSelectedRegions.map(r => r.regionId);
    this.assignModalSaving = true;
    this.assignModalError = null;
    this.notifyStateChanged();

    this.usersService.addRegionalManager$(user.userId, territoryId, regionIds)
      .pipe(finalize(() => {
        this.assignModalSaving = false;
        this.notifyStateChanged();
      }))
      .subscribe({
        next: () => {
          this.closeAssignModal();
          this.refresh();
        },
        error: (err: HttpErrorResponse) => {
          this.assignModalError = err?.error?.message || err?.message || 'Failed to assign manager.';
          this.notifyStateChanged();
        },
      });
  }

  private fetchAssignModalUsers(query: string): void {
    const territoryId = this.activeTerritoryId;
    if (!territoryId) return;

    this.assignModalUsersLoading = true;
    this.notifyStateChanged();

    this.usersService.searchAssignableUsers$(territoryId, query, 20)
      .pipe(finalize(() => {
        this.assignModalUsersLoading = false;
        this.notifyStateChanged();
      }))
      .subscribe({
        next: users => {
          this.assignModalUsers = users ?? [];
          this.notifyStateChanged();
        },
        error: () => {
          this.assignModalUsers = [];
          this.notifyStateChanged();
        },
      });
  }

  private fetchAssignModalRegions(query: string): void {
    const user = this.assignModalSelectedUser;
    const territoryId = this.activeTerritoryId;
    if (!user || !territoryId) return;

    this.assignModalRegionsLoading = true;
    this.notifyStateChanged();

    this.usersService.getAvailableRegions$(user.userId, territoryId, query, 50)
      .pipe(finalize(() => {
        this.assignModalRegionsLoading = false;
        this.notifyStateChanged();
      }))
      .subscribe({
        next: regions => {
          // Exclude already-selected
          const selectedIds = new Set(this.assignModalSelectedRegions.map(r => r.regionId));
          this.assignModalAvailableRegions = (regions ?? []).filter(r => !selectedIds.has(r.regionId));
          this.notifyStateChanged();
        },
        error: () => {
          this.assignModalAvailableRegions = [];
          this.notifyStateChanged();
        },
      });
  }

  refresh(): void {
    if (this.activeTerritoryId) {
      this.loadRegionMap(this.activeTerritoryId, true);
    } else {
      this.loadAssignedTerritories();
    }
  }

  trackByRegionId(_index: number, region: TerritoryRegionMapRegion): number {
    return region.regionId;
  }

  filterCount(filter: RegionMapFilter): number {
    switch (filter) {
      case 'assigned':
        return this.assignedRegionCount;
      case 'unassigned':
        return this.unassignedRegionCount;
      case 'active':
        return this.activeRegionCount;
      case 'inactive':
        return this.inactiveRegionCount;
      default:
        return this.totalRegions;
    }
  }

  isAssigned(region: TerritoryRegionMapRegion): boolean {
    return (region.assignedManagers?.length ?? 0) > 0;
  }

  isSelected(region: TerritoryRegionMapRegion): boolean {
    return region.regionId === this.selectedRegionId;
  }

  isHovered(region: TerritoryRegionMapRegion): boolean {
    return region.regionId === this.hoveredRegionId;
  }

  isActiveStatus(region: TerritoryRegionMapRegion): boolean {
    return (region.status ?? '').toLowerCase() === 'active';
  }

  managerLabel(region: TerritoryRegionMapRegion): string {
    if (region.managerDisplayName?.trim()) {
      return region.managerDisplayName.trim();
    }

    const managers = region.assignedManagers ?? [];
    if (managers.length === 0) {
      return 'No manager assigned';
    }

    return managers
      .map(manager => manager.displayName || manager.email)
      .filter(Boolean)
      .join(', ');
  }

  assignmentActionLabel(region: TerritoryRegionMapRegion | null): string {
    if (!region) return 'Manage assignment';
    return this.isAssigned(region) ? 'Change Manager' : 'Assign Manager';
  }

  contentTotal(region: TerritoryRegionMapRegion): number {
    const counts = region.contentCounts;
    return counts.destinations + counts.locations + counts.places + counts.stays + counts.events;
  }

  healthLabel(region: TerritoryRegionMapRegion): string {
    return typeof region.healthScore === 'number' ? String(Math.round(region.healthScore)) : 'No score';
  }

  statusClass(status: string | null | undefined): string {
    return (status ?? '').toLowerCase() === 'active'
      ? 'bg-emerald-50 text-emerald-700 border-emerald-200'
      : 'bg-slate-100 text-slate-600 border-slate-200';
  }

  healthClass(region: TerritoryRegionMapRegion): string {
    if (typeof region.healthScore !== 'number') {
      return 'bg-slate-100 text-slate-500 border-slate-200';
    }

    if (region.healthScore >= 80) {
      return 'bg-emerald-50 text-emerald-700 border-emerald-200';
    }

    if (region.healthScore >= 50) {
      return 'bg-amber-50 text-amber-700 border-amber-200';
    }

    return 'bg-red-50 text-red-600 border-red-200';
  }

  hasBoundaryIssue(region: TerritoryRegionMapRegion): boolean {
    return !this.hasPreciseRegionBoundary(region);
  }

  regionBoundaryBadgeLabel(region: TerritoryRegionMapRegion): string {
    if (this.usesFallbackRegionBoundary(region)) {
      return 'Fallback boundary';
    }

    if (region.hasBoundary && region.geometryGeoJson && !this.hasPreciseRegionBoundary(region)) {
      return 'Display boundary ignored';
    }

    return 'Missing boundary';
  }

  regionBoundaryMessage(region: TerritoryRegionMapRegion): string {
    if (region.boundaryMissingReason?.trim()) {
      return region.boundaryMissingReason.trim();
    }

    if (this.usesFallbackRegionBoundary(region)) {
      return 'Display boundary fallback is shown because the full region boundary is missing.';
    }

    if (region.hasBoundary && region.geometryGeoJson && !this.hasPreciseRegionBoundary(region)) {
      return 'This boundary is not from Region.BoundaryGeometry, so it is not drawn on the accuracy map.';
    }

    return 'Boundary data is missing for this region.';
  }

  private loadAssignedTerritories(): void {
    this.territoryLoading = true;
    this.errorMessage = null;
    this.notifyStateChanged();

    this.territoriesSubscription?.unsubscribe();
    this.territoriesSubscription = this.territoryDashboard.getAssignedTerritories()
      .pipe(
        finalize(() => {
          this.territoryLoading = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: territories => {
          this.assignedTerritories = territories ?? [];

          if (this.assignedTerritories.length === 0) {
            this.activeTerritoryId = null;
            this.mapData = null;
            this.selectedRegionId = null;
            this.clearMapLayers();
            return;
          }

          const selectedTerritory = this.resolveInitialTerritory();
          this.setActiveTerritory(selectedTerritory.id, false);
          this.loadRegionMap(selectedTerritory.id, true);
        },
        error: error => this.handleLoadError(error, 'Unable to load assigned territories.'),
      });
  }

  private loadRegionMap(territoryId: number, fitViewport: boolean): void {
    const requestId = ++this.mapRequestSequence;
    this.mapLoading = true;
    this.errorMessage = null;
    this.notifyStateChanged();

    this.mapSubscription?.unsubscribe();
    this.mapSubscription = this.territoryDashboard.getRegionMap(territoryId)
      .pipe(
        finalize(() => {
          if (requestId === this.mapRequestSequence) {
            this.mapLoading = false;
            this.notifyStateChanged();
          }
        }),
      )
      .subscribe({
        next: response => {
          if (requestId !== this.mapRequestSequence) return;

          this.mapData = response;
          this.selectedRegionId = this.resolveSelectedRegionId(response.regions ?? []);
          this.hoveredRegionId = null;
          this.renderMapData(fitViewport);
          this.notifyStateChanged();
        },
        error: error => {
          if (requestId !== this.mapRequestSequence) return;

          this.mapData = null;
          this.selectedRegionId = null;
          this.hoveredRegionId = null;
          this.clearMapLayers();
          this.handleLoadError(error, 'Unable to load region map data.');
        },
      });
  }

  private resolveInitialTerritory(): TerritoryAssignedTerritory {
    const queryTerritoryId = Number(this.route.snapshot.queryParamMap.get('territoryId'));
    const navigationState = history.state as { territoryId?: number | null };
    const stateTerritoryId = Number(navigationState?.territoryId);
    const storedTerritoryId = this.territoryDashboard.getActiveTerritoryId();

    return this.assignedTerritories.find(item => item.id === queryTerritoryId) ??
      this.assignedTerritories.find(item => item.id === stateTerritoryId) ??
      this.assignedTerritories.find(item => item.id === storedTerritoryId) ??
      this.assignedTerritories[0];
  }

  private setActiveTerritory(territoryId: number, updateUrl: boolean): void {
    this.activeTerritoryId = territoryId;
    this.territoryDashboard.setActiveTerritoryId(territoryId);

    if (updateUrl) {
      this.router.navigate([], {
        relativeTo: this.route,
        queryParams: { territoryId },
        queryParamsHandling: 'merge',
        replaceUrl: true,
      });
    }
  }

  private resolveSelectedRegionId(regions: TerritoryRegionMapRegion[]): number | null {
    if (this.selectedRegionId && regions.some(region => region.regionId === this.selectedRegionId)) {
      return this.selectedRegionId;
    }

    return regions.find(region => this.isAssigned(region))?.regionId ?? regions[0]?.regionId ?? null;
  }

  private initMap(): void {
    const element = this.mapContainer?.nativeElement;
    if (!element || this.map || typeof L === 'undefined') {
      return;
    }

    this.zone.runOutsideAngular(() => {
      this.map = L.map(element, {
        zoomControl: false,
        attributionControl: false,
        preferCanvas: true,
      });

      L.control?.zoom?.({ position: 'topleft' })?.addTo(this.map);

      this.mapTileLayer.addTo(L, this.map);

      this.createMapPanes();
      this.territoryLayerGroup = L.layerGroup().addTo(this.map);
      this.regionLayerGroup = L.layerGroup().addTo(this.map);
      this.map.fitWorld({ animate: false });
    });

    setTimeout(() => {
      this.map?.invalidateSize();
      if (this.pendingRender || this.mapData) {
        this.renderMapData(this.pendingFitViewport);
      }
    }, 0);
  }

  private createMapPanes(): void {
    this.createPane('territory-outline-pane', 330);
    this.createPane('territory-region-pane', 370);
  }

  private createPane(name: string, zIndex: number): void {
    if (!this.map.getPane(name)) {
      this.map.createPane(name);
    }

    const pane = this.map.getPane(name) as HTMLElement | null;
    if (pane) {
      pane.style.zIndex = String(zIndex);
    }
  }

  private renderMapData(fitViewport: boolean): void {
    if (!this.map || typeof L === 'undefined') {
      this.pendingRender = true;
      this.pendingFitViewport = fitViewport;
      return;
    }

    this.pendingRender = false;
    this.pendingFitViewport = fitViewport;
    this.clearMapLayers();

    const data = this.mapData;
    if (!data) return;

    this.addTerritoryBoundary(data.territory ?? null);
    data.regions.forEach(region => this.addRegionLayer(region));
    this.updateRegionLayerStyles();

    if (fitViewport) {
      this.applyInitialViewport();
    }
  }

  private clearMapLayers(): void {
    this.territoryLayerGroup?.clearLayers();
    this.regionLayerGroup?.clearLayers();
    this.territoryBoundaryLayer = null;
    this.regionLayers.clear();
  }

  private addTerritoryBoundary(territory: TerritoryRegionMapResponse['territory']): void {
    if (!this.hasRenderableTerritoryBoundary(territory)) {
      return;
    }

    this.territoryBoundaryLayer = L.geoJSON(territory.geometryGeoJson, {
      pane: 'territory-outline-pane',
      interactive: false,
      style: {
        color: '#1d4ed8',
        weight: 2.6,
        opacity: 0.96,
        fillColor: '#dbeafe',
        fillOpacity: 0.1,
      },
    }).addTo(this.territoryLayerGroup);
  }

  private addRegionLayer(region: TerritoryRegionMapRegion): void {
    if (!this.hasRenderableRegionBoundary(region)) {
      return;
    }

    const layer = L.geoJSON(region.geometryGeoJson, {
      pane: 'territory-region-pane',
      interactive: true,
      style: () => this.styleForRegion(region),
    });

    layer.bindTooltip(this.regionTooltip(region), { sticky: true });
    layer.on('click', (event: { originalEvent?: Event }) => {
      if (event.originalEvent && L.DomEvent?.stopPropagation) {
        L.DomEvent.stopPropagation(event.originalEvent);
      }

      this.zone.run(() => this.selectRegion(region, false));
    });
    layer.on('mouseover', () => {
      this.zone.run(() => {
        this.hoveredRegionId = region.regionId;
        this.updateRegionLayerStyles();
        this.notifyStateChanged();
      });
      this.setMapCursor('pointer');
    });
    layer.on('mouseout', () => {
      this.zone.run(() => {
        if (this.hoveredRegionId === region.regionId) {
          this.hoveredRegionId = null;
        }
        this.updateRegionLayerStyles();
        this.notifyStateChanged();
      });
      this.setMapCursor('');
    });

    layer.addTo(this.regionLayerGroup);
    this.regionLayers.set(region.regionId, layer);
  }

  private styleForRegion(region: TerritoryRegionMapRegion): Record<string, unknown> {
    const fallback = this.usesFallbackRegionBoundary(region);

    if (this.isSelected(region)) {
      return {
        color: fallback ? '#92400e' : '#111827',
        weight: 3,
        opacity: 1,
        fillColor: fallback ? '#fbbf24' : this.isAssigned(region) ? '#059669' : '#2563eb',
        fillOpacity: fallback ? 0.34 : 0.48,
        dashArray: fallback ? '5 3' : undefined,
      };
    }

    if (fallback) {
      return {
        color: '#d97706',
        weight: 2,
        opacity: 0.95,
        fillColor: '#fde68a',
        fillOpacity: 0.24,
        dashArray: '5 3',
      };
    }

    if (this.isHovered(region)) {
      return {
        color: this.isAssigned(region) ? '#047857' : '#1d4ed8',
        weight: 2.4,
        opacity: 1,
        fillColor: this.isAssigned(region) ? '#34d399' : '#60a5fa',
        fillOpacity: 0.42,
      };
    }

    if (this.isAssigned(region)) {
      return {
        color: '#047857',
        weight: 1.8,
        opacity: 0.95,
        fillColor: '#86efac',
        fillOpacity: 0.34,
      };
    }

    return {
      color: '#2563eb',
      weight: 1.4,
      opacity: 0.85,
      fillColor: '#bfdbfe',
      fillOpacity: 0.24,
      dashArray: '4 4',
    };
  }

  private updateRegionLayerStyles(): void {
    this.regionLayers.forEach((layer, regionId) => {
      const region = this.regions.find(item => item.regionId === regionId);
      if (region && typeof layer.setStyle === 'function') {
        layer.setStyle(this.styleForRegion(region));
      }
    });
  }

  private regionTooltip(region: TerritoryRegionMapRegion): string {
    const manager = this.escapeHtml(this.managerLabel(region));
    const status = this.escapeHtml(region.status || 'Unknown');
    const health = typeof region.healthScore === 'number'
      ? `${Math.round(region.healthScore)}`
      : 'No score';

    return `
      <strong>${this.escapeHtml(region.displayName || region.name)}</strong><br>
      <span style="font-size:11px;color:#475569;">${manager}</span><br>
      <span style="font-size:11px;color:#475569;">Status: ${status} | Health: ${health}</span>
    `;
  }

  private applyInitialViewport(): void {
    if (!this.map) return;

    try {
      if (this.territoryBoundaryLayer?.getBounds) {
        this.map.fitBounds(this.territoryBoundaryLayer.getBounds(), {
          padding: [36, 36],
          animate: false,
        });
        return;
      }

      if (this.fitViewportBounds(this.mapData?.territory?.viewport ?? null, false)) return;
      if (this.fitViewportBounds(this.mapData?.viewport ?? null, false)) return;

      this.map.fitWorld({ animate: false });
    } catch {
      // Leaflet can throw while a hidden/resizing container settles.
    }
  }

  private fitRegion(region: TerritoryRegionMapRegion): void {
    if (!this.map) return;

    try {
      const layer = this.regionLayers.get(region.regionId);
      if (layer?.getBounds) {
        this.map.fitBounds(layer.getBounds(), {
          padding: [42, 42],
          maxZoom: 9,
          animate: true,
        });
        return;
      }

      this.fitViewportBounds(region.viewport, true);
    } catch {
      // Keep selection even if the map cannot fit yet.
    }
  }

  private fitViewportBounds(viewport: TerritoryRegionMapViewport | null | undefined, animate: boolean): boolean {
    if (!this.map || !viewport || viewport.isEmpty) return false;

    if (this.isValidBounds(viewport.bounds)) {
      this.map.fitBounds(
        [
          [viewport.bounds.south, viewport.bounds.west],
          [viewport.bounds.north, viewport.bounds.east],
        ],
        { padding: [36, 36], maxZoom: 9, animate },
      );
      return true;
    }

    if (
      typeof viewport.centerLatitude === 'number' &&
      typeof viewport.centerLongitude === 'number' &&
      typeof viewport.zoom === 'number'
    ) {
      this.map.setView([viewport.centerLatitude, viewport.centerLongitude], viewport.zoom, { animate });
      return true;
    }

    return false;
  }

  private isValidBounds(bounds: TerritoryRegionMapBounds | null | undefined): bounds is TerritoryRegionMapBounds {
    if (!bounds) return false;

    return (
      typeof bounds.north === 'number' &&
      typeof bounds.south === 'number' &&
      typeof bounds.east === 'number' &&
      typeof bounds.west === 'number' &&
      bounds.north !== bounds.south &&
      bounds.east !== bounds.west
    );
  }

  private matchesFilter(region: TerritoryRegionMapRegion): boolean {
    switch (this.activeFilter) {
      case 'assigned':
        return this.isAssigned(region);
      case 'unassigned':
        return !this.isAssigned(region);
      case 'active':
        return this.isActiveStatus(region);
      case 'inactive':
        return !this.isActiveStatus(region);
      default:
        return true;
    }
  }

  private isSupportedGeometry(geometry: TerritoryRegionGeometry): boolean {
    const type = (geometry.type ?? '').toLowerCase();
    return type === 'polygon' || type === 'multipolygon';
  }

  private hasPreciseTerritoryBoundary(territory: TerritoryRegionMapResponse['territory']): boolean {
    return Boolean(
      territory?.hasBoundary &&
      territory.geometryGeoJson &&
      territory.geometrySource === territoryBoundaryGeometrySource &&
      this.isSupportedGeometry(territory.geometryGeoJson),
    );
  }

  private hasRenderableTerritoryBoundary(territory: TerritoryRegionMapResponse['territory']): territory is NonNullable<TerritoryRegionMapResponse['territory']> {
    return Boolean(
      territory?.hasBoundary &&
      territory.geometryGeoJson &&
      this.isSupportedGeometry(territory.geometryGeoJson) &&
      (
        territory.geometrySource === territoryBoundaryGeometrySource ||
        territory.geometrySource === territoryBoundaryDisplayFallbackSource
      ),
    );
  }

  private hasPreciseRegionBoundary(region: TerritoryRegionMapRegion): boolean {
    return Boolean(
      region.hasBoundary &&
      region.geometryGeoJson &&
      region.geometrySource === regionBoundaryGeometrySource &&
      this.isSupportedGeometry(region.geometryGeoJson),
    );
  }

  private hasRenderableRegionBoundary(region: TerritoryRegionMapRegion): boolean {
    return this.hasPreciseRegionBoundary(region) || this.usesFallbackRegionBoundary(region);
  }

  private usesFallbackRegionBoundary(region: TerritoryRegionMapRegion): boolean {
    return Boolean(
      region.hasBoundary &&
      region.geometryGeoJson &&
      region.geometrySource === regionBoundaryDisplayFallbackSource &&
      this.isSupportedGeometry(region.geometryGeoJson),
    );
  }

  private setMapCursor(cursor: string): void {
    const container = this.map?.getContainer?.() as HTMLElement | null;
    if (container) {
      container.style.cursor = cursor;
    }
  }

  private handleLoadError(error: unknown, fallbackMessage: string): void {
    const httpError = error as HttpErrorResponse;
    if (httpError?.status === 401 || httpError?.status === 403) {
      this.errorMessage = 'You are not authorized to view this territory map.';
    } else {
      this.errorMessage = httpError?.error?.message || httpError?.message || fallbackMessage;
    }

    this.notifyStateChanged();
  }

  private escapeHtml(value: string): string {
    return value
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#039;');
  }

  private notifyStateChanged(): void {
    this.cdr.markForCheck();
  }
}
