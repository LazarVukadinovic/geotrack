import { AfterViewInit, ChangeDetectorRef, Component, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import maplibregl, {
  GeoJSONSource,
  Map as MapLibreMap,
  type MapMouseEvent,
  MapStyleImageMissingEvent,
} from 'maplibre-gl';
import {
  MapApiService,
  MapEntityMarker,
  MapMarkersMode,
  MapMarkersResponse,
  MapRouteResult,
  MapSearchResult,
  MapViewportBounds,
} from '../../../../core/services/map-api.service';
import { AnalyticsService } from '../../../../core/services/analytics.service';
import { LocationService } from '../../../../core/services/location.service';
import { ImageFallbackDirective } from '../../../../shared/directives/image-fallback.directive';
import { EXPLORE_IMAGE_FALLBACK } from '../../../../core/constants/explore-media.constants';
import type { AnalyticsEntityType } from '../../../../core/services/analytics.types';
import type { ResolvedLocation } from '../../../../core/models/location.model';
import { MapFilterOption, MapFilterStateService } from '../../services/map-filter-state.service';
import { DestinationService } from '../../../../core/services/destination.service';

const MAP_STYLE_URL = '/map-style.json?v=osm-liberty-v1';

const DESTINATION_ZOOM_THRESHOLD = 10;
const VIEWPORT_DEBOUNCE_MS = 250;
const VIEWPORT_CACHE_PADDING = 0.1;
const LOCATION_CACHE_TTL_MS = 60 * 60 * 1000;
const MAP_VIEW_STATE_KEY = 'mapViewState';
const MAP_OPENED_THIS_SESSION_KEY = 'mapOpenedThisSession';
const INITIAL_LOCATION_ZOOM = 13;
const DETAIL_FOCUS_ZOOM = 16;
const MARKER_ACTIVATION_DEDUPE_MS = 250;
const MARKER_DISMISS_GRACE_MS = 250;

interface MarkerEntry {
  data: MapEntityMarker;
  marker: maplibregl.Marker;
  imageElement: HTMLImageElement;
}

interface CachedViewport extends MapViewportBounds {
  mode: MapMarkersMode;
}

interface StoredMapViewState {
  center?: [number, number];
  zoom?: number;
  destinationId?: number | null;
}

interface MapInitialView {
  center: [number, number];
  zoom: number;
}

interface MapNavigationState {
  name?: string;
  lat?: number;
  lon?: number;
  isUserLocation?: boolean;
  fromFilters?: boolean;
  cleared?: boolean;
  mapFocus?: Partial<MapEntityMarker> | null;
  mapPreviewRestore?: boolean;
  fromExplore?: boolean;
}

@Component({
  selector: 'app-map-page',
  imports: [CommonModule, FormsModule, ImageFallbackDirective],
  templateUrl: './map-page.component.html',
  styleUrl: './map-page.component.css',
})
export class MapPageComponent implements AfterViewInit, OnDestroy {
  private readonly USER_SOURCE = 'user-location-source';
  private readonly USER_LAYER = 'user-location-layer';
  private readonly USER_RING = 'user-location-ring';
  private readonly ROUTE_SOURCE = 'route-source';
  private readonly ROUTE_LAYER = 'route-layer';
  private readonly ROUTE_OUTLINE = 'route-layer-outline';

  private map!: MapLibreMap;
  private searchTimeout?: ReturnType<typeof setTimeout>;
  private noDataSearchTimeout?: ReturnType<typeof setTimeout>;
  private filterCategories: string[] = [];
  private selectedCategoryLabelFilters: string[] = [];
  private readonly markersByKey = new Map<string, MarkerEntry>();
  private readonly markerImageFallbackUsed = new Set<string>();
  private rawViewportMarkers: MapEntityMarker[] = [];
  private viewportLoadTimeout?: ReturnType<typeof setTimeout>;
  private requestSeq = 0;
  private routeRequestSeq = 0;
  private lastFetchedViewport: CachedViewport | null = null;
  private isDestroyed = false;
  private readonly initialMapFocus: MapEntityMarker | null;
  private readonly navigatedWithDestination: boolean;
  private preserveSelectedPlaceFromNavigation = false;
  private lastMarkerActivationKey: string | null = null;
  private lastMarkerActivationAt = 0;
  private noDataModalDismissed = false;

  currentMode: MapMarkersMode = 'entities';

  destinationName = 'Search Your Destination';
  destinationLat = 44.0165;
  destinationLon = 21.0059;

  isLoading = false;
  isLocating = false;
  isLoadingRoute = false;
  activeCategory = 'all';
  hasActiveFilters = false;
  isMoreFiltersOpen = false;
  isUserLocation = false;

  searchQuery = '';
  searchResults: MapSearchResult[] = [];
  isSearchFocused = false;
  noDataSearchQuery = '';
  noDataSearchResults: MapSearchResult[] = [];
  allDestinations: MapSearchResult[] = [];
  private searchBlurTimeout?: ReturnType<typeof setTimeout>;

  selectedPlace: MapEntityMarker | null = null;
  emptyStateMessage = '';
  loadErrorMessage = '';
  routeErrorMessage = '';
  routeInfoMessage = '';
  showNoDataModal = false;
  showLegend = false;

  categories = [
    { id: 'all', label: 'All' },
    { id: 'places', label: 'Places' },
    { id: 'stays', label: 'Stays' },
    { id: 'events', label: 'Events' },
  ];

  availableMoreFilters: MapFilterOption[] = [];

  constructor(
    public router: Router,
    private readonly cdr: ChangeDetectorRef,
    private readonly mapApi: MapApiService,
    private readonly analytics: AnalyticsService,
    private readonly locationSvc: LocationService,
    private readonly mapFilterState: MapFilterStateService,
    private readonly destinationSvc: DestinationService,
  ) {
    const nav = this.router.getCurrentNavigation();
    const state = (nav?.extras?.state ?? history.state) as MapNavigationState;
    const savedLocation = this.readSessionJson<{
      name?: string;
      lat?: number;
      lon?: number;
      isUserLocation?: boolean;
    }>('mapLocation');

    if (savedLocation) {
      this.destinationName = savedLocation.name ?? this.destinationName;
      this.destinationLat = savedLocation.lat ?? this.destinationLat;
      this.destinationLon = savedLocation.lon ?? this.destinationLon;
      this.isUserLocation = savedLocation.isUserLocation === true;
    }

    if (state?.name) {
      this.destinationName = state.name;
      this.destinationLat = state.lat ?? this.destinationLat;
      this.destinationLon = state.lon ?? this.destinationLon;
      this.isUserLocation = state.isUserLocation === true;
      sessionStorage.setItem('mapLocation', JSON.stringify({
        name: this.destinationName,
        lat: this.destinationLat,
        lon: this.destinationLon,
        isUserLocation: this.isUserLocation,
      }));
    }

    this.navigatedWithDestination = !!(
      state?.fromExplore &&
      state.name &&
      !state.isUserLocation &&
      typeof state.lat === 'number' &&
      typeof state.lon === 'number'
    );

    if (this.navigatedWithDestination) {
      this.noDataModalDismissed = true;
    }

    this.initialMapFocus = this.readMapFocus(state?.mapFocus);
    if (this.initialMapFocus) {
      this.destinationName = this.initialMapFocus.title;
      this.destinationLat = this.initialMapFocus.latitude;
      this.destinationLon = this.initialMapFocus.longitude;
      this.isUserLocation = false;
      this.selectedPlace = this.initialMapFocus;
      this.preserveSelectedPlaceFromNavigation = true;
    }

    const savedFilters = this.mapFilterState.readSelection();
    this.filterCategories = savedFilters.categories;
    this.selectedCategoryLabelFilters = savedFilters.categoryLabelIds;
    this.syncActiveFilters();

    if (state?.cleared) {
      this.filterCategories = [];
      this.selectedCategoryLabelFilters = [];
      this.hasActiveFilters = false;
      this.mapFilterState.clearSelection();
    }

    // Explore → Map sync: if explore switched to a different destination, re-center the map.
    // Skip when navigating from within the map (filters, preview restore, mapFocus, or explicit fromExplore).
    const isInternalMapNav = !!(state?.fromFilters || state?.mapPreviewRestore);
    if (!isInternalMapNav && !this.navigatedWithDestination && !this.initialMapFocus && this.destinationSvc.isDestinationMode()) {
      const dsLat = this.destinationSvc.latitude();
      const dsLon = this.destinationSvc.longitude();
      const dsName = this.destinationSvc.name();
      if (typeof dsLat === 'number' && typeof dsLon === 'number' && dsName) {
        const coordsAlreadyMatch = savedLocation &&
          Math.abs((savedLocation.lat ?? 0) - dsLat) < 0.001 &&
          Math.abs((savedLocation.lon ?? 0) - dsLon) < 0.001;
        if (!coordsAlreadyMatch) {
          this.destinationName = dsName;
          this.destinationLat = dsLat;
          this.destinationLon = dsLon;
          this.isUserLocation = false;
          this.navigatedWithDestination = true;
        }
        this.noDataModalDismissed = true;
      }
    }
  }

  ngAfterViewInit(): void {
    setTimeout(() => void this.initMap(), 0);
  }

  ngOnDestroy(): void {
    this.isDestroyed = true;
    this.locationSvc.stopMapLocationUpdates();

    if (this.viewportLoadTimeout) {
      clearTimeout(this.viewportLoadTimeout);
    }

    if (this.noDataSearchTimeout) {
      clearTimeout(this.noDataSearchTimeout);
    }

    if (this.searchBlurTimeout) {
      clearTimeout(this.searchBlurTimeout);
    }

    if (this.map) {
      this.persistMapViewState();

      for (const entry of this.markersByKey.values()) {
        entry.marker.remove();
      }
      this.markersByKey.clear();
      this.markerImageFallbackUsed.clear();

      this.map.remove();
    }

    window.removeEventListener('resize', this.resizeHandler);
  }

  get isMoreActive(): boolean {
    return this.hasActiveFilters || this.isMoreFiltersOpen;
  }

  zoomIn(): void {
    this.map?.zoomIn({ duration: 300 });
  }

  zoomOut(): void {
    this.map?.zoomOut({ duration: 300 });
  }

  get filteredDestinations(): MapSearchResult[] {
    const q = this.noDataSearchQuery.trim().toLowerCase();
    if (!q) return this.allDestinations;
    return this.allDestinations.filter(d => d.name.toLowerCase().includes(q) || (d.subtitle ?? '').toLowerCase().includes(q));
  }

  private loadDestinationsIfNeeded(): void {
    if (this.allDestinations.length > 0) return;
    this.mapApi.getDestinations().subscribe({
      next: (results) => {
        this.allDestinations = results;
        this.cdr.detectChanges();
      },
      error: () => {},
    });
  }

  selectNoDataDestination(result: MapSearchResult): void {
    this.noDataSearchQuery = '';
    this.showNoDataModal = false;
    this.selectSearchResult(result);
    this.noDataModalDismissed = true;
  }

  exploreAnyway(): void {
    this.showNoDataModal = false;
    this.noDataModalDismissed = true;
  }

  goBack(): void {
    this.clearMapFocusHistoryState();
    this.router.navigate(['/home']);
  }

  onSearchFocus(): void {
    if (this.searchBlurTimeout) {
      clearTimeout(this.searchBlurTimeout);
      this.searchBlurTimeout = undefined;
    }
    this.isSearchFocused = true;
    this.loadDestinationsIfNeeded();
    this.cdr.detectChanges();
  }

  onSearchBlur(): void {
    this.searchBlurTimeout = setTimeout(() => {
      this.isSearchFocused = false;
      this.cdr.detectChanges();
    }, 200);
  }

  onSearchInput(value: string): void {
    clearTimeout(this.searchTimeout);
    if (value.trim().length < 2) {
      this.searchResults = [];
      return;
    }

    this.searchTimeout = setTimeout(() => {
      this.mapApi.search(value).subscribe({
        next: (results) => {
          this.searchResults = results;
          this.cdr.detectChanges();
        },
        error: () => {
          this.searchResults = [];
          this.cdr.detectChanges();
        },
      });
    }, 150);
  }

  selectSearchResult(result: MapSearchResult): void {
    const committedQuery = this.searchQuery.trim();
    const resultCount = this.searchResults.length;
    const selectedIndex = this.searchResults.findIndex((candidate) =>
      candidate.name === result.name &&
      candidate.latitude === result.latitude &&
      candidate.longitude === result.longitude);
    this.searchResults = [];
    this.noDataSearchResults = [];
    this.isSearchFocused = false;
    this.noDataModalDismissed = true;
    this.searchQuery = result.name;
    this.destinationName = result.name;
    this.destinationLat = result.latitude;
    this.destinationLon = result.longitude;
    this.isUserLocation = false;
    this.preserveSelectedPlaceFromNavigation = false;
    this.clearMapFocusHistoryState();
    this.selectedPlace = null;
    this.routeErrorMessage = ''; this.routeInfoMessage = '';
    this.clearRoute();

    sessionStorage.setItem('mapLocation', JSON.stringify({
      name: result.name,
      lat: result.latitude,
      lon: result.longitude,
      isUserLocation: false,
    }));

    if (result.id && (result.type === 'destination' || !result.type)) {
      this.destinationSvc.setDestinationOverride({
        id: result.id,
        name: result.name,
        latitude: result.latitude,
        longitude: result.longitude,
      });
    }

    this.map.flyTo({ center: [result.longitude, result.latitude], zoom: 14, duration: 900, essential: true });
    this.analytics.track('search_performed', {
      metadata: {
        surface: 'map',
        queryLength: committedQuery.length || result.name.length,
        resultCount,
        selectedIndex: selectedIndex >= 0 ? selectedIndex : null,
      },
    });
    this.invalidateMarkerCache();
    this.scheduleViewportLoad(0);
  }

  locateMe(): void {
    if (this.isLocating) return;
    if (typeof navigator.geolocation?.getCurrentPosition !== 'function') {
      void this.handleLocateFallback();
      return;
    }

    const isSecure = location.protocol === 'https:' || location.hostname === 'localhost';
    if (!isSecure) {
      void this.handleLocateFallback();
      return;
    }

    this.isLocating = true;
    this.cdr.detectChanges();

    navigator.geolocation.getCurrentPosition(
      ({ coords }) => this.handleLocationSuccess(coords.latitude, coords.longitude),
      (err) => {
        if (err.code === GeolocationPositionError.PERMISSION_DENIED) {
          void this.handleLocateFallback();
          return;
        }

        navigator.geolocation.getCurrentPosition(
          ({ coords }) => this.handleLocationSuccess(coords.latitude, coords.longitude),
          () => void this.handleLocateFallback(),
          { timeout: 15_000, maximumAge: 0, enableHighAccuracy: true },
        );
      },
      { timeout: 10_000, maximumAge: 60_000, enableHighAccuracy: false },
    );
  }

  selectCategory(category: { id: string; label: string }): void {
    if (this.activeCategory === category.id && !this.hasActiveFilters) {
      return;
    }

    this.activeCategory = category.id;
    this.filterCategories = [];
    this.selectedCategoryLabelFilters = [];
    this.hasActiveFilters = false;
    this.isMoreFiltersOpen = false;
    this.preserveSelectedPlaceFromNavigation = false;
    this.clearMapFocusHistoryState();
    this.selectedPlace = null;
    this.routeErrorMessage = ''; this.routeInfoMessage = '';
    this.clearRoute();
    this.mapFilterState.clearSelection();

    this.analytics.track('filter_applied', {
      metadata: {
        surface: 'map',
        category: category.id,
      },
    });

    this.invalidateMarkerCache();
    this.scheduleViewportLoad(0);
  }

  onMoreFiltersClick(): void {
    if (!this.isDesktopViewport()) {
      void this.router.navigate(['/filters']);
      return;
    }

    this.isMoreFiltersOpen = !this.isMoreFiltersOpen;
  }

  closeMoreFilters(): void {
    this.isMoreFiltersOpen = false;
  }

  clearMoreFilters(): void {
    const hadServerCategories = this.filterCategories.length > 0;
    const hadFilters = this.hasActiveFilters;

    this.filterCategories = [];
    this.selectedCategoryLabelFilters = [];
    this.syncActiveFilters();
    this.mapFilterState.clearSelection();

    if (hadFilters) {
      this.analytics.track('filter_applied', {
        metadata: {
          surface: 'map_inline_filters',
          categories: [],
          categoryLabels: [],
          cleared: true,
        },
      });
    }

    this.clearSelectionForFilterChange();
    this.refreshMarkersAfterMoreFilterChange(hadServerCategories);
  }

  toggleMoreFilter(filter: MapFilterOption): void {
    this.selectedCategoryLabelFilters = this.mapFilterState.toggleCategoryLabelFilter(
      this.selectedCategoryLabelFilters,
      filter,
    );
    this.syncActiveFilters();
    this.persistMapFilters();

    this.analytics.track('filter_applied', {
      metadata: {
        surface: 'map_inline_filters',
        categories: this.filterCategories,
        categoryLabels: this.selectedMoreFilterLabels(),
      },
    });

    this.clearSelectionForFilterChange();
    this.refreshMarkersAfterMoreFilterChange(false);
  }

  isMoreFilterSelected(filter: MapFilterOption): boolean {
    return this.selectedCategoryLabelFilters.includes(filter.id);
  }

  async openDirections(): Promise<void> {
    if (!this.selectedPlace) return;

    const selectedPlace = this.selectedPlace;
    const selectedKey = this.getMarkerKey(selectedPlace);
    const routeRequestId = ++this.routeRequestSeq;

    this.isLoadingRoute = true;
    this.routeErrorMessage = ''; this.routeInfoMessage = '';
    this.clearRoute();
    this.cdr.detectChanges();

    const origin = await this.locationSvc.resolveRoutingOrigin();
    if (!this.isCurrentRouteRequest(routeRequestId, selectedKey)) {
      if (routeRequestId === this.routeRequestSeq) {
        this.isLoadingRoute = false;
        this.cdr.detectChanges();
      }
      return;
    }

    if (!origin || !this.hasUsableCoordinates(origin.latitude, origin.longitude)) {
      this.isLoadingRoute = false;
      this.routeInfoMessage = '';
      this.routeErrorMessage = 'Location is unavailable, so directions cannot be calculated yet.';
      this.cdr.detectChanges();
      return;
    }

    if (origin.source === 'gps' && this.map.isStyleLoaded()) {
      this.placeUserMarker(origin.latitude, origin.longitude);
    }

    const straightLineKm = this.haversineKm(
      origin.latitude, origin.longitude,
      selectedPlace.latitude, selectedPlace.longitude,
    );
    const useDriving = straightLineKm > 30;

    this.analytics.track('directions_started', {
      entityType: this.toAnalyticsEntityType(selectedPlace),
      entityId: selectedPlace.entityId,
      metadata: {
        surface: 'map',
        provider: 'osrm',
        mode: useDriving ? 'car' : 'foot',
        originSource: origin.source,
      },
    });

    const routeRequest = useDriving
      ? this.mapApi.getDrivingRoute(origin.latitude, origin.longitude, selectedPlace.latitude, selectedPlace.longitude)
      : this.mapApi.getWalkingRoute(origin.latitude, origin.longitude, selectedPlace.latitude, selectedPlace.longitude);

    routeRequest.subscribe({
      next: (route) => {
        if (!this.isCurrentRouteRequest(routeRequestId, selectedKey)) {
          if (routeRequestId === this.routeRequestSeq) {
            this.isLoadingRoute = false;
            this.cdr.detectChanges();
          }
          return;
        }
        this.drawRoute(route);
        this.fitRoute(route);
        this.isLoadingRoute = false;
        this.routeInfoMessage = this.buildRouteLabel(route, useDriving);
        this.cdr.detectChanges();
      },
      error: () => {
        if (!this.isCurrentRouteRequest(routeRequestId, selectedKey)) {
          if (routeRequestId === this.routeRequestSeq) {
            this.isLoadingRoute = false;
            this.cdr.detectChanges();
          }
          return;
        }
        this.isLoadingRoute = false;
        this.routeErrorMessage = useDriving
          ? 'Driving directions are unavailable right now.'
          : 'Walking directions are unavailable right now.';
        this.cdr.detectChanges();
      },
    });
  }

  private haversineKm(lat1: number, lon1: number, lat2: number, lon2: number): number {
    const R = 6371;
    const toRad = (deg: number) => (deg * Math.PI) / 180;
    const dLat = toRad(lat2 - lat1);
    const dLon = toRad(lon2 - lon1);
    const a =
      Math.sin(dLat / 2) ** 2 +
      Math.cos(toRad(lat1)) * Math.cos(toRad(lat2)) * Math.sin(dLon / 2) ** 2;
    return R * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
  }

  private buildRouteLabel(route: MapRouteResult, isDriving: boolean): string {
    const parts: string[] = [];

    const durationSeconds = route.durationSeconds;
    if (typeof durationSeconds === 'number' && durationSeconds > 0) {
      const totalMin = Math.round(durationSeconds / 60);
      let timeLabel: string;
      if (totalMin >= 60) {
        const hours = Math.floor(totalMin / 60);
        const minutes = totalMin % 60;
        timeLabel = minutes > 0 ? `${hours}h ${minutes}min` : `${hours}h`;
      } else {
        timeLabel = `${totalMin} min`;
      }
      parts.push(isDriving ? `${timeLabel} drive` : `${timeLabel} walk`);
    }

    const distanceMeters = route.distanceMeters;
    if (typeof distanceMeters === 'number' && distanceMeters > 0) {
      const km = distanceMeters / 1000;
      const kmLabel = km >= 10
        ? `${Math.round(km)} km`
        : `${km.toFixed(1)} km`;
      parts.push(kmLabel);
    }

    return parts.join(' · ');
  }

  openDetails(): void {
    const selectedPlace = this.selectedPlace;
    if (!selectedPlace?.detailRoute) return;

    this.persistMapFocusHistoryState(selectedPlace);
    void this.router.navigateByUrl(selectedPlace.detailRoute, {
      state: {
        fromMapPreview: true,
        mapFocus: selectedPlace,
      },
    });
  }

  closePlace(): void {
    this.preserveSelectedPlaceFromNavigation = false;
    this.clearMapFocusHistoryState();
    this.selectedPlace = null;
    this.routeErrorMessage = ''; this.routeInfoMessage = '';
    this.clearRoute();
  }

  private readonly resizeHandler = () => this.map?.resize();

  private async initMap(): Promise<void> {
    const container = document.getElementById('map');
    if (!container) return;

    const initialView = await this.resolveInitialMapView();
    if (this.isDestroyed) return;

    this.map = new maplibregl.Map({
      container,
      style: MAP_STYLE_URL,
      center: initialView.center,
      zoom: initialView.zoom,
      attributionControl: false,
      fadeDuration: 0,
      trackResize: true,
    });
    this.markMapOpenedThisSession();

    window.addEventListener('resize', this.resizeHandler);
    this.map.scrollZoom.setWheelZoomRate(1 / 150);
    this.map.touchZoomRotate.enable();
    this.map.touchPitch.enable();

    this.map.on('styleimagemissing', (event: MapStyleImageMissingEvent) => {
      this.map.addImage(event.id, { width: 1, height: 1, data: new Uint8Array([0, 0, 0, 0]) });
    });

    this.map.on('load', () => {
      this.map.resize();
      this.hideExternalPoiLayers();

      const exactLocation = this.getExactUserLocation();
      if (exactLocation) {
        this.placeUserMarker(exactLocation.latitude, exactLocation.longitude);
        this.locationSvc.startMapLocationUpdates((loc) => this.handleMapLocationUpdate(loc));
      }

      this.scheduleViewportLoad(0);
    });

    this.map.on('moveend', () => {
      this.scheduleViewportLoad();
    });

    this.map.on('click', (event: MapMouseEvent) => this.onMapClick(event));
  }


  private hideExternalPoiLayers(): void {
    const layers = this.map.getStyle().layers ?? [];

    const keepLabelKeywords = [
      'road',
      'street',
      'highway',
      'transportation_name',
      'place_city',
      'place_town',
      'place_village',
      'place_hamlet',
      'settlement',
      'country',
      'state',
      'province',
      'boundary',
      'water',
      'waterway',
      'marine',
    ];

    const poiLayerKeywords = [
      'poi',
      'amenity',
      'shop',
      'tourism',
      'food',
      'restaurant',
      'cafe',
      'bar',
      'hotel',
      'museum',
      'attraction',
      'leisure',
      'commercial',
    ];

    for (const layer of layers) {
      if (layer.type !== 'symbol') continue;

      const layerLike = layer as {
        id: string;
        ['source-layer']?: string;
      };

      const id = layerLike.id.toLowerCase();
      const sourceLayer = String(layerLike['source-layer'] ?? '').toLowerCase();
      const searchableName = `${id} ${sourceLayer}`;

      const isImportantBaseLabel = keepLabelKeywords.some(keyword => searchableName.includes(keyword));
      if (isImportantBaseLabel) continue;

      const looksLikePoiLayer = poiLayerKeywords.some(keyword => searchableName.includes(keyword));
      if (!looksLikePoiLayer) continue;

      try {
        this.map.setLayoutProperty(layerLike.id, 'visibility', 'none');
      } catch {
        // Ignore style layers that cannot be modified by MapLibre at runtime.
      }
    }
  }

  private onMapClick(event: MapMouseEvent): void {
    if (this.shouldIgnoreMapBackgroundClick(event)) return;

    if (this.selectedPlace) {
      this.closePlace();
      this.cdr.detectChanges();
    }
  }

  private shouldIgnoreMapBackgroundClick(event: MapMouseEvent): boolean {
    const target = event.originalEvent.target;
    if (target instanceof Element && target.closest('.gt-marker')) {
      return true;
    }

    return Date.now() - this.lastMarkerActivationAt < MARKER_DISMISS_GRACE_MS;
  }

  private async resolveInitialMapView(): Promise<MapInitialView> {
    if (this.initialMapFocus) {
      void this.locationSvc.refreshExactLocationForMap();
      return {
        center: [this.initialMapFocus.longitude, this.initialMapFocus.latitude],
        zoom: DETAIL_FOCUS_ZOOM,
      };
    }

    if (this.navigatedWithDestination) {
      void this.locationSvc.refreshExactLocationForMap();
      return {
        center: [this.destinationLon, this.destinationLat],
        zoom: INITIAL_LOCATION_ZOOM,
      };
    }

    const exactLocation = await this.locationSvc.refreshExactLocationForMap();

    // Re-check destination after awaiting location.
    // Also check whether the stored mapViewState was saved for a different destination
    // (e.g., user browsed Tara, panned around, then went back to Explore and picked Novi Sad).
    if (this.destinationSvc.isDestinationMode()) {
      const dsId = this.destinationSvc.id();
      const dsLat = this.destinationSvc.latitude();
      const dsLon = this.destinationSvc.longitude();
      const dsName = this.destinationSvc.name();
      if (typeof dsLat === 'number' && typeof dsLon === 'number' && dsName) {
        const savedViewState = this.readSessionJson<StoredMapViewState>(MAP_VIEW_STATE_KEY);
        const savedViewDestId = savedViewState?.destinationId ?? null;
        const destinationChanged = dsId != null && savedViewDestId !== dsId;

        const currentSaved = this.readSessionJson<{ lat?: number; lon?: number }>('mapLocation');
        const coordsMatch = currentSaved &&
          Math.abs((currentSaved.lat ?? 0) - dsLat) < 0.001 &&
          Math.abs((currentSaved.lon ?? 0) - dsLon) < 0.001;

        // Re-center if: coords don't match (new destination set after constructor ran)
        // OR if the saved camera view belongs to a different destination.
        if (!coordsMatch || destinationChanged) {
          this.destinationName = dsName;
          this.destinationLat = dsLat;
          this.destinationLon = dsLon;
          this.isUserLocation = false;
          return { center: [dsLon, dsLat], zoom: INITIAL_LOCATION_ZOOM };
        }
      }
    }

    const savedView = this.readSavedMapViewState();
    if (this.hasOpenedMapThisSession() && savedView) {
      return savedView;
    }

    const origin = exactLocation ?? await this.locationSvc.resolveRoutingOrigin();
    if (origin && this.hasUsableCoordinates(origin.latitude, origin.longitude)) {
      return {
        center: [origin.longitude, origin.latitude],
        zoom: INITIAL_LOCATION_ZOOM,
      };
    }

    if (savedView) {
      this.markMapOpenedThisSession();
      return savedView;
    }

    this.loadErrorMessage = "GeoTrack couldn't determine your current location, so the map opened at the last selected destination.";
    this.cdr.detectChanges();

    return {
      center: [this.destinationLon, this.destinationLat],
      zoom: INITIAL_LOCATION_ZOOM,
    };
  }

  private hasOpenedMapThisSession(): boolean {
    try {
      return sessionStorage.getItem(MAP_OPENED_THIS_SESSION_KEY) === 'true';
    } catch {
      return false;
    }
  }

  private markMapOpenedThisSession(): void {
    try {
      sessionStorage.setItem(MAP_OPENED_THIS_SESSION_KEY, 'true');
    } catch {
      // storage may be unavailable; restore simply falls back to location.
    }
  }

  private persistMapViewState(): void {
    const center = this.map.getCenter();
    const zoom = this.map.getZoom();
    try {
      const destinationId = this.destinationSvc.isDestinationMode()
        ? (this.destinationSvc.id() ?? null)
        : null;
      sessionStorage.setItem(MAP_VIEW_STATE_KEY, JSON.stringify({
        center: [center.lng, center.lat],
        zoom,
        destinationId,
      }));
      this.markMapOpenedThisSession();
    } catch {
      // storage may be unavailable; the next map open will resolve location again.
    }
  }

  private readSavedMapViewState(): MapInitialView | null {
    const savedView = this.readSessionJson<StoredMapViewState>(MAP_VIEW_STATE_KEY);
    const center = savedView?.center;
    const zoom = savedView?.zoom;

    if (!center ||
      center.length !== 2 ||
      !this.hasUsableCoordinates(center[1], center[0]) ||
      typeof zoom !== 'number' ||
      !Number.isFinite(zoom)) {
      return null;
    }

    return {
      center,
      zoom: Math.min(Math.max(zoom, 0), 22),
    };
  }

  private readMapFocus(value: Partial<MapEntityMarker> | null | undefined): MapEntityMarker | null {
    if (!value || typeof value !== 'object') return null;

    const entityType = value.entityType;
    if (entityType !== 'place' && entityType !== 'stay' && entityType !== 'event') return null;

    const entityId = typeof value.entityId === 'number' ? value.entityId : NaN;
    const latitude = value.latitude;
    const longitude = value.longitude;
    const title = typeof value.title === 'string' ? value.title.trim() : '';

    if (!Number.isFinite(entityId) ||
      entityId <= 0 ||
      !title ||
      !this.hasUsableCoordinates(latitude, longitude)) {
      return null;
    }

    return {
      entityType,
      entityId,
      title,
      subtitle: typeof value.subtitle === 'string' && value.subtitle.trim() ? value.subtitle : null,
      categoryLabel: typeof value.categoryLabel === 'string' && value.categoryLabel.trim() ? value.categoryLabel : null,
      imageUrl: typeof value.imageUrl === 'string' && value.imageUrl.trim() ? value.imageUrl : null,
      detailRoute: typeof value.detailRoute === 'string' && value.detailRoute.trim() ? value.detailRoute : null,
      latitude: latitude as number,
      longitude: longitude as number,
      distance: typeof value.distance === 'string' && value.distance.trim() ? value.distance : null,
      isSaved: value.isSaved === true,
    };
  }

  private persistMapFocusHistoryState(marker: MapEntityMarker): void {
    const mapFocus = this.readMapFocus(marker);
    if (!mapFocus) return;

    try {
      globalThis.history.replaceState({
        ...(history.state ?? {}),
        mapFocus,
        mapPreviewRestore: true,
      } satisfies MapNavigationState, '', this.router.url);
    } catch {
      // History state may be unavailable in restricted browser modes.
    }
  }

  private clearMapFocusHistoryState(): void {
    try {
      const currentState = history.state as MapNavigationState | null;
      if (!currentState?.mapFocus && !currentState?.mapPreviewRestore) return;

      const nextState = { ...currentState };
      delete nextState.mapFocus;
      delete nextState.mapPreviewRestore;
      globalThis.history.replaceState(nextState, '', this.router.url);
    } catch {
      // If history state cannot be updated, the in-memory selectedPlace still controls this view.
    }
  }

  private scheduleViewportLoad(debounceMs: number = VIEWPORT_DEBOUNCE_MS): void {
    if (!this.map) return;

    if (this.viewportLoadTimeout) {
      clearTimeout(this.viewportLoadTimeout);
      this.viewportLoadTimeout = undefined;
    }

    if (debounceMs <= 0) {
      this.loadMarkersForViewport();
      return;
    }

    this.viewportLoadTimeout = setTimeout(() => {
      this.viewportLoadTimeout = undefined;
      this.loadMarkersForViewport();
    }, debounceMs);
  }

  private loadMarkersForViewport(): void {
    if (!this.map) return;

    const mapBounds = this.map.getBounds();
    const zoom = this.map.getZoom();
    const bounds: MapViewportBounds = {
      north: mapBounds.getNorth(),
      south: mapBounds.getSouth(),
      east: mapBounds.getEast(),
      west: mapBounds.getWest(),
    };

    const expectedMode: MapMarkersMode = zoom < DESTINATION_ZOOM_THRESHOLD ? 'destinations' : 'entities';

    if (this.isWithinCachedViewport(bounds, expectedMode)) {
      return;
    }

    const seq = ++this.requestSeq;
    this.isLoading = true;
    this.loadErrorMessage = '';
    this.cdr.detectChanges();

    this.mapApi.getMarkersInViewport(bounds, zoom, this.getRequestedCategories()).subscribe({
      next: (response) => {
        if (seq !== this.requestSeq) return;
        this.lastFetchedViewport = { ...bounds, mode: response.mode };
        this.applyMarkers(response);
        this.isLoading = false;
        this.cdr.detectChanges();
      },
      error: () => {
        if (seq !== this.requestSeq) return;
        this.isLoading = false;
        this.loadErrorMessage = "GeoTrack couldn't load backend-backed map results right now.";
        this.cdr.detectChanges();
      },
    });
  }

  private isWithinCachedViewport(bounds: MapViewportBounds, mode: MapMarkersMode): boolean {
    const last = this.lastFetchedViewport;
    if (!last || last.mode !== mode) return false;

    const dy = (last.north - last.south) * VIEWPORT_CACHE_PADDING;
    const dx = (last.east - last.west) * VIEWPORT_CACHE_PADDING;

    return bounds.north <= last.north + dy
      && bounds.south >= last.south - dy
      && bounds.east <= last.east + dx
      && bounds.west >= last.west - dx;
  }

  private invalidateMarkerCache(): void {
    this.lastFetchedViewport = null;
  }

  private applyMarkers(response: MapMarkersResponse): void {
    this.rawViewportMarkers = response.markers ?? [];
    this.rebuildAvailableMoreFilters(this.rawViewportMarkers);
    this.renderMarkers(this.filterMarkersByMoreFilters(this.rawViewportMarkers), response.mode);
  }

  private renderMarkers(markers: MapEntityMarker[], mode: MapMarkersMode): void {
    const newKeys = new Set<string>();

    for (const marker of markers) {
      const key = this.getMarkerKey(marker);
      newKeys.add(key);
      const existing = this.markersByKey.get(key);
      if (existing) {
        existing.data = marker;
        existing.marker.setLngLat([marker.longitude, marker.latitude]);
        this.refreshMarkerDom(existing, marker);
      } else {
        const entry = this.createMarkerEntry(marker);
        this.markersByKey.set(key, entry);
      }
    }

    for (const [key, entry] of this.markersByKey) {
      if (!newKeys.has(key)) {
        entry.marker.remove();
        this.markersByKey.delete(key);
        this.markerImageFallbackUsed.delete(key);
      }
    }

    this.currentMode = mode;

    if (this.selectedPlace) {
      const selectedKey = this.getMarkerKey(this.selectedPlace);
      const stillVisible = this.markersByKey.get(selectedKey);
      if (stillVisible) {
        this.selectedPlace = stillVisible.data;
        this.preserveSelectedPlaceFromNavigation = false;
      } else if (!this.shouldPreserveSelectedPlaceDuringMarkerRefresh()) {
        this.selectedPlace = null;
        this.clearRoute();
      }
    }

    if (markers.length > 0) {
      this.emptyStateMessage = '';
      this.showNoDataModal = false;
    } else if (mode === 'destinations') {
      this.emptyStateMessage = 'Pan or zoom in to discover destinations.';
      this.showNoDataModal = false;
    } else if (this.selectedCategoryLabelFilters.length > 0) {
      this.emptyStateMessage = 'No results in this area match those filters.';
      this.showNoDataModal = false;
    } else {
      this.emptyStateMessage = '';
      if (!this.noDataModalDismissed) {
        this.showNoDataModal = true;
        this.loadDestinationsIfNeeded();
      }
    }
  }

  private createMarkerEntry(marker: MapEntityMarker): MarkerEntry {
    const button = document.createElement('button');
    button.type = 'button';

    const img = document.createElement('img');
    img.alt = '';
    img.draggable = false;
    button.appendChild(img);

    const key = this.getMarkerKey(marker);
    img.addEventListener('error', () => {
      if (this.markerImageFallbackUsed.has(key)) return;
      this.markerImageFallbackUsed.add(key);
      img.src = EXPLORE_IMAGE_FALLBACK;
    });

    button.addEventListener('pointerup', (event) => this.onMarkerPointerUp(event, key, marker));
    button.addEventListener('click', (event) => this.onMarkerDomClick(event, key, marker));

    const mapMarker = new maplibregl.Marker({ element: button, anchor: 'center' })
      .setLngLat([marker.longitude, marker.latitude])
      .addTo(this.map);
    mapMarker.on('click', (event) => this.onMapLibreMarkerClick(event as { originalEvent?: MouseEvent }, key, marker));

    const entry: MarkerEntry = { data: marker, marker: mapMarker, imageElement: img };
    this.refreshMarkerDom(entry, marker);
    return entry;
  }

  private refreshMarkerDom(entry: MarkerEntry, marker: MapEntityMarker): void {
    const button = entry.marker.getElement();
    button.classList.add('gt-marker');
    button.classList.remove('gt-marker--place', 'gt-marker--stay', 'gt-marker--event', 'gt-marker--destination');
    button.classList.add(`gt-marker--${marker.entityType}`);

    if (button.getAttribute('aria-label') !== marker.title) {
      button.setAttribute('aria-label', marker.title);
    }

    const key = this.getMarkerKey(marker);
    const desiredSrc = marker.imageUrl ?? EXPLORE_IMAGE_FALLBACK;
    if (entry.imageElement.getAttribute('src') !== desiredSrc) {
      this.markerImageFallbackUsed.delete(key);
      entry.imageElement.src = desiredSrc;
    }
  }

  private onMarkerPointerUp(event: PointerEvent, key: string, marker: MapEntityMarker): void {
    if (event.pointerType !== 'mouse' || event.button !== 0) return;

    event.preventDefault();
    event.stopPropagation();
    this.activateMarker(key, marker);
  }

  private onMarkerDomClick(event: MouseEvent, key: string, marker: MapEntityMarker): void {
    event.preventDefault();
    event.stopPropagation();
    this.activateMarker(key, marker);
  }

  private onMapLibreMarkerClick(event: { originalEvent?: MouseEvent }, key: string, marker: MapEntityMarker): void {
    event.originalEvent?.preventDefault();
    event.originalEvent?.stopPropagation();
    this.activateMarker(key, marker);
  }

  private activateMarker(key: string, marker: MapEntityMarker): void {
    const now = Date.now();
    if (this.lastMarkerActivationKey === key && now - this.lastMarkerActivationAt < MARKER_ACTIVATION_DEDUPE_MS) {
      return;
    }

    this.lastMarkerActivationKey = key;
    this.lastMarkerActivationAt = now;

    const entry = this.markersByKey.get(key);
    this.onMarkerClick(entry?.data ?? marker);
  }

  private onMarkerClick(marker: MapEntityMarker): void {
    this.preserveSelectedPlaceFromNavigation = false;
    this.clearMapFocusHistoryState();

    if (marker.entityType === 'destination') {
      this.selectedPlace = null;
      this.routeErrorMessage = ''; this.routeInfoMessage = '';
      this.searchResults = [];
      this.clearRoute();
      this.noDataModalDismissed = true;

      this.destinationName = marker.title;
      this.destinationLat = marker.latitude;
      this.destinationLon = marker.longitude;
      this.searchQuery = marker.title;
      this.isUserLocation = false;

      sessionStorage.setItem('mapLocation', JSON.stringify({
        name: marker.title,
        lat: marker.latitude,
        lon: marker.longitude,
        isUserLocation: false,
      }));

      this.destinationSvc.setDestinationOverride({
        id: marker.entityId,
        name: marker.title,
        latitude: marker.latitude,
        longitude: marker.longitude,
      });

      this.analytics.track('map_marker_opened', {
        entityType: 'destination',
        entityId: marker.entityId,
        metadata: {
          surface: 'map',
          mode: 'destinations',
        },
      });

      this.invalidateMarkerCache();
      this.map.flyTo({
        center: [marker.longitude, marker.latitude],
        zoom: Math.max(this.map.getZoom() + 1, 13),
        duration: 800,
      });
      this.cdr.detectChanges();
      return;
    }

    this.selectedPlace = marker;
    this.routeErrorMessage = ''; this.routeInfoMessage = '';
    this.clearRoute();
    this.analytics.track('map_marker_opened', {
      entityType: this.toAnalyticsEntityType(marker),
      entityId: marker.entityId,
      metadata: {
        surface: 'map',
        category: marker.categoryLabel ?? marker.entityType,
      },
    });
    this.cdr.detectChanges();
  }

  private getRequestedCategories(): string[] {
    if (this.filterCategories.length > 0) {
      return this.filterCategories;
    }

    return this.activeCategory === 'all' ? [] : [this.activeCategory];
  }

  private refreshMarkersAfterMoreFilterChange(serverCategoriesChanged: boolean): void {
    if (serverCategoriesChanged) {
      this.invalidateMarkerCache();
      this.scheduleViewportLoad(0);
      return;
    }

    this.renderMarkers(this.filterMarkersByMoreFilters(this.rawViewportMarkers), this.currentMode);
    this.cdr.detectChanges();
  }

  private clearSelectionForFilterChange(): void {
    this.preserveSelectedPlaceFromNavigation = false;
    this.clearMapFocusHistoryState();
    this.selectedPlace = null;
    this.routeErrorMessage = ''; this.routeInfoMessage = '';
    this.clearRoute();
  }

  private rebuildAvailableMoreFilters(markers: MapEntityMarker[]): void {
    this.availableMoreFilters = this.mapFilterState.rebuildAvailableMoreFilters(
      markers,
      this.selectedCategoryLabelFilters,
    );
  }

  private filterMarkersByMoreFilters(markers: MapEntityMarker[]): MapEntityMarker[] {
    return this.mapFilterState.filterMarkersByCategoryLabels(markers, this.selectedCategoryLabelFilters);
  }

  private persistMapFilters(): void {
    this.mapFilterState.persistSelection(this.filterCategories, this.selectedCategoryLabelFilters);
  }

  private selectedMoreFilterLabels(): string[] {
    return this.mapFilterState.labelsForCategoryLabelIds(this.selectedCategoryLabelFilters);
  }

  private syncActiveFilters(): void {
    this.hasActiveFilters = this.filterCategories.length > 0 || this.selectedCategoryLabelFilters.length > 0;
  }

  private isDesktopViewport(): boolean {
    return typeof window !== 'undefined' &&
      typeof window.matchMedia === 'function' &&
      window.matchMedia('(min-width: 1024px)').matches;
  }

  private getMarkerKey(marker: MapEntityMarker): string {
    return `${marker.entityType}:${marker.entityId}`;
  }

  private toAnalyticsEntityType(marker: MapEntityMarker): AnalyticsEntityType {
    switch (marker.entityType) {
      case 'stay': return 'hotel';
      case 'event': return 'event';
      case 'destination': return 'destination';
      default: return 'place';
    }
  }

  private shouldPreserveSelectedPlaceDuringMarkerRefresh(): boolean {
    return this.preserveSelectedPlaceFromNavigation || this.isLoadingRoute || this.hasRenderedRoute();
  }

  private hasRenderedRoute(): boolean {
    return !!this.map?.getSource(this.ROUTE_SOURCE);
  }

  private isCurrentRouteRequest(routeRequestId: number, selectedKey: string): boolean {
    return routeRequestId === this.routeRequestSeq &&
      this.selectedPlace !== null &&
      this.getMarkerKey(this.selectedPlace) === selectedKey;
  }

  private getExactUserLocation(): ResolvedLocation | null {
    const loc = this.locationSvc.location();
    const cachedAt = typeof loc?.cachedAt === 'number' ? loc.cachedAt : 0;
    return loc?.source === 'gps' &&
      Date.now() - cachedAt < LOCATION_CACHE_TTL_MS &&
      this.hasUsableCoordinates(loc.latitude, loc.longitude)
      ? loc
      : null;
  }

  private hasUsableCoordinates(latitude: number | null | undefined, longitude: number | null | undefined): boolean {
    return typeof latitude === 'number' &&
      typeof longitude === 'number' &&
      Number.isFinite(latitude) &&
      Number.isFinite(longitude) &&
      latitude >= -90 &&
      latitude <= 90 &&
      longitude >= -180 &&
      longitude <= 180 &&
      (latitude !== 0 || longitude !== 0);
  }

  private drawRoute(route: MapRouteResult): void {
    const coordinates = route.coordinates.map((point) => [point.longitude, point.latitude] as [number, number]);
    if (coordinates.length < 2) {
      this.routeInfoMessage = '';
      this.routeErrorMessage = 'Directions are unavailable right now.';
      return;
    }

    const data: GeoJSON.FeatureCollection = {
      type: 'FeatureCollection',
      features: [{
        type: 'Feature',
        geometry: {
          type: 'LineString',
          coordinates,
        },
        properties: {},
      }],
    };

    if (this.map.getSource(this.ROUTE_SOURCE)) {
      (this.map.getSource(this.ROUTE_SOURCE) as GeoJSONSource).setData(data);
      return;
    }

    this.map.addSource(this.ROUTE_SOURCE, { type: 'geojson', data });
    this.map.addLayer({
      id: this.ROUTE_OUTLINE,
      type: 'line',
      source: this.ROUTE_SOURCE,
      layout: { 'line-join': 'round', 'line-cap': 'round' },
      paint: { 'line-color': '#ffffff', 'line-width': 8, 'line-opacity': 0.85 },
    });
    this.map.addLayer({
      id: this.ROUTE_LAYER,
      type: 'line',
      source: this.ROUTE_SOURCE,
      layout: { 'line-join': 'round', 'line-cap': 'round' },
      paint: { 'line-color': '#e8570a', 'line-width': 5, 'line-opacity': 1 },
    });
  }

  private fitRoute(route: MapRouteResult): void {
    const coordinates = route.coordinates.map((point) => [point.longitude, point.latitude] as [number, number]);
    if (coordinates.length < 2) return;

    const bounds = coordinates.reduce(
      (acc, coord) => acc.extend(coord as maplibregl.LngLatLike),
      new maplibregl.LngLatBounds(coordinates[0], coordinates[0]),
    );

    this.map.fitBounds(bounds, { padding: { top: 100, bottom: 260, left: 80, right: 80 }, duration: 900 });
  }

  clearRoute(): void {
    if (this.map?.getLayer(this.ROUTE_LAYER)) this.map.removeLayer(this.ROUTE_LAYER);
    if (this.map?.getLayer(this.ROUTE_OUTLINE)) this.map.removeLayer(this.ROUTE_OUTLINE);
    if (this.map?.getSource(this.ROUTE_SOURCE)) this.map.removeSource(this.ROUTE_SOURCE);
  }

  private placeUserMarker(lat: number, lon: number): void {
    const data: GeoJSON.FeatureCollection = {
      type: 'FeatureCollection',
      features: [{
        type: 'Feature',
        geometry: { type: 'Point', coordinates: [lon, lat] },
        properties: {},
      }],
    };

    if (this.map.getSource(this.USER_SOURCE)) {
      (this.map.getSource(this.USER_SOURCE) as GeoJSONSource).setData(data);
      return;
    }

    this.map.addSource(this.USER_SOURCE, { type: 'geojson', data });
    this.map.addLayer({
      id: this.USER_RING,
      type: 'circle',
      source: this.USER_SOURCE,
      paint: {
        'circle-radius': 18,
        'circle-color': '#e8570a',
        'circle-opacity': 0.2,
        'circle-stroke-width': 1.5,
        'circle-stroke-color': '#e8570a',
        'circle-stroke-opacity': 0.4,
      },
    });
    this.map.addLayer({
      id: this.USER_LAYER,
      type: 'circle',
      source: this.USER_SOURCE,
      paint: {
        'circle-radius': 9,
        'circle-color': '#e8570a',
        'circle-stroke-width': 3,
        'circle-stroke-color': '#ffffff',
        'circle-opacity': 1,
        'circle-stroke-opacity': 1,
      },
    });
  }

  private handleLocationSuccess(lat: number, lon: number): void {
    this.destinationName = 'My Location';
    this.destinationLat = lat;
    this.destinationLon = lon;
    this.locationSvc.setGpsLocation(lat, lon);
    this.isUserLocation = true;
    this.preserveSelectedPlaceFromNavigation = false;
    this.clearMapFocusHistoryState();
    this.selectedPlace = null;
    this.routeErrorMessage = ''; this.routeInfoMessage = '';
    this.clearRoute();
    this.noDataModalDismissed = false;

    this.map.resize();
    this.map.flyTo({
      center: [lon, lat],
      zoom: 15,
      duration: 1000,
      padding: { top: 120, bottom: 0, left: 0, right: 0 },
    });

    if (this.map.isStyleLoaded()) {
      this.placeUserMarker(lat, lon);
    } else {
      this.map.once('load', () => this.placeUserMarker(lat, lon));
    }
    this.locationSvc.startMapLocationUpdates((loc) => this.handleMapLocationUpdate(loc));

    sessionStorage.setItem('mapLocation', JSON.stringify({
      name: 'My Location',
      lat,
      lon,
      isUserLocation: true,
    }));

    this.isLocating = false;
    this.invalidateMarkerCache();
    this.cdr.detectChanges();
  }

  private async handleLocateFallback(): Promise<void> {
    this.isLocating = true;
    this.cdr.detectChanges();

    const loc = await this.locationSvc.ensureIpFallback();
    if (this.isDestroyed) return;

    this.isLocating = false;

    if (!loc || !this.hasUsableCoordinates(loc.latitude, loc.longitude)) {
      this.loadErrorMessage = "GeoTrack couldn't determine your current location.";
      this.cdr.detectChanges();
      return;
    }

    if (loc.source === 'gps') {
      this.handleLocationSuccess(loc.latitude, loc.longitude);
      return;
    }

    this.destinationName = loc.displayLabel || 'Approximate Location';
    this.destinationLat = loc.latitude;
    this.destinationLon = loc.longitude;
    this.isUserLocation = false;
    this.loadErrorMessage = '';
    this.preserveSelectedPlaceFromNavigation = false;
    this.clearMapFocusHistoryState();
    this.selectedPlace = null;
    this.routeErrorMessage = ''; this.routeInfoMessage = '';
    this.clearRoute();

    this.map.resize();
    this.map.flyTo({
      center: [loc.longitude, loc.latitude],
      zoom: INITIAL_LOCATION_ZOOM,
      duration: 1000,
      padding: { top: 120, bottom: 0, left: 0, right: 0 },
    });

    sessionStorage.setItem('mapLocation', JSON.stringify({
      name: this.destinationName,
      lat: loc.latitude,
      lon: loc.longitude,
      isUserLocation: false,
    }));

    this.invalidateMarkerCache();
    this.scheduleViewportLoad(0);
    this.cdr.detectChanges();
  }

  private handleMapLocationUpdate(loc: ResolvedLocation): void {
    if (!this.map || this.isDestroyed) return;

    if (this.map.isStyleLoaded()) {
      this.placeUserMarker(loc.latitude, loc.longitude);
    }

    if (this.isUserLocation) {
      this.destinationLat = loc.latitude;
      this.destinationLon = loc.longitude;
      sessionStorage.setItem('mapLocation', JSON.stringify({
        name: 'My Location',
        lat: loc.latitude,
        lon: loc.longitude,
        isUserLocation: true,
      }));
    }
  }

  private readSessionJson<T>(key: string): T | null {
    try {
      const raw = sessionStorage.getItem(key);
      return raw ? JSON.parse(raw) as T : null;
    } catch {
      sessionStorage.removeItem(key);
      return null;
    }
  }
}
