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
import { ActivatedRoute, Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { finalize, Subscription } from 'rxjs';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { AuthService } from '../../../../core/services/auth.service';
import { DashboardService } from '../../../../core/services/dashboard.service';
import { AdminMapService } from '../../../../core/services/admin-map.service';
import { MapTileLayerService } from '../../../../core/services/map-tile-layer.service';
import { RegionalAssignedRegion } from '../../../../core/models/dashboard.model';
import {
  AdminMapAssignedRegionBoundary,
  AdminMapMarker,
  AdminMapResponse,
  AdminMapSearchResult,
  AdminMapZone,
} from '../../../../core/models/admin-map.model';

declare const L: any;

interface DroppedPin {
  latitude: number;
  longitude: number;
}

interface PopupPosition {
  x: number;
  y: number;
}

interface PopupEntity {
  id?: number | null;
  entityType: AdminMapMarker['entityType'];
  title: string;
  subtitle?: string | null;
  categoryLabel?: string | null;
  latitude: number;
  longitude: number;
  imageUrl?: string | null;
  status?: string | null;
  detailRoute?: string | null;
  editRoute?: string | null;
}

type MapEntityFilter = 'all' | 'place' | 'location' | 'destination';

@Component({
  selector: 'app-map-page',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './map-page.component.html',
  styleUrl: './map-page.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MapPageComponent implements OnInit, AfterViewInit, OnDestroy {
  private static readonly activeRegionStorageKey = 'geotrack_admin_active_region_id';
  private static readonly defaultRegionTypes = ['destination', 'location', 'place'];
  private static readonly searchDebounceMs = 250;

  @ViewChild('mapContainer') mapContainer?: ElementRef<HTMLDivElement>;

  assignedRegions: RegionalAssignedRegion[] = [];
  activeRegionId: number | null = null;
  regionLoading = false;
  mapLoading = false;
  searchLoading = false;
  errorMessage: string | null = null;
  authRequired = false;
  searchQuery = '';
  searchResults: AdminMapSearchResult[] = [];
  searchDropdownOpen = false;
  searchResultsCache: AdminMapSearchResult[] = [];
  entityFilter: MapEntityFilter = 'all';
  filterMenuOpen = false;
  pinValidationPending = false;
  pinValidationMessage: string | null = null;
  pinValidationTone: 'error' | 'success' | null = null;

  mapData: AdminMapResponse | null = null;
  selectedEntity: PopupEntity | null = null;
  selectedZone: AdminMapZone | null = null;
  stackedEntities: PopupEntity[] | null = null;
  popupPosition: PopupPosition | null = null;
  droppedPin: DroppedPin | null = null;
  droppedPinPosition: PopupPosition | null = null;

  private map: any = null;
  private regionZoneGroup: any = null;
  private zoneGroup: any = null;
  private markerGroup: any = null;
  private droppedPinGroup: any = null;
  private droppedPinMarker: any = null;
  private assignedRegionsSubscription: Subscription | null = null;
  private mapSubscription: Subscription | null = null;
  private searchSubscription: Subscription | null = null;
  private searchHandle: ReturnType<typeof setTimeout> | null = null;
  private mapRequestSequence = 0;
  private pendingRender = false;
  private readonly mapTileLayer = inject(MapTileLayerService);

  constructor(
    private readonly zone: NgZone,
    private readonly cdr: ChangeDetectorRef,
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly auth: AuthService,
    private readonly dashboardService: DashboardService,
    private readonly adminMapService: AdminMapService,
  ) {}

  ngOnInit(): void {
    this.loadAssignedRegions();
  }

  ngAfterViewInit(): void {
    this.initMap();
  }

  ngOnDestroy(): void {
    this.mapRequestSequence++;
    this.assignedRegionsSubscription?.unsubscribe();
    this.mapSubscription?.unsubscribe();
    this.searchSubscription?.unsubscribe();

    if (this.searchHandle) {
      clearTimeout(this.searchHandle);
    }

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

  get activeRegion(): RegionalAssignedRegion | null {
    return this.assignedRegions.find(region => region.id === this.activeRegionId) ?? null;
  }

  get regionDisplayName(): string {
    return this.activeRegion?.displayName ?? '';
  }

  get waitingForRegion(): boolean {
    return this.regionLoading && !this.activeRegionId;
  }

  get emptyMessage(): string {
    if (!this.activeRegionId) return 'No active assigned region.';
    return this.mapData?.emptyStateMessage ?? 'No mapped entities found in this assigned region.';
  }

  get hasMapContent(): boolean {
    return Boolean(this.mapData && (
      this.mapData.markers.some(marker => this.markerMatchesFilter(marker)) ||
      this.hasAssignedRegionBoundary ||
      this.mapData.zones.some(zone => zone.entityType !== 'Region' && this.zoneMatchesFilter(zone))
    ));
  }

  get mapAccessibilitySummary(): string {
    if (this.errorMessage) {
      return `Regional map unavailable. ${this.errorMessage}`;
    }

    if (!this.activeRegionId) {
      return 'No active assigned region is selected.';
    }

    const markerCount = this.mapData?.markers.filter(marker => this.markerMatchesFilter(marker)).length ?? 0;
    const zoneCount = this.mapData?.zones
      .filter(zone => zone.entityType !== 'Region')
      .filter(zone => this.zoneMatchesFilter(zone))
      .length ?? 0;
    const selected = this.selectedEntity
      ? ` Selected entity: ${this.selectedEntity.title}.`
      : this.selectedZone
        ? ` Selected zone: ${this.selectedZone.displayName || this.selectedZone.name}.`
        : this.droppedPin
          ? ` Dropped pin at ${this.droppedPin.latitude.toFixed(4)}, ${this.droppedPin.longitude.toFixed(4)}.`
          : '';

    return `${this.regionDisplayName || 'Active region'} map has ${markerCount} mapped marker${markerCount === 1 ? '' : 's'} and ${zoneCount} editable zone${zoneCount === 1 ? '' : 's'}.${selected}`;
  }

  get assignedRegionBoundary(): AdminMapAssignedRegionBoundary | null {
    return this.mapData?.assignedRegionBoundary ?? null;
  }

  get searchPlaceholder(): string {
    if (this.entityFilter === 'place') return 'Search places or coordinates...';
    if (this.entityFilter === 'location') return 'Search locations or coordinates...';
    if (this.entityFilter === 'destination') return 'Search destinations or coordinates...';
    return 'Search places, locations, destinations, or coordinates...';
  }

  get activeFilterLabel(): string {
    if (this.entityFilter === 'place') return 'Places';
    if (this.entityFilter === 'location') return 'Locations';
    if (this.entityFilter === 'destination') return 'Destinations';
    return 'All';
  }

  get hasAssignedRegionBoundary(): boolean {
    return Boolean(this.assignedRegionBoundary?.hasBoundary && this.assignedRegionBoundary.geometryGeoJson);
  }

  get regionBoundaryNotice(): string | null {
    if (!this.activeRegionId || !this.mapData) return null;

    const boundary = this.assignedRegionBoundary;
    if (!boundary) {
      return 'Assigned region boundary geometry is not available yet.';
    }

    return boundary.hasBoundary
      ? null
      : boundary.message ?? 'Assigned region boundary is missing. Please contact a territory manager or administrator.';
  }

  loadAssignedRegions(): void {
    this.regionLoading = true;
    this.errorMessage = null;
    this.authRequired = false;
    this.notifyStateChanged();

    this.assignedRegionsSubscription?.unsubscribe();
    this.assignedRegionsSubscription = this.dashboardService
      .getAssignedRegions()
      .pipe(
        finalize(() => {
          this.regionLoading = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: regions => {
          this.assignedRegions = regions ?? [];

          if (this.assignedRegions.length === 0) {
            this.activeRegionId = null;
            this.mapData = null;
            this.clearPersistedRegion();
            this.clearMapLayers();
            return;
          }

          const queryRegionId = Number(this.route.snapshot.queryParamMap.get('regionId'));
          const persistedRegionId = this.readPersistedRegionId();
          const selectedRegion =
            this.assignedRegions.find(region => region.id === queryRegionId) ??
            this.assignedRegions.find(region => region.id === persistedRegionId) ??
            this.assignedRegions[0];

          this.setActiveRegion(selectedRegion.id, false);
          this.loadMapData(selectedRegion.id);
        },
        error: error => this.handleLoadError(error, 'Unable to load assigned regions.'),
      });
  }

  onRegionSelected(regionId: number): void {
    if (!this.assignedRegions.some(region => region.id === regionId) || regionId === this.activeRegionId) {
      return;
    }

    this.setActiveRegion(regionId, true);
    this.clearDroppedPin();
    this.clearSelections();
    this.searchQuery = '';
    this.searchResults = [];
    this.searchResultsCache = [];
    this.searchDropdownOpen = false;
    this.loadMapData(regionId);
  }

  onSearchInput(): void {
    if (this.searchHandle) {
      clearTimeout(this.searchHandle);
    }

    const query = this.searchQuery.trim();
    if (query.length < 2 || this.parseCoordinates(query)) {
      this.searchResults = [];
      this.searchResultsCache = [];
      this.searchDropdownOpen = false;
      this.notifyStateChanged();
      return;
    }

    this.searchHandle = setTimeout(() => this.runSearch(false), MapPageComponent.searchDebounceMs);
  }

  onSearchFocus(): void {
    if (this.searchResults.length > 0) {
      this.searchDropdownOpen = true;
      this.notifyStateChanged();
    }
  }

  closeSearchDropdown(): void {
    setTimeout(() => {
      this.searchDropdownOpen = false;
      this.notifyStateChanged();
    }, 150);
  }

  toggleFilterMenu(): void {
    this.filterMenuOpen = !this.filterMenuOpen;
    this.notifyStateChanged();
  }

  closeFilterMenu(): void {
    setTimeout(() => {
      this.filterMenuOpen = false;
      this.notifyStateChanged();
    }, 150);
  }

  doSearch(): void {
    const query = this.searchQuery.trim();
    if (!query || !this.activeRegionId) return;

    const coordinates = this.parseCoordinates(query);
    if (coordinates) {
      this.setDroppedPin(coordinates.latitude, coordinates.longitude, true);
      return;
    }

    if (this.searchResults.length > 0) {
      this.selectSearchResult(this.searchResults[0]);
      return;
    }

    this.runSearch(true);
  }

  clearSearch(): void {
    this.searchQuery = '';
    this.searchResults = [];
    this.searchResultsCache = [];
    this.searchDropdownOpen = false;
    this.closePopup();
  }

  selectSearchResult(result: AdminMapSearchResult): void {
    if (!this.hasValidCoordinates(result.latitude, result.longitude)) return;

    this.searchQuery = result.title;
    this.searchDropdownOpen = false;
    this.centerMap(result.latitude, result.longitude, 15);
    this.activateEntity(this.searchResultToPopupEntity(result));
  }

  cancelDroppedPin(): void {
    this.clearDroppedPin();
    this.notifyStateChanged();
  }

  navigateToNewPlaceWithPin(): void {
    if (!this.droppedPin || !this.hasValidCoordinates(this.droppedPin.latitude, this.droppedPin.longitude)) {
      return;
    }

    if (!this.activeRegionId) {
      this.pinValidationTone = 'error';
      this.pinValidationMessage = 'Select an active region before creating a place from the map.';
      this.notifyStateChanged();
      return;
    }

    const { latitude, longitude } = this.droppedPin;
    this.pinValidationPending = true;
    this.pinValidationMessage = null;
    this.pinValidationTone = null;
    this.notifyStateChanged();

    this.adminMapService.validateEntityLocation({
      regionId: this.activeRegionId,
      entityType: 'Place',
      operationType: 'Create',
      latitude,
      longitude,
    }).pipe(
      finalize(() => {
        this.pinValidationPending = false;
        this.notifyStateChanged();
      }),
    ).subscribe({
      next: result => {
        if (!result.isAllowed) {
          this.pinValidationTone = 'error';
          this.pinValidationMessage = result.message || 'Place cannot be created outside your assigned region.';
          this.notifyStateChanged();
          return;
        }

        const queryParams: Record<string, string | number> = {
          latitude: latitude.toFixed(6),
          longitude: longitude.toFixed(6),
          source: 'map',
          regionId: this.activeRegionId!,
        };

        this.router.navigate(['/places/new'], {
          queryParams,
          state: {
            regionId: this.activeRegionId,
            droppedPin: { latitude, longitude },
            returnTo: '/map',
          },
        });
      },
      error: error => {
        this.pinValidationTone = 'error';
        this.pinValidationMessage = this.extractErrorMessage(
          error,
          'Unable to validate this location against your assigned region.',
        );
        this.notifyStateChanged();
      },
    });
  }

  navigateToNewDestination(): void {
    if (!this.activeRegionId) return;
    this.router.navigate(['/destinations/new'], {
      queryParams: { regionId: this.activeRegionId },
      state: { regionId: this.activeRegionId, returnTo: '/map' },
    });
  }

  navigateToNewLocation(): void {
    if (!this.activeRegionId) return;
    this.router.navigate(['/locations/new'], {
      queryParams: { regionId: this.activeRegionId },
      state: { regionId: this.activeRegionId, returnTo: '/map' },
    });
  }

  closePopup(): void {
    this.clearSelections();
    this.notifyStateChanged();
  }

  onSelectedEntityImageError(): void {
    if (!this.selectedEntity) return;

    this.selectedEntity = {
      ...this.selectedEntity,
      imageUrl: null,
    };
    this.notifyStateChanged();
  }

  openSelectedDetail(): void {
    const route = this.selectedEntity?.detailRoute ?? this.selectedZone?.detailRoute;
    if (route) {
      this.router.navigate([route]);
    }
  }

  editSelected(): void {
    const route = this.selectedEntity?.editRoute ?? this.selectedZone?.editRoute;
    if (route) {
      this.router.navigate([route], {
        queryParams: this.activeRegionId ? { regionId: this.activeRegionId } : undefined,
        state: { regionId: this.activeRegionId, returnTo: '/map' },
      });
    }
  }

  entityTypeColor(type: string): string {
    if (type === 'Location') return 'bg-cyan-100 text-cyan-700 border-cyan-300';
    if (type === 'Destination') return 'bg-purple-100 text-purple-700 border-purple-300';
    if (type === 'Region') return 'bg-emerald-100 text-emerald-700 border-emerald-300';
    return 'bg-blue-100 text-blue-700 border-blue-300';
  }

  entityTypeDot(type: string): string {
    if (type === 'Location') return '#0891b2';
    if (type === 'Destination') return '#7c3aed';
    if (type === 'Region') return '#16a34a';
    return '#2563eb';
  }

  setEntityFilter(filter: MapEntityFilter): void {
    if (this.entityFilter === filter) {
      this.filterMenuOpen = false;
      this.notifyStateChanged();
      return;
    }

    this.entityFilter = filter;
    this.filterMenuOpen = false;
    this.clearSelections();
    this.clearDroppedPin();
    this.renderMapData();
    this.searchResults = this.filterSearchResults(this.searchResultsCache);
    this.searchDropdownOpen = this.searchResults.length > 0;
    this.notifyStateChanged();
  }

  private loadMapData(regionId: number): void {
    const requestId = ++this.mapRequestSequence;
    this.mapLoading = true;
    this.errorMessage = null;
    this.notifyStateChanged();

    this.mapSubscription?.unsubscribe();
    this.mapSubscription = this.adminMapService
      .getMap(regionId, MapPageComponent.defaultRegionTypes)
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
          this.renderMapData();
        },
        error: error => {
          if (requestId !== this.mapRequestSequence) return;
          this.mapData = null;
          this.clearMapLayers();
          this.handleLoadError(error, 'Unable to load map data for the active region.');
        },
      });
  }

  private initMap(): void {
    const element = this.mapContainer?.nativeElement;
    if (!element || this.map || typeof L === 'undefined') {
      return;
    }

    this.map = L.map(element, {
      zoomControl: false,
      attributionControl: false,
    });
    this.map.fitWorld({ animate: false });

    this.mapTileLayer.addTo(L, this.map);

    this.createMapPanes();
    this.regionZoneGroup = L.layerGroup().addTo(this.map);
    this.zoneGroup = L.layerGroup().addTo(this.map);
    this.markerGroup = L.layerGroup().addTo(this.map);
    this.droppedPinGroup = L.layerGroup().addTo(this.map);
    L.control.zoom({ position: 'bottomright' }).addTo(this.map);
    this.configureLeafletZoomControlLabels();

    this.map.on('click', (event: { latlng?: { lat: number; lng: number } }) => {
      if (!event.latlng) return;
      this.zone.run(() => this.setDroppedPin(event.latlng!.lat, event.latlng!.lng, false));
    });

    this.map.on('moveend zoomend', () => {
      this.zone.run(() => {
        this.refreshPopupPositions();
        this.notifyStateChanged();
      });
    });

    if (this.pendingRender || this.mapData) {
      this.renderMapData();
    }
  }

  private createMapPanes(): void {
    this.createPane('admin-region-zone-pane', 350);
    this.createPane('admin-zone-pane', 370);
    this.createPane('admin-marker-pane', 620);
    this.createPane('admin-dropped-pin-pane', 680);
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

  private renderMapData(): void {
    if (!this.map) {
      this.pendingRender = true;
      return;
    }

    this.pendingRender = false;
    this.clearMapLayers();

    const data = this.mapData;
    if (!data) return;

    this.addAssignedRegionBoundary(data.assignedRegionBoundary ?? null);

    const zones = data.zones
      .filter(zone => zone.entityType !== 'Region')
      .filter(zone => this.zoneMatchesFilter(zone))
      .sort((a, b) => this.zoneOrder(a) - this.zoneOrder(b));
    zones.forEach(zone => this.addZone(zone));
    data.markers
      .filter(marker => this.markerMatchesFilter(marker))
      .forEach(marker => this.addMarker(marker));
    this.applyViewport(data);
    this.notifyStateChanged();
  }

  private clearMapLayers(): void {
    this.regionZoneGroup?.clearLayers();
    this.zoneGroup?.clearLayers();
    this.markerGroup?.clearLayers();
    this.clearDroppedPin();
    this.clearSelections();
  }

  private addAssignedRegionBoundary(boundary: AdminMapAssignedRegionBoundary | null): void {
    if (!boundary?.hasBoundary || !boundary.geometryGeoJson || !this.isSupportedGeometry(boundary.geometryGeoJson.type)) {
      return;
    }

    L.geoJSON(boundary.geometryGeoJson, {
      pane: 'admin-region-zone-pane',
      interactive: false,
      style: () => this.assignedRegionBoundaryStyle(),
    }).addTo(this.regionZoneGroup);
  }

  private addZone(zone: AdminMapZone): void {
    if (!zone.geometryGeoJson || !this.isSupportedGeometry(zone.geometryGeoJson.type)) {
      return;
    }

    const layer = L.geoJSON(zone.geometryGeoJson, {
      pane: zone.entityType === 'Region' ? 'admin-region-zone-pane' : 'admin-zone-pane',
      interactive: zone.entityType !== 'Region',
      style: () => this.zoneStyle(zone),
    });

    if (zone.entityType === 'Region') {
      layer.addTo(this.regionZoneGroup);
      return;
    }

    layer.on('click', (event: { latlng?: { lat: number; lng: number }; originalEvent?: Event }) => {
      if (event.originalEvent) {
        L.DomEvent.stopPropagation(event.originalEvent);
      }

      this.zone.run(() => {
        this.selectedZone = zone;
        this.selectedEntity = null;
        this.droppedPin = null;
        this.clearDroppedPinMarker();
        const latitude = event.latlng?.lat ?? zone.centerLatitude ?? null;
        const longitude = event.latlng?.lng ?? zone.centerLongitude ?? null;
        this.popupPosition = latitude !== null && longitude !== null
          ? this.toPopupPosition(latitude, longitude, 28)
          : null;
        this.notifyStateChanged();
      });
    });

    layer.on('mouseover', () => this.setMapCursor('pointer'));
    layer.on('mouseout', () => this.setMapCursor(''));

    layer.addTo(this.zoneGroup);
  }

  private addMarker(marker: AdminMapMarker): void {
    if (!this.hasValidCoordinates(marker.latitude, marker.longitude)) {
      return;
    }

    const layer = L.marker([marker.latitude, marker.longitude], {
      icon: this.makeEntityIcon(marker),
      pane: 'admin-marker-pane',
      riseOnHover: true,
    }).addTo(this.markerGroup);

    layer.on('click', (event: { originalEvent?: Event }) => {
      if (event.originalEvent) {
        L.DomEvent.stopPropagation(event.originalEvent);
      }
      this.zone.run(() => this.activateEntity(this.markerToPopupEntity(marker)));
    });
  }

  private makeEntityIcon(marker: AdminMapMarker): any {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = `gt-admin-marker gt-admin-marker--${marker.entityType.toLowerCase()}`;
    const label = `Open ${marker.entityType.toLowerCase()} marker: ${marker.title}`;
    button.setAttribute('aria-label', label);
    button.title = label;

    const fallback = document.createElement('span');
    fallback.className = 'gt-admin-marker__fallback';
    fallback.textContent = this.markerFallbackText(marker);

    if (marker.imageUrl) {
      const image = document.createElement('img');
      image.alt = '';
      image.draggable = false;
      image.src = marker.imageUrl;
      image.addEventListener('error', () => {
        image.remove();
        fallback.style.display = 'flex';
      });
      button.appendChild(image);
      fallback.style.display = 'none';
    }

    button.appendChild(fallback);

    const size = marker.entityType === 'Place' ? 42 : 34;
    return L.divIcon({
      html: button,
      className: '',
      iconSize: [size, size],
      iconAnchor: [size / 2, size / 2],
    });
  }

  private makeDroppedPinIcon(): any {
    const element = document.createElement('div');
    element.className = 'gt-admin-dropped-pin';
    element.innerHTML = `<svg width="28" height="36" viewBox="0 0 24 32" xmlns="http://www.w3.org/2000/svg" aria-hidden="true">
      <path d="M12 0C5.373 0 0 5.373 0 12c0 8 12 20 12 20S24 20 24 12C24 5.373 18.627 0 12 0z" fill="#f97316" stroke="white" stroke-width="1.5"/>
      <circle cx="12" cy="12" r="4" fill="white"/>
    </svg>`;

    return L.divIcon({
      html: element,
      className: '',
      iconSize: [28, 36],
      iconAnchor: [14, 36],
    });
  }

  private configureLeafletZoomControlLabels(): void {
    const container = this.map?.getContainer?.() as HTMLElement | undefined;
    if (!container) return;

    const zoomIn = container.querySelector<HTMLAnchorElement>('.leaflet-control-zoom-in');
    const zoomOut = container.querySelector<HTMLAnchorElement>('.leaflet-control-zoom-out');

    zoomIn?.setAttribute('aria-label', 'Zoom in');
    zoomIn?.setAttribute('title', 'Zoom in');
    zoomOut?.setAttribute('aria-label', 'Zoom out');
    zoomOut?.setAttribute('title', 'Zoom out');
  }

  activateEntity(entity: PopupEntity): void {
    this.selectedEntity = entity;
    this.selectedZone = null;
    this.droppedPin = null;
    this.clearDroppedPinMarker();
    this.popupPosition = this.toPopupPosition(entity.latitude, entity.longitude, 28);
    this.notifyStateChanged();
  }

  private setDroppedPin(latitude: number, longitude: number, center: boolean): void {
    if (!this.hasValidCoordinates(latitude, longitude) || !this.map) {
      return;
    }

    this.clearSelections();
    this.clearDroppedPinMarker();
    this.pinValidationMessage = null;
    this.pinValidationTone = null;
    this.droppedPin = { latitude, longitude };

    this.droppedPinMarker = L.marker([latitude, longitude], {
      icon: this.makeDroppedPinIcon(),
      pane: 'admin-dropped-pin-pane',
      zIndexOffset: 1000,
    }).addTo(this.droppedPinGroup);

    this.droppedPinPosition = this.toPopupPosition(latitude, longitude, 32);

    if (center) {
      this.centerMap(latitude, longitude, 15);
    }

    this.notifyStateChanged();
  }

  private clearDroppedPin(): void {
    this.droppedPin = null;
    this.droppedPinPosition = null;
    this.clearDroppedPinMarker();
  }

  private clearDroppedPinMarker(): void {
    if (this.droppedPinMarker && this.map) {
      this.map.removeLayer(this.droppedPinMarker);
    }

    this.droppedPinMarker = null;
    this.droppedPinGroup?.clearLayers();
  }

  private clearSelections(): void {
    this.selectedEntity = null;
    this.selectedZone = null;
    this.popupPosition = null;
  }

  private applyViewport(data: AdminMapResponse): void {
    const viewport = data.viewport;
    if (!this.map || viewport.isEmpty) {
      return;
    }

    if (viewport.bounds) {
      this.map.fitBounds(
        [
          [viewport.bounds.south, viewport.bounds.west],
          [viewport.bounds.north, viewport.bounds.east],
        ],
        {
          padding: [60, 60],
          maxZoom: 14,
          animate: false,
        },
      );
      return;
    }

    if (
      viewport.centerLatitude !== null &&
      viewport.centerLatitude !== undefined &&
      viewport.centerLongitude !== null &&
      viewport.centerLongitude !== undefined
    ) {
      this.map.setView(
        [viewport.centerLatitude, viewport.centerLongitude],
        viewport.zoom ?? 12,
        { animate: false },
      );
    }
  }

  private runSearch(selectFirst: boolean): void {
    if (!this.activeRegionId) return;

    const query = this.searchQuery.trim();
    if (query.length < 2) return;

    this.searchLoading = true;
    this.notifyStateChanged();

    this.searchSubscription?.unsubscribe();
    this.searchSubscription = this.adminMapService
      .search(this.activeRegionId, query)
      .pipe(
        finalize(() => {
          this.searchLoading = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: response => {
          this.searchResultsCache = response.items ?? [];
          this.searchResults = this.filterSearchResults(this.searchResultsCache);
          this.searchDropdownOpen = !selectFirst && this.searchResults.length > 0;
          if (selectFirst && this.searchResults.length > 0) {
            this.selectSearchResult(this.searchResults[0]);
          }
        },
        error: error => this.handleLoadError(error, 'Unable to search map entities.'),
      });
  }

  private refreshPopupPositions(): void {
    if (this.selectedEntity) {
      this.popupPosition = this.toPopupPosition(this.selectedEntity.latitude, this.selectedEntity.longitude, 28);
    } else if (this.selectedZone?.centerLatitude != null && this.selectedZone.centerLongitude != null) {
      this.popupPosition = this.toPopupPosition(this.selectedZone.centerLatitude, this.selectedZone.centerLongitude, 28);
    }

    if (this.droppedPin) {
      this.droppedPinPosition = this.toPopupPosition(this.droppedPin.latitude, this.droppedPin.longitude, 32);
    }
  }

  private toPopupPosition(latitude: number, longitude: number, yOffset: number): PopupPosition | null {
    if (!this.map || !this.hasValidCoordinates(latitude, longitude)) {
      return null;
    }

    const point = this.map.latLngToContainerPoint([latitude, longitude]);
    const mapEl = this.mapContainer?.nativeElement.getBoundingClientRect();
    if (!mapEl) return null;

    return {
      x: point.x + mapEl.left,
      y: point.y + mapEl.top - yOffset,
    };
  }

  private centerMap(latitude: number, longitude: number, zoom: number): void {
    if (!this.map) return;
    this.map.setView([latitude, longitude], zoom, { animate: true });
  }

  private zoneStyle(zone: AdminMapZone): Record<string, string | number> {
    if (zone.entityType === 'Region') {
      return this.assignedRegionBoundaryStyle();
    }

    if (zone.entityType === 'Destination') {
      return {
        color: '#7c3aed',
        fillColor: '#a855f7',
        fillOpacity: 0.08,
        weight: 2,
        dashArray: '8 5',
        opacity: 0.75,
      };
    }

    return {
      color: '#0891b2',
      fillColor: '#06b6d4',
      fillOpacity: 0.12,
      weight: 2,
      dashArray: '5 4',
      opacity: 0.8,
    };
  }

  private assignedRegionBoundaryStyle(): Record<string, string | number> {
    return {
      color: '#047857',
      fillColor: '#22c55e',
      fillOpacity: 0.10,
      weight: 3,
      opacity: 0.95,
    };
  }

  private zoneOrder(zone: AdminMapZone): number {
    if (zone.entityType === 'Region') return 0;
    if (zone.entityType === 'Destination') return 1;
    return 2;
  }

  private markerFallbackText(marker: AdminMapMarker): string {
    if (marker.entityType === 'Destination') return 'D';
    if (marker.entityType === 'Location') return 'L';
    return marker.title.trim().charAt(0).toUpperCase() || 'P';
  }


  private markerMatchesFilter(marker: AdminMapMarker): boolean {
    if (this.entityFilter === 'all') return true;
    return marker.entityType.toLowerCase() === this.entityFilter;
  }

  private zoneMatchesFilter(zone: AdminMapZone): boolean {
    if (this.entityFilter === 'all') return true;
    return zone.entityType.toLowerCase() === this.entityFilter;
  }

  private searchResultMatchesFilter(result: AdminMapSearchResult): boolean {
    if (this.entityFilter === 'all') return true;
    return result.entityType.toLowerCase() === this.entityFilter;
  }

  private filterSearchResults(results: AdminMapSearchResult[]): AdminMapSearchResult[] {
    return results.filter(result => this.searchResultMatchesFilter(result));
  }

  private searchResultToPopupEntity(result: AdminMapSearchResult): PopupEntity {
    const id = result.id;
    return {
      id,
      entityType: result.entityType,
      title: result.title,
      subtitle: result.subtitle,
      categoryLabel: result.categoryLabel,
      latitude: result.latitude,
      longitude: result.longitude,
      imageUrl: result.imageUrl,
      detailRoute: result.detailRoute,
      editRoute: id
        ? result.entityType === 'Destination'
          ? `/destinations/edit/${id}`
          : result.entityType === 'Location'
            ? `/locations/edit/${id}`
            : `/places/edit/${id}`
        : null,
    };
  }

  private markerToPopupEntity(marker: AdminMapMarker): PopupEntity {
    return {
      id: marker.id,
      entityType: marker.entityType,
      title: marker.title,
      subtitle: marker.subtitle,
      categoryLabel: marker.categoryLabel,
      latitude: marker.latitude,
      longitude: marker.longitude,
      imageUrl: marker.imageUrl,
      status: marker.status,
      detailRoute: marker.detailRoute,
      editRoute: marker.editRoute,
    };
  }

  private parseCoordinates(value: string): DroppedPin | null {
    const match = value.trim().match(/^(-?\d+(?:\.\d+)?)\s*[, ]\s*(-?\d+(?:\.\d+)?)$/);
    if (!match) return null;

    const latitude = Number(match[1]);
    const longitude = Number(match[2]);
    return this.hasValidCoordinates(latitude, longitude) ? { latitude, longitude } : null;
  }

  private hasValidCoordinates(latitude: number, longitude: number): boolean {
    return Number.isFinite(latitude) &&
      Number.isFinite(longitude) &&
      latitude >= -90 &&
      latitude <= 90 &&
      longitude >= -180 &&
      longitude <= 180 &&
      !(latitude === 0 && longitude === 0);
  }

  private isSupportedGeometry(type: string): boolean {
    return type === 'Polygon' || type === 'MultiPolygon';
  }

  private setActiveRegion(regionId: number, updateUrl: boolean): void {
    this.activeRegionId = regionId;
    this.persistRegion(regionId);

    if (updateUrl) {
      this.router.navigate([], {
        relativeTo: this.route,
        queryParams: { regionId },
        queryParamsHandling: 'merge',
        replaceUrl: true,
      });
    }
  }

  private readPersistedRegionId(): number | null {
    try {
      const value = sessionStorage.getItem(MapPageComponent.activeRegionStorageKey);
      const parsed = value ? Number(value) : NaN;
      return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
    } catch {
      return null;
    }
  }

  private persistRegion(regionId: number): void {
    try {
      sessionStorage.setItem(MapPageComponent.activeRegionStorageKey, String(regionId));
    } catch {
      // Session persistence is a convenience only.
    }
  }

  private clearPersistedRegion(): void {
    try {
      sessionStorage.removeItem(MapPageComponent.activeRegionStorageKey);
    } catch {
      // Session persistence is a convenience only.
    }
  }

  private handleLoadError(error: unknown, fallback: string): void {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 401) {
        this.authRequired = true;
        this.errorMessage = 'Sign in with an authenticated Regional Manager account to view the map.';
      } else if (error.status === 403) {
        this.errorMessage = 'You do not have access to this assigned region.';
      } else {
        this.errorMessage = error.error?.message ?? fallback;
      }
    } else {
      this.errorMessage = fallback;
    }

    this.notifyStateChanged();
  }

  private extractErrorMessage(error: unknown, fallback: string): string {
    if (error instanceof HttpErrorResponse) {
      return error.error?.message ?? fallback;
    }

    return fallback;
  }

  private setMapCursor(cursor: string): void {
    const container = this.map?.getContainer?.() as HTMLElement | undefined;
    if (container) {
      container.style.cursor = cursor;
    }
  }

  private notifyStateChanged(): void {
    this.cdr.markForCheck();
  }
}
