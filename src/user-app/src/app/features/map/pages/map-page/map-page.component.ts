import { AfterViewInit, ChangeDetectorRef, Component, NgZone, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';
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
const PRIMARY_PLACE_TYPE_COUNT = 4;
const PRIMARY_PLACE_TYPE_LABELS = ['stays', 'restaurant', 'cafe', 'landmark'];

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
  private searchSubscription?: Subscription;
  private searchRequestSeq = 0;
  private noDataSearchTimeout?: ReturnType<typeof setTimeout>;
  private filterCategories: string[] = [];
  private selectedCategoryLabelFilters: string[] = [];
  private readonly markersByKey = new Map<string, MarkerEntry>();
  private readonly stackMarkersByKey = new Map<string, maplibregl.Marker>();
  private readonly stackGroupByElement = new WeakMap<HTMLElement, MapEntityMarker[]>();
  private readonly markerImageFallbackUsed = new Set<string>();
  private rawViewportMarkers: MapEntityMarker[] = [];
  private viewportLoadTimeout?: ReturnType<typeof setTimeout>;
  private requestSeq = 0;
  private routeRequestSeq = 0;
  private activeRouteDestinationKey: string | null = null;
  private activeRouteMode: 'walking' | 'driving' | null = null;
  private activeRouteOrigin: { latitude: number; longitude: number } | null = null;
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
  isSearching = false;
  searchErrorMessage = '';
  isSearchFocused = false;
  noDataSearchQuery = '';
  noDataSearchResults: MapSearchResult[] = [];
  allDestinations: MapSearchResult[] = [];
  isLoadingDestinations = false;
  destinationLoadError = '';
  private searchBlurTimeout?: ReturnType<typeof setTimeout>;

  selectedPlace: MapEntityMarker | null = null;
  focusedDestination: MapEntityMarker | null = null;
  stackedPlacePicker: MapEntityMarker[] | null = null;
  emptyStateMessage = '';
  loadErrorMessage = '';
  routeErrorMessage = '';
  routeInfoMessage = '';
  showNoDataModal = false;
  showLegend = false;
  isPinToPinMode = false;
  pinToPinOrigin: MapEntityMarker | null = null;

  categories: Array<{ id: string; label: string; count?: number }> = [{ id: 'all', label: 'All' }];

  availableMoreFilters: MapFilterOption[] = [];

  constructor(
    public router: Router,
    private readonly cdr: ChangeDetectorRef,
    private readonly ngZone: NgZone,
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
      sessionStorage.setItem(
        'mapLocation',
        JSON.stringify({
          name: this.destinationName,
          lat: this.destinationLat,
          lon: this.destinationLon,
          isUserLocation: this.isUserLocation,
        }),
      );
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
    this.activeCategory = this.filterCategories[0] ?? 'all';
    this.selectedCategoryLabelFilters = savedFilters.categoryLabelIds;
    this.syncActiveFilters();

    if (state?.cleared) {
      this.filterCategories = [];
      this.activeCategory = 'all';
      this.selectedCategoryLabelFilters = [];
      this.hasActiveFilters = false;
      this.mapFilterState.clearSelection();
    }

    const isInternalMapNav = !!(state?.fromFilters || state?.mapPreviewRestore);
    if (
      !isInternalMapNav &&
      !this.navigatedWithDestination &&
      !this.initialMapFocus &&
      this.destinationSvc.isDestinationMode()
    ) {
      const dsLat = this.destinationSvc.latitude();
      const dsLon = this.destinationSvc.longitude();
      const dsName = this.destinationSvc.name();
      if (typeof dsLat === 'number' && typeof dsLon === 'number' && dsName) {
        const coordsAlreadyMatch =
          savedLocation &&
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

    this.focusedDestination = this.destinationMarkerFromCurrentMode();
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

    if (this.searchTimeout) {
      clearTimeout(this.searchTimeout);
    }

    this.cancelActiveSearch();

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

      for (const marker of this.stackMarkersByKey.values()) {
        marker.remove();
      }
      this.stackMarkersByKey.clear();

      this.map.remove();
    }

    window.removeEventListener('resize', this.resizeHandler);
  }

  get primaryCategories(): Array<{ id: string; label: string; count?: number }> {
    const all = this.categories.find((category) => category.id === 'all') ?? { id: 'all', label: 'All' };
    const available = this.categories.filter((category) => category.id !== 'all');
    const selectedIds = new Set<string>();
    const preferred = PRIMARY_PLACE_TYPE_LABELS
      .map((label) => available.find((category) => {
        const normalizedLabel = category.label.trim().toLowerCase();
        return (normalizedLabel === 'stay' ? 'stays' : normalizedLabel) === label;
      }))
      .filter((category): category is { id: string; label: string; count?: number } => !!category)
      .filter((category) => {
        if (selectedIds.has(category.id)) return false;
        selectedIds.add(category.id);
        return true;
      });
    const fallback = available
      .filter((category) => !selectedIds.has(category.id))
      .slice(0, Math.max(0, PRIMARY_PLACE_TYPE_COUNT - preferred.length));

    return [all, ...preferred, ...fallback];
  }

  get morePlaceTypeCategories(): Array<{ id: string; label: string; count?: number }> {
    return this.categories.filter((category) => category.id !== 'all');
  }

  get isMoreActive(): boolean {
    const activeTypeIsInMore =
      this.activeCategory !== 'all' &&
      !this.primaryCategories.some((category) => category.id === this.activeCategory);
    return (
      activeTypeIsInMore ||
      this.selectedCategoryLabelFilters.length > 0 ||
      this.isMoreFiltersOpen
    );
  }

  get hasRecommendedTagFilters(): boolean {
    return this.selectedCategoryLabelFilters.length > 0;
  }

  get showDestinationPreview(): boolean {
    return !!(
      this.focusedDestination &&
      !this.selectedPlace &&
      !this.stackedPlacePicker &&
      !this.isPinToPinMode &&
      this.currentMode === 'entities'
    );
  }

  get hasActiveDirectionsForSelectedPlace(): boolean {
    return !!(
      this.selectedPlace &&
      this.activeRouteDestinationKey === this.getMarkerKey(this.selectedPlace) &&
      this.hasRenderedRoute()
    );
  }

  get mapAccessibilitySummary(): string {
    const selected = this.selectedPlace ? ` Selected marker: ${this.selectedPlace.title}.` : '';
    const state = this.loadErrorMessage
      ? ` Map results unavailable: ${this.loadErrorMessage}`
      : this.emptyStateMessage
        ? ` ${this.emptyStateMessage}`
        : '';

    return `Showing ${this.markersByKey.size} map result${this.markersByKey.size === 1 ? '' : 's'} near ${this.destinationName}.${selected}${state}`;
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
    return this.allDestinations.filter(
      (d) => d.name.toLowerCase().includes(q) || (d.subtitle ?? '').toLowerCase().includes(q),
    );
  }

  get noDataHeading(): string {
    if (
      !this.isLoadingDestinations &&
      !this.destinationLoadError &&
      this.allDestinations.length === 0
    ) {
      return 'GeoTrack does not have public destinations available yet.';
    }

    return "GeoTrack doesn't have content for this area yet.";
  }

  get noDataBody(): string {
    if (
      !this.isLoadingDestinations &&
      !this.destinationLoadError &&
      this.allDestinations.length === 0
    ) {
      return 'Try again later after public places have been published.';
    }

    return 'Try searching for another destination where GeoTrack has places mapped.';
  }

  private loadDestinationsIfNeeded(force = false): void {
    if (!force && (this.allDestinations.length > 0 || this.isLoadingDestinations)) return;
    this.isLoadingDestinations = true;
    this.destinationLoadError = '';
    this.mapApi.getDestinations().subscribe({
      next: (results) => {
        this.allDestinations = results;
        this.isLoadingDestinations = false;
        this.destinationLoadError = '';
        this.cdr.detectChanges();
      },
      error: () => {
        this.isLoadingDestinations = false;
        this.destinationLoadError = 'Destinations are unavailable right now.';
        this.cdr.detectChanges();
      },
    });
  }

  retryLoadDestinations(): void {
    this.loadDestinationsIfNeeded(true);
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
    this.searchQuery = value;
    this.isSearchFocused = true;
    if (this.searchTimeout) {
      clearTimeout(this.searchTimeout);
      this.searchTimeout = undefined;
    }
    this.cancelActiveSearch();

    const trimmedQuery = value.trim();
    this.searchErrorMessage = '';

    if (trimmedQuery.length < 2) {
      this.isSearching = false;
      this.searchResults = [];
      this.cdr.detectChanges();
      return;
    }

    this.isSearching = true;
    this.searchResults = [];
    const requestId = this.searchRequestSeq;

    this.searchTimeout = setTimeout(() => {
      this.searchTimeout = undefined;
      this.searchSubscription = this.mapApi.search(trimmedQuery).subscribe({
        next: (results) => {
          if (requestId !== this.searchRequestSeq || this.searchQuery.trim() !== trimmedQuery)
            return;
          this.searchResults = results;
          this.isSearching = false;
          this.searchErrorMessage = '';
          this.cdr.detectChanges();
        },
        error: () => {
          if (requestId !== this.searchRequestSeq || this.searchQuery.trim() !== trimmedQuery)
            return;
          this.searchResults = [];
          this.isSearching = false;
          this.searchErrorMessage = 'Search is unavailable right now. Please try again.';
          this.cdr.detectChanges();
        },
      });
    }, 150);

    this.cdr.detectChanges();
  }

  selectSearchResult(result: MapSearchResult): void {
    if (this.searchTimeout) {
      clearTimeout(this.searchTimeout);
      this.searchTimeout = undefined;
    }
    this.cancelActiveSearch();
    this.isSearching = false;
    this.searchErrorMessage = '';

    const committedQuery = this.searchQuery.trim();
    const resultCount = this.searchResults.length;
    const selectedIndex = this.searchResults.findIndex(
      (candidate) =>
        candidate.name === result.name &&
        candidate.latitude === result.latitude &&
        candidate.longitude === result.longitude,
    );
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
    this.focusedDestination = null;
    this.routeErrorMessage = '';
    this.routeInfoMessage = '';
    this.clearRoute();

    sessionStorage.setItem(
      'mapLocation',
      JSON.stringify({
        name: result.name,
        lat: result.latitude,
        lon: result.longitude,
        isUserLocation: false,
      }),
    );

    if (result.id && (result.type === 'destination' || !result.type)) {
      this.destinationSvc.setDestinationOverride({
        id: result.id,
        name: result.name,
        latitude: result.latitude,
        longitude: result.longitude,
      });
    }

    const zoom = this.zoomForSearchResult(result);
    const focusMarker = this.toMarkerFromSearchResult(result);
    if (focusMarker) {
      this.selectedPlace = focusMarker;
    }

    if (result.type === 'destination' && result.id) {
      this.focusedDestination = this.toDestinationMarkerFromSearchResult(result);
    }

    this.writeSearchResultMapViewState(result, zoom);

    const flyOptions: Parameters<MapLibreMap['flyTo']>[0] = {
      center: [result.longitude, result.latitude],
      zoom,
      duration: 900,
      essential: true,
    };

    if (focusMarker) {
      flyOptions.padding = { top: 80, bottom: 180, left: 0, right: 0 };
    }

    this.map.flyTo(flyOptions);
    this.analytics.track('search_performed', {
      metadata: {
        surface: 'map',
        queryLength: committedQuery.length || result.name.length,
        resultCount,
        selectedIndex: selectedIndex >= 0 ? selectedIndex : null,
      },
    });
    this.invalidateMarkerCache();
    this.cdr.detectChanges();
  }

  private cancelActiveSearch(): void {
    this.searchRequestSeq += 1;
    this.searchSubscription?.unsubscribe();
    this.searchSubscription = undefined;
  }

  private toMarkerFromSearchResult(result: MapSearchResult): MapEntityMarker | null {
    const entityType = this.entityTypeFromSearchResult(result);
    if (!entityType || !result.id) {
      return null;
    }

    return {
      entityType,
      entityId: result.id,
      title: result.name,
      subtitle: result.subtitle ?? result.parentName ?? null,
      categoryLabel: result.type === 'stay' ? 'Stay' : entityType === 'event' ? 'Event' : 'Place',
      imageUrl: null,
      detailRoute: this.detailRouteForSearchResult(result, entityType),
      latitude: result.latitude,
      longitude: result.longitude,
      isSaved: false,
    };
  }

  private toDestinationMarkerFromSearchResult(result: MapSearchResult): MapEntityMarker {
    return {
      entityType: 'destination',
      entityId: result.id ?? 0,
      title: result.name,
      subtitle: result.subtitle ?? result.parentName ?? null,
      categoryLabel: 'Destination',
      imageUrl: null,
      detailRoute: result.id ? `/destinations/${result.id}` : null,
      latitude: result.latitude,
      longitude: result.longitude,
      isSaved: false,
    };
  }

  private destinationMarkerFromCurrentMode(): MapEntityMarker | null {
    if (!this.destinationSvc.isDestinationMode()) return null;

    const id = this.destinationSvc.id();
    const name = this.destinationSvc.name();
    const latitude = this.destinationSvc.latitude();
    const longitude = this.destinationSvc.longitude();
    if (
      typeof id !== 'number' ||
      id <= 0 ||
      !name ||
      !this.hasUsableCoordinates(latitude, longitude)
    ) {
      return null;
    }

    return {
      entityType: 'destination',
      entityId: id,
      title: name,
      subtitle: 'Destination',
      categoryLabel: 'Destination',
      imageUrl: null,
      detailRoute: `/destinations/${id}`,
      latitude: latitude as number,
      longitude: longitude as number,
      isSaved: false,
    };
  }

  private entityTypeFromSearchResult(
    result: MapSearchResult,
  ): MapEntityMarker['entityType'] | null {
    switch (result.type) {
      case 'place':
        return 'place';
      case 'stay':
        return 'stay';
      case 'event':
        return 'event';
      default:
        return null;
    }
  }

  private detailRouteForSearchResult(
    result: MapSearchResult,
    entityType: MapEntityMarker['entityType'],
  ): string {
    if (!result.id) return '';
    if (entityType === 'stay') return `/stays/${result.id}`;
    if (entityType === 'event') return `/events/${result.id}`;
    return `/places/${result.id}`;
  }

  private zoomForSearchResult(result: MapSearchResult): number {
    if (result.type === 'place' || result.type === 'stay' || result.type === 'event') {
      return 17;
    }

    if (result.type === 'location') {
      return 15;
    }

    return 14;
  }

  protected searchResultTrackKey(result: MapSearchResult, index: number): string {
    const typedId = result.type && result.id ? `${result.type}:${result.id}` : null;
    if (typedId) return typedId;

    return [result.type ?? 'unknown', result.name, result.latitude, result.longitude, index].join(
      ':',
    );
  }

  private writeSearchResultMapViewState(result: MapSearchResult, zoom: number): void {
    try {
      sessionStorage.setItem(
        MAP_VIEW_STATE_KEY,
        JSON.stringify({
          center: [result.longitude, result.latitude],
          zoom,
          destinationId:
            result.id && (result.type === 'destination' || !result.type) ? result.id : null,
        }),
      );
      sessionStorage.setItem(MAP_OPENED_THIS_SESSION_KEY, 'true');
    } catch {}
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
      ({ coords }) => this.scheduleLocationSuccess(coords.latitude, coords.longitude),
      (err) => {
        if (err.code === GeolocationPositionError.PERMISSION_DENIED) {
          this.ngZone.run(() => void this.handleLocateFallback());
          return;
        }

        navigator.geolocation.getCurrentPosition(
          ({ coords }) => this.scheduleLocationSuccess(coords.latitude, coords.longitude),
          () => this.ngZone.run(() => void this.handleLocateFallback()),
          { timeout: 15_000, maximumAge: 0, enableHighAccuracy: true },
        );
      },
      { timeout: 10_000, maximumAge: 60_000, enableHighAccuracy: false },
    );
  }

  selectCategory(category: { id: string; label: string }): void {
    const sameCategory = this.activeCategory === category.id;
    const hadMoreFilters = this.selectedCategoryLabelFilters.length > 0;
    if (sameCategory && !hadMoreFilters) return;

    this.activeCategory = category.id;
    this.filterCategories = category.id === 'all' ? [] : [category.id];
    // A primary place-type choice replaces any tag combination selected in More.
    this.selectedCategoryLabelFilters = [];
    this.isMoreFiltersOpen = false;
    this.syncActiveFilters();
    this.persistMapFilters();
    this.clearSelectionForFilterChange();

    this.analytics.track('filter_applied', {
      metadata: {
        surface: 'map',
        placeType: category.id === 'all' ? null : category.label,
        recommendedTags: [],
        replacedMoreFilters: hadMoreFilters,
      },
    });

    this.renderFilteredMarkers();
  }

  selectCategoryFromMore(category: { id: string; label: string }): void {
    this.selectCategory(category);
  }

  onMoreFiltersClick(): void {
    this.isMoreFiltersOpen = !this.isMoreFiltersOpen;
  }

  closeMoreFilters(): void {
    this.isMoreFiltersOpen = false;
  }

  clearMoreFilters(): void {
    const hadTagFilters = this.selectedCategoryLabelFilters.length > 0;
    this.selectedCategoryLabelFilters = [];
    this.syncActiveFilters();
    this.persistMapFilters();

    if (hadTagFilters) {
      this.analytics.track('filter_applied', {
        metadata: {
          surface: 'map_inline_filters',
          placeType: this.activeCategory === 'all' ? null : this.activeCategory,
          recommendedTags: [],
          cleared: true,
        },
      });
    }

    this.clearSelectionForFilterChange();
    this.renderFilteredMarkers();
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
        placeType: this.activeCategory === 'all' ? null : this.activeCategory,
        recommendedTags: this.selectedMoreFilterLabels(),
      },
    });

    this.clearSelectionForFilterChange();
    this.renderFilteredMarkers();
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
    this.routeErrorMessage = '';
    this.routeInfoMessage = '';
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
      origin.latitude,
      origin.longitude,
      selectedPlace.latitude,
      selectedPlace.longitude,
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
      ? this.mapApi.getDrivingRoute(
          origin.latitude,
          origin.longitude,
          selectedPlace.latitude,
          selectedPlace.longitude,
        )
      : this.mapApi.getWalkingRoute(
          origin.latitude,
          origin.longitude,
          selectedPlace.latitude,
          selectedPlace.longitude,
        );

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
        this.fitRoute(route, origin, selectedPlace);
        if (route.coordinates.length >= 2) {
          this.activeRouteDestinationKey = selectedKey;
          this.activeRouteMode = useDriving ? 'driving' : 'walking';
          this.activeRouteOrigin = { latitude: origin.latitude, longitude: origin.longitude };
        }
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

  openDetailedDirections(): void {
    const place = this.selectedPlace;
    if (!place || !this.hasActiveDirectionsForSelectedPlace) return;

    const params = new URLSearchParams({
      api: '1',
      destination: `${place.latitude},${place.longitude}`,
      travelmode: this.activeRouteMode ?? 'walking',
    });

    if (this.activeRouteOrigin) {
      params.set(
        'origin',
        `${this.activeRouteOrigin.latitude},${this.activeRouteOrigin.longitude}`,
      );
    }

    globalThis.open(
      `https://www.google.com/maps/dir/?${params.toString()}`,
      '_blank',
      'noopener,noreferrer',
    );
  }

  openDestinationDetails(): void {
    const destination = this.focusedDestination;
    if (!destination?.detailRoute) return;
    void this.router.navigateByUrl(destination.detailRoute);
  }

  closeDestinationPreview(): void {
    this.focusedDestination = null;
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
      const kmLabel = km >= 10 ? `${Math.round(km)} km` : `${km.toFixed(1)} km`;
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
    this.routeErrorMessage = '';
    this.routeInfoMessage = '';
    this.isPinToPinMode = false;
    this.pinToPinOrigin = null;
    this.clearRoute();
  }

  startPinToPinRoute(): void {
    if (!this.selectedPlace) return;
    this.pinToPinOrigin = this.selectedPlace;
    this.isPinToPinMode = true;
    this.selectedPlace = null;
    this.routeErrorMessage = '';
    this.routeInfoMessage = '';
    this.clearRoute();
    this.cdr.detectChanges();
  }

  cancelPinToPinMode(): void {
    this.isPinToPinMode = false;
    this.selectedPlace = this.pinToPinOrigin;
    this.pinToPinOrigin = null;
    this.cdr.detectChanges();
  }

  async openPinToPinDirections(destination: MapEntityMarker): Promise<void> {
    const origin = this.pinToPinOrigin;
    if (!origin) return;

    this.isPinToPinMode = false;
    this.pinToPinOrigin = null;
    this.selectedPlace = destination;

    const routeRequestId = ++this.routeRequestSeq;
    const selectedKey = this.getMarkerKey(destination);

    this.isLoadingRoute = true;
    this.routeErrorMessage = '';
    this.routeInfoMessage = '';
    this.clearRoute();
    this.cdr.detectChanges();

    const straightLineKm = this.haversineKm(
      origin.latitude,
      origin.longitude,
      destination.latitude,
      destination.longitude,
    );
    const useDriving = straightLineKm > 30;

    this.analytics.track('directions_started', {
      entityType: this.toAnalyticsEntityType(destination),
      entityId: destination.entityId,
      metadata: {
        surface: 'map',
        provider: 'osrm',
        mode: useDriving ? 'car' : 'foot',
        originSource: 'pin',
      },
    });

    const routeRequest = useDriving
      ? this.mapApi.getDrivingRoute(
          origin.latitude,
          origin.longitude,
          destination.latitude,
          destination.longitude,
        )
      : this.mapApi.getWalkingRoute(
          origin.latitude,
          origin.longitude,
          destination.latitude,
          destination.longitude,
        );

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
        this.fitRoute(route, origin, destination);
        if (route.coordinates.length >= 2) {
          this.activeRouteDestinationKey = selectedKey;
          this.activeRouteMode = useDriving ? 'driving' : 'walking';
          this.activeRouteOrigin = { latitude: origin.latitude, longitude: origin.longitude };
        }
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
      renderWorldCopies: false,
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

      const isImportantBaseLabel = keepLabelKeywords.some((keyword) =>
        searchableName.includes(keyword),
      );
      if (isImportantBaseLabel) continue;

      const looksLikePoiLayer = poiLayerKeywords.some((keyword) =>
        searchableName.includes(keyword),
      );
      if (!looksLikePoiLayer) continue;

      try {
        this.map.setLayoutProperty(layerLike.id, 'visibility', 'none');
      } catch {}
    }
  }

  private onMapClick(event: MapMouseEvent): void {
    if (this.shouldIgnoreMapBackgroundClick(event)) return;

    if (this.selectedPlace) {
      this.closePlace();
      this.cdr.detectChanges();
    } else if (this.stackedPlacePicker) {
      this.stackedPlacePicker = null;
      this.cdr.detectChanges();
    }
  }

  private shouldIgnoreMapBackgroundClick(event: MapMouseEvent): boolean {
    const target = event.originalEvent.target;
    if (
      target instanceof Element &&
      (target.closest('.gt-marker') || target.closest('.gt-marker-stack'))
    ) {
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
        const coordsMatch =
          currentSaved &&
          Math.abs((currentSaved.lat ?? 0) - dsLat) < 0.001 &&
          Math.abs((currentSaved.lon ?? 0) - dsLon) < 0.001;

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

    const origin = exactLocation ?? (await this.locationSvc.resolveRoutingOrigin());
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

    this.loadErrorMessage =
      "GeoTrack couldn't determine your current location, so the map opened at the last selected destination.";
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
    } catch {}
  }

  private persistMapViewState(): void {
    const center = this.map.getCenter();
    const zoom = this.map.getZoom();
    try {
      const destinationId = this.destinationSvc.isDestinationMode()
        ? (this.destinationSvc.id() ?? null)
        : null;
      sessionStorage.setItem(
        MAP_VIEW_STATE_KEY,
        JSON.stringify({
          center: [center.lng, center.lat],
          zoom,
          destinationId,
        }),
      );
      this.markMapOpenedThisSession();
    } catch {}
  }

  private readSavedMapViewState(): MapInitialView | null {
    const savedView = this.readSessionJson<StoredMapViewState>(MAP_VIEW_STATE_KEY);
    const center = savedView?.center;
    const zoom = savedView?.zoom;

    if (
      !center ||
      center.length !== 2 ||
      !this.hasUsableCoordinates(center[1], center[0]) ||
      typeof zoom !== 'number' ||
      !Number.isFinite(zoom)
    ) {
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

    if (
      !Number.isFinite(entityId) ||
      entityId <= 0 ||
      !title ||
      !this.hasUsableCoordinates(latitude, longitude)
    ) {
      return null;
    }

    return {
      entityType,
      entityId,
      title,
      subtitle: typeof value.subtitle === 'string' && value.subtitle.trim() ? value.subtitle : null,
      categoryLabel:
        typeof value.categoryLabel === 'string' && value.categoryLabel.trim()
          ? value.categoryLabel
          : null,
      imageUrl: typeof value.imageUrl === 'string' && value.imageUrl.trim() ? value.imageUrl : null,
      detailRoute:
        typeof value.detailRoute === 'string' && value.detailRoute.trim()
          ? value.detailRoute
          : null,
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
      globalThis.history.replaceState(
        {
          ...(history.state ?? {}),
          mapFocus,
          mapPreviewRestore: true,
        } satisfies MapNavigationState,
        '',
        this.router.url,
      );
    } catch {}
  }

  private clearMapFocusHistoryState(): void {
    try {
      const currentState = history.state as MapNavigationState | null;
      if (!currentState?.mapFocus && !currentState?.mapPreviewRestore) return;

      const nextState = { ...currentState };
      delete nextState.mapFocus;
      delete nextState.mapPreviewRestore;
      globalThis.history.replaceState(nextState, '', this.router.url);
    } catch {}
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
    const visibleBounds = this.normalizeViewportBounds({
      north: mapBounds.getNorth(),
      south: mapBounds.getSouth(),
      east: mapBounds.getEast(),
      west: mapBounds.getWest(),
    });

    const expectedMode: MapMarkersMode =
      zoom < DESTINATION_ZOOM_THRESHOLD ? 'destinations' : 'entities';

    if (this.isWithinCachedViewport(visibleBounds, expectedMode)) {
      return;
    }

    const requestedBounds = this.expandViewportBounds(visibleBounds, VIEWPORT_CACHE_PADDING);

    const seq = ++this.requestSeq;
    this.isLoading = true;
    this.loadErrorMessage = '';
    this.cdr.detectChanges();

    this.mapApi
      .getMarkersInViewport(requestedBounds, zoom, this.getRequestedCategories())
      .subscribe({
        next: (response) => {
          if (seq !== this.requestSeq) return;
          this.lastFetchedViewport = { ...requestedBounds, mode: response.mode };
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

    return (
      bounds.north <= last.north &&
      bounds.south >= last.south &&
      this.isLongitudeRangeContained(bounds, last)
    );
  }

  private expandViewportBounds(bounds: MapViewportBounds, paddingRatio: number): MapViewportBounds {
    const latitudePadding = (bounds.north - bounds.south) * paddingRatio;
    const longitudeSpan = this.longitudeSpan(bounds);
    const longitudePadding = longitudeSpan * paddingRatio;
    const unwrappedEast = bounds.east > bounds.west ? bounds.east : bounds.east + 360;

    return this.normalizeViewportBounds({
      north: bounds.north + latitudePadding,
      south: bounds.south - latitudePadding,
      east: unwrappedEast + longitudePadding,
      west: bounds.west - longitudePadding,
    });
  }

  private normalizeViewportBounds(bounds: MapViewportBounds): MapViewportBounds {
    let north = Math.min(90, Math.max(-90, bounds.north));
    let south = Math.min(90, Math.max(-90, bounds.south));
    const rawLongitudeSpan = bounds.east - bounds.west;
    let east = this.normalizeLongitude(bounds.east);
    let west = this.normalizeLongitude(bounds.west);

    if (north <= south) {
      const center = (north + south) / 2;
      north = Math.min(90, center + 0.000001);
      south = Math.max(-90, center - 0.000001);
    }

    if (Math.abs(rawLongitudeSpan) >= 360) {
      east = 180;
      west = -180;
    } else if (east === west) {
      east = Math.min(180, east + 0.000001);
      west = Math.max(-180, west - 0.000001);
    }

    return { north, south, east, west };
  }

  private normalizeLongitude(longitude: number): number {
    if (longitude === 180) return 180;
    const normalized = ((((longitude + 180) % 360) + 360) % 360) - 180;
    return Object.is(normalized, -0) ? 0 : normalized;
  }

  private longitudeSpan(bounds: MapViewportBounds): number {
    if (bounds.west === -180 && bounds.east === 180) return 360;
    return bounds.east > bounds.west
      ? bounds.east - bounds.west
      : 180 - bounds.west + (bounds.east + 180);
  }

  private isLongitudeRangeContained(inner: MapViewportBounds, outer: MapViewportBounds): boolean {
    if (this.longitudeSpan(outer) >= 359.999999) return true;

    const innerIntervals = this.longitudeIntervals(inner);
    const outerIntervals = this.longitudeIntervals(outer);
    return innerIntervals.every(([innerWest, innerEast]) =>
      outerIntervals.some(
        ([outerWest, outerEast]) => innerWest >= outerWest && innerEast <= outerEast,
      ),
    );
  }

  private longitudeIntervals(bounds: MapViewportBounds): Array<[number, number]> {
    if (this.longitudeSpan(bounds) >= 359.999999) return [[-180, 180]];
    if (bounds.east > bounds.west) return [[bounds.west, bounds.east]];
    return [
      [bounds.west, 180],
      [-180, bounds.east],
    ];
  }

  private hasEntityOnlyFilters(): boolean {
    return this.filterCategories.length > 0 || this.selectedCategoryLabelFilters.length > 0;
  }

  private invalidateMarkerCache(): void {
    this.lastFetchedViewport = null;
  }

  private applyMarkers(response: MapMarkersResponse): void {
    this.rawViewportMarkers = response.markers ?? [];
    this.rebuildAvailableFilters(response);
    this.renderMarkers(this.filterMarkersByMoreFilters(this.rawViewportMarkers), response.mode);
  }

  private spreadOverlappingMarkers(markers: MapEntityMarker[]): MapEntityMarker[] {
    const COORD_PRECISION = 5;
    const SPREAD_RADIUS = 0.00025;
    const groups = new Map<string, MapEntityMarker[]>();
    for (const marker of markers) {
      const key = `${marker.latitude.toFixed(COORD_PRECISION)},${marker.longitude.toFixed(COORD_PRECISION)}`;
      if (!groups.has(key)) groups.set(key, []);
      groups.get(key)!.push(marker);
    }
    const result: MapEntityMarker[] = [];
    for (const group of groups.values()) {
      if (group.length === 1) {
        result.push(group[0]);
      } else {
        group.forEach((marker, i) => {
          const angle = (2 * Math.PI * i) / group.length - Math.PI / 2;
          result.push({
            ...marker,
            latitude: marker.latitude + SPREAD_RADIUS * Math.cos(angle),
            longitude: marker.longitude + SPREAD_RADIUS * Math.sin(angle),
          });
        });
      }
    }
    return result;
  }

  private renderMarkers(markers: MapEntityMarker[], mode: MapMarkersMode): void {
    const COORD_PRECISION = 5;
    const groups = new Map<string, MapEntityMarker[]>();
    for (const marker of markers) {
      const coordKey = `${marker.latitude.toFixed(COORD_PRECISION)},${marker.longitude.toFixed(COORD_PRECISION)}`;
      if (!groups.has(coordKey)) groups.set(coordKey, []);
      groups.get(coordKey)!.push(marker);
    }

    const newSingleKeys = new Set<string>();
    const newStackKeys = new Set<string>();

    // First pass: single markers — added to DOM before stacks so stacks end up on top
    for (const [, group] of groups) {
      if (group.length !== 1) continue;
      const marker = group[0];
      const key = this.getMarkerKey(marker);
      newSingleKeys.add(key);
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

    // Second pass: stack markers — added to DOM last so they sit on top of single markers
    for (const [coordKey, group] of groups) {
      if (group.length <= 1) continue;
      const stackKey = `stack:${coordKey}`;
      newStackKeys.add(stackKey);
      const existingStack = this.stackMarkersByKey.get(stackKey);
      if (existingStack) {
        this.refreshStackMarkerDom(existingStack.getElement(), group);
      } else {
        const stackMarker = this.createStackMarker(group);
        this.stackMarkersByKey.set(stackKey, stackMarker);
      }
    }

    for (const [key, entry] of this.markersByKey) {
      if (!newSingleKeys.has(key)) {
        entry.marker.remove();
        this.markersByKey.delete(key);
        this.markerImageFallbackUsed.delete(key);
      }
    }

    for (const [key, marker] of this.stackMarkersByKey) {
      if (!newStackKeys.has(key)) {
        marker.remove();
        this.stackMarkersByKey.delete(key);
      }
    }

    for (const marker of this.stackMarkersByKey.values()) {
      const container = marker.getElement().parentElement;
      if (container?.parentElement) {
        container.parentElement.appendChild(container);
      }
    }

    this.currentMode = mode;

    if (this.selectedPlace) {
      const selectedKey = this.getMarkerKey(this.selectedPlace);
      const stillVisible = this.markersByKey.get(selectedKey);
      const stillInViewport = markers.some((m) => this.getMarkerKey(m) === selectedKey);
      if (stillVisible) {
        this.selectedPlace = stillVisible.data;
        this.preserveSelectedPlaceFromNavigation = false;
      } else if (stillInViewport) {
        // selectedPlace is inside a stack marker — keep it
      } else if (!this.shouldPreserveSelectedPlaceDuringMarkerRefresh()) {
        this.selectedPlace = null;
        this.clearRoute();
      }
    }

    if (this.stackedPlacePicker) {
      const firstKey = this.getMarkerKey(this.stackedPlacePicker[0]);
      const coordKey = `${this.stackedPlacePicker[0].latitude.toFixed(COORD_PRECISION)},${this.stackedPlacePicker[0].longitude.toFixed(COORD_PRECISION)}`;
      const stackKey = `stack:${coordKey}`;
      const stillExists =
        this.stackMarkersByKey.has(stackKey) ||
        markers.some((m) => this.getMarkerKey(m) === firstKey);
      if (!stillExists) {
        this.stackedPlacePicker = null;
      }
    }

    if (markers.length > 0) {
      this.emptyStateMessage = '';
      this.showNoDataModal = false;
    } else if (mode === 'destinations') {
      this.emptyStateMessage = 'Pan or zoom in to discover destinations.';
      this.showNoDataModal = false;
    } else if (this.hasEntityOnlyFilters()) {
      this.emptyStateMessage = 'No places in this area match those filters.';
      this.showNoDataModal = false;
    } else {
      this.emptyStateMessage = '';
      if (!this.noDataModalDismissed) {
        this.showNoDataModal = true;
        this.loadDestinationsIfNeeded();
      }
    }
  }

  private createMarkerEntry(
    marker: MapEntityMarker,
    displayLat?: number,
    displayLon?: number,
  ): MarkerEntry {
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

    const pinLat = displayLat ?? marker.latitude;
    const pinLon = displayLon ?? marker.longitude;
    const mapMarker = new maplibregl.Marker({ element: button, anchor: 'center' })
      .setLngLat([pinLon, pinLat])
      .addTo(this.map);
    mapMarker.on('click', (event) =>
      this.onMapLibreMarkerClick(event as { originalEvent?: MouseEvent }, key, marker),
    );

    const entry: MarkerEntry = { data: marker, marker: mapMarker, imageElement: img };
    this.refreshMarkerDom(entry, marker);
    return entry;
  }

  private createStackMarker(group: MapEntityMarker[]): maplibregl.Marker {
    const button = document.createElement('button');
    button.type = 'button';
    this.refreshStackMarkerDom(button, group);

    const { latitude, longitude } = group[0];
    const mapMarker = new maplibregl.Marker({ element: button, anchor: 'center' })
      .setLngLat([longitude, latitude])
      .addTo(this.map);

    const getGroup = (): MapEntityMarker[] => this.stackGroupByElement.get(button) ?? [];
    let lastActivatedAt = 0;
    const stopMarkerEvent = (event: Event): void => {
      event.stopPropagation();
    };
    const activate = (event: Event): void => {
      event.preventDefault();
      event.stopPropagation();

      const now = Date.now();
      if (now - lastActivatedAt < MARKER_ACTIVATION_DEDUPE_MS) return;
      lastActivatedAt = now;
      this.lastMarkerActivationKey = `stack:${group[0]?.latitude}:${group[0]?.longitude}`;
      this.lastMarkerActivationAt = now;
      this.openStackPicker(getGroup());
    };

    button.addEventListener('pointerdown', stopMarkerEvent);
    button.addEventListener('pointerup', (event) => {
      event.stopPropagation();
      if (event.pointerType === 'mouse') return;
      activate(event);
    });
    button.addEventListener('click', (event) => {
      activate(event);
    });
    mapMarker.on('click', (event) => {
      if (event && typeof event === 'object' && 'originalEvent' in event && event.originalEvent) {
        activate(event.originalEvent as Event);
      }
    });

    return mapMarker;
  }

  private refreshStackMarkerDom(button: HTMLElement, group: MapEntityMarker[]): void {
    this.stackGroupByElement.set(button, group);
    const typeClass = `gt-marker-stack--${this.markerVisualCategory(group[0])}`;
    button.classList.remove(
      'gt-marker-stack--place',
      'gt-marker-stack--stay',
      'gt-marker-stack--restaurant',
      'gt-marker-stack--drinks',
      'gt-marker-stack--attraction',
      'gt-marker-stack--other',
      'gt-marker-stack--event',
      'gt-marker-stack--destination',
    );
    button.classList.add('gt-marker-stack', typeClass);
    button.setAttribute(
      'aria-label',
      `${group.length} places here: ${group.map((m) => m.title).join(', ')}`,
    );
    button.setAttribute('title', `${group.length} places here`);
    Object.assign(button.style, {
      width: '52px',
      height: '52px',
      padding: '0',
      border: '0',
      background: 'transparent',
      cursor: 'pointer',
      pointerEvents: 'auto',
      position: 'relative',
      touchAction: 'manipulation',
      zIndex: '20',
    });
    button.innerHTML = '';

    const layers = Math.min(group.length, 3);
    for (let i = layers; i >= 1; i--) {
      const layer = document.createElement('span');
      layer.className = `gt-marker-stack__layer gt-marker-stack__layer--${i}`;
      const offset = (layers - i) * 4;
      Object.assign(layer.style, {
        position: 'absolute',
        left: `${offset}px`,
        top: `${offset}px`,
        width: '42px',
        height: '42px',
        borderRadius: '50%',
        overflow: 'hidden',
        border: '3px solid white',
        background: '#e2e8f0',
        boxShadow: '0 4px 12px rgba(15, 23, 42, 0.28)',
        pointerEvents: 'none',
      });

      const img = document.createElement('img');
      img.alt = '';
      img.draggable = false;
      Object.assign(img.style, {
        width: '100%',
        height: '100%',
        objectFit: 'cover',
        display: 'block',
        pointerEvents: 'none',
      });
      const src = group[layers - i]?.imageUrl ?? EXPLORE_IMAGE_FALLBACK;
      img.src = src;
      img.addEventListener('error', () => {
        img.src = EXPLORE_IMAGE_FALLBACK;
      });

      layer.appendChild(img);
      button.appendChild(layer);
    }
  }

  private openStackPicker(group: MapEntityMarker[]): void {
    this.ngZone.run(() => {
      this.selectedPlace = null;
      this.routeErrorMessage = '';
      this.routeInfoMessage = '';
      this.clearRoute();
      this.stackedPlacePicker = group;
      this.cdr.detectChanges();
    });
  }

  closeStackPicker(): void {
    this.stackedPlacePicker = null;
  }

  selectFromStack(marker: MapEntityMarker): void {
    this.stackedPlacePicker = null;
    this.onMarkerClick(marker);
  }

  private refreshMarkerDom(entry: MarkerEntry, marker: MapEntityMarker): void {
    const button = entry.marker.getElement();
    button.classList.add('gt-marker');
    button.classList.remove(
      'gt-marker--place',
      'gt-marker--stay',
      'gt-marker--restaurant',
      'gt-marker--drinks',
      'gt-marker--attraction',
      'gt-marker--other',
      'gt-marker--event',
      'gt-marker--destination',
    );
    button.classList.add(`gt-marker--${this.markerVisualCategory(marker)}`);

    const ariaLabel = `Open ${this.markerTypeLabel(marker)} marker: ${marker.title}`;
    if (button.getAttribute('aria-label') !== ariaLabel) {
      button.setAttribute('aria-label', ariaLabel);
    }
    button.title = ariaLabel;

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

  private onMapLibreMarkerClick(
    event: { originalEvent?: MouseEvent },
    key: string,
    marker: MapEntityMarker,
  ): void {
    event.originalEvent?.preventDefault();
    event.originalEvent?.stopPropagation();
    this.activateMarker(key, marker);
  }

  private activateMarker(key: string, marker: MapEntityMarker): void {
    if (this.stackedPlacePicker) {
      this.stackedPlacePicker = null;
      this.cdr.detectChanges();
      return;
    }

    const now = Date.now();
    if (
      this.lastMarkerActivationKey === key &&
      now - this.lastMarkerActivationAt < MARKER_ACTIVATION_DEDUPE_MS
    ) {
      return;
    }

    this.lastMarkerActivationKey = key;
    this.lastMarkerActivationAt = now;

    const entry = this.markersByKey.get(key);
    this.onMarkerClick(entry?.data ?? marker);
  }

  private onMarkerClick(marker: MapEntityMarker): void {
    if (this.isPinToPinMode) {
      if (marker.entityType !== 'destination') {
        void this.openPinToPinDirections(marker);
      }
      return;
    }

    this.preserveSelectedPlaceFromNavigation = false;
    this.clearMapFocusHistoryState();

    if (marker.entityType === 'destination') {
      this.selectedPlace = null;
      this.focusedDestination = marker;
      this.routeErrorMessage = '';
      this.routeInfoMessage = '';
      this.searchResults = [];
      this.clearRoute();
      this.noDataModalDismissed = true;

      this.destinationName = marker.title;
      this.destinationLat = marker.latitude;
      this.destinationLon = marker.longitude;
      this.searchQuery = marker.title;
      this.isUserLocation = false;

      sessionStorage.setItem(
        'mapLocation',
        JSON.stringify({
          name: marker.title,
          lat: marker.latitude,
          lon: marker.longitude,
          isUserLocation: false,
        }),
      );

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
    this.routeErrorMessage = '';
    this.routeInfoMessage = '';
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
    // The viewport endpoint now returns only places (including stays). Place type
    // and recommended-tag filters are applied to that result set on the client.
    return [];
  }

  private renderFilteredMarkers(): void {
    this.renderMarkers(this.filterMarkersByMoreFilters(this.rawViewportMarkers), this.currentMode);
    this.cdr.detectChanges();
  }

  private clearSelectionForFilterChange(): void {
    this.preserveSelectedPlaceFromNavigation = false;
    this.clearMapFocusHistoryState();
    this.selectedPlace = null;
    this.stackedPlacePicker = null;
    this.routeErrorMessage = '';
    this.routeInfoMessage = '';
    this.clearRoute();
  }

  private rebuildAvailableFilters(response: MapMarkersResponse): void {
    const placeTypes = this.mapFilterState.rebuildAvailablePlaceTypes(
      response.mode === 'entities' ? this.rawViewportMarkers : [],
      this.filterCategories,
      response.placeTypes,
    );
    this.categories = [
      { id: 'all', label: 'All' },
      ...placeTypes.map((option) => ({ id: option.id, label: option.label, count: option.count })),
    ];

    this.availableMoreFilters = response.mode === 'entities'
      ? this.mapFilterState.setRecommendedTagFilters(
          response.recommendedTags,
          this.selectedCategoryLabelFilters,
        )
      : this.mapFilterState.availableMoreFilters();
  }

  private restoreAvailableFiltersFromState(): void {
    this.categories = [
      { id: 'all', label: 'All' },
      ...this.mapFilterState.availablePlaceTypes().map((option) => ({
        id: option.id,
        label: option.label,
        count: option.count,
      })),
    ];
    this.availableMoreFilters = this.mapFilterState.availableMoreFilters();
  }

  private filterMarkersByMoreFilters(markers: MapEntityMarker[]): MapEntityMarker[] {
    return this.mapFilterState.filterMarkers(
      markers,
      this.filterCategories,
      this.selectedCategoryLabelFilters,
    );
  }

  private persistMapFilters(): void {
    this.mapFilterState.persistSelection(this.filterCategories, this.selectedCategoryLabelFilters);
  }

  private selectedMoreFilterLabels(): string[] {
    return this.mapFilterState.labelsForCategoryLabelIds(this.selectedCategoryLabelFilters);
  }

  private syncActiveFilters(): void {
    this.activeCategory = this.filterCategories[0] ?? 'all';
    this.hasActiveFilters =
      this.filterCategories.length > 0 || this.selectedCategoryLabelFilters.length > 0;
  }

  private getMarkerKey(marker: MapEntityMarker): string {
    return `${marker.entityType}:${marker.entityId}`;
  }

  private toAnalyticsEntityType(marker: MapEntityMarker): AnalyticsEntityType {
    switch (marker.entityType) {
      case 'stay':
        return 'hotel';
      case 'event':
        return 'event';
      case 'destination':
        return 'destination';
      default:
        return 'place';
    }
  }

  private markerVisualCategory(
    marker: MapEntityMarker,
  ): 'stay' | 'restaurant' | 'drinks' | 'attraction' | 'other' | 'destination' | 'event' {
    if (marker.entityType === 'destination') return 'destination';
    if (marker.entityType === 'event') return 'event';
    if (marker.entityType === 'stay') return 'stay';

    const label = `${marker.placeTypeLabel ?? ''} ${marker.categoryLabel ?? ''}`.toLowerCase();
    if (/restaurant|dining|food/.test(label)) return 'restaurant';
    if (/cafe|coffee|bar|pub|drink|nightlife|club/.test(label)) return 'drinks';
    if (/landmark|museum|gallery|park|nature|attraction|monument|culture|historic/.test(label)) {
      return 'attraction';
    }
    return 'other';
  }

  private markerTypeLabel(marker: MapEntityMarker): string {
    if (marker.entityType === 'stay') return 'stay';
    if (marker.entityType === 'event') return 'event';
    if (marker.entityType === 'destination') return 'destination';
    return 'place';
  }

  private shouldPreserveSelectedPlaceDuringMarkerRefresh(): boolean {
    return (
      this.preserveSelectedPlaceFromNavigation || this.isLoadingRoute || this.hasRenderedRoute()
    );
  }

  private hasRenderedRoute(): boolean {
    return !!this.map?.getSource(this.ROUTE_SOURCE);
  }

  private isCurrentRouteRequest(routeRequestId: number, selectedKey: string): boolean {
    return (
      routeRequestId === this.routeRequestSeq &&
      this.selectedPlace !== null &&
      this.getMarkerKey(this.selectedPlace) === selectedKey
    );
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

  private hasUsableCoordinates(
    latitude: number | null | undefined,
    longitude: number | null | undefined,
  ): boolean {
    return (
      typeof latitude === 'number' &&
      typeof longitude === 'number' &&
      Number.isFinite(latitude) &&
      Number.isFinite(longitude) &&
      latitude >= -90 &&
      latitude <= 90 &&
      longitude >= -180 &&
      longitude <= 180 &&
      (latitude !== 0 || longitude !== 0)
    );
  }

  private drawRoute(route: MapRouteResult): void {
    const coordinates = route.coordinates.map(
      (point) => [point.longitude, point.latitude] as [number, number],
    );
    if (coordinates.length < 2) {
      this.routeInfoMessage = '';
      this.routeErrorMessage = 'Directions are unavailable right now.';
      return;
    }

    const data: GeoJSON.FeatureCollection = {
      type: 'FeatureCollection',
      features: [
        {
          type: 'Feature',
          geometry: {
            type: 'LineString',
            coordinates,
          },
          properties: {},
        },
      ],
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

  private fitRoute(
    route: MapRouteResult,
    origin?: { latitude: number; longitude: number } | null,
    destination?: { latitude: number; longitude: number } | null,
  ): void {
    const routeCoords = route.coordinates.map(
      (point) => [point.longitude, point.latitude] as [number, number],
    );
    if (routeCoords.length < 2) return;

    const allCoords: [number, number][] = [...routeCoords];

    if (origin && this.hasUsableCoordinates(origin.latitude, origin.longitude)) {
      allCoords.push([origin.longitude, origin.latitude]);
    }
    if (destination && this.hasUsableCoordinates(destination.latitude, destination.longitude)) {
      allCoords.push([destination.longitude, destination.latitude]);
    }

    const bounds = allCoords.reduce(
      (acc, coord) => acc.extend(coord as maplibregl.LngLatLike),
      new maplibregl.LngLatBounds(allCoords[0], allCoords[0]),
    );

    this.map.fitBounds(bounds, {
      padding: { top: 100, bottom: 260, left: 80, right: 80 },
      duration: 900,
    });
  }

  clearRoute(): void {
    if (this.map?.getLayer(this.ROUTE_LAYER)) this.map.removeLayer(this.ROUTE_LAYER);
    if (this.map?.getLayer(this.ROUTE_OUTLINE)) this.map.removeLayer(this.ROUTE_OUTLINE);
    if (this.map?.getSource(this.ROUTE_SOURCE)) this.map.removeSource(this.ROUTE_SOURCE);
    this.activeRouteDestinationKey = null;
    this.activeRouteMode = null;
    this.activeRouteOrigin = null;
  }

  private placeUserMarker(lat: number, lon: number): void {
    const data: GeoJSON.FeatureCollection = {
      type: 'FeatureCollection',
      features: [
        {
          type: 'Feature',
          geometry: { type: 'Point', coordinates: [lon, lat] },
          properties: {},
        },
      ],
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
    this.destinationSvc.useLocationMode();
    this.isUserLocation = true;
    this.preserveSelectedPlaceFromNavigation = false;
    this.clearMapFocusHistoryState();
    this.selectedPlace = null;
    this.focusedDestination = null;
    this.routeErrorMessage = '';
    this.routeInfoMessage = '';
    this.clearRoute();
    this.noDataModalDismissed = false;

    if (!this.map) return;

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

    sessionStorage.setItem(
      'mapLocation',
      JSON.stringify({
        name: 'My Location',
        lat,
        lon,
        isUserLocation: true,
      }),
    );

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
      this.scheduleLocationSuccess(loc.latitude, loc.longitude);
      return;
    }

    this.destinationName = loc.displayLabel || 'Approximate Location';
    this.destinationLat = loc.latitude;
    this.destinationLon = loc.longitude;
    this.destinationSvc.useLocationMode();
    this.isUserLocation = false;
    this.loadErrorMessage = '';
    this.preserveSelectedPlaceFromNavigation = false;
    this.clearMapFocusHistoryState();
    this.selectedPlace = null;
    this.focusedDestination = null;
    this.routeErrorMessage = '';
    this.routeInfoMessage = '';
    this.clearRoute();

    this.map.resize();
    this.map.flyTo({
      center: [loc.longitude, loc.latitude],
      zoom: INITIAL_LOCATION_ZOOM,
      duration: 1000,
      padding: { top: 120, bottom: 0, left: 0, right: 0 },
    });

    sessionStorage.setItem(
      'mapLocation',
      JSON.stringify({
        name: this.destinationName,
        lat: loc.latitude,
        lon: loc.longitude,
        isUserLocation: false,
      }),
    );

    this.invalidateMarkerCache();
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
      sessionStorage.setItem(
        'mapLocation',
        JSON.stringify({
          name: 'My Location',
          lat: loc.latitude,
          lon: loc.longitude,
          isUserLocation: true,
        }),
      );
    }
  }

  private scheduleLocationSuccess(lat: number, lon: number): void {
    setTimeout(() => {
      if (this.isDestroyed) return;
      this.ngZone.run(() => this.handleLocationSuccess(lat, lon));
    }, 0);
  }

  private readSessionJson<T>(key: string): T | null {
    try {
      const raw = sessionStorage.getItem(key);
      return raw ? (JSON.parse(raw) as T) : null;
    } catch {
      sessionStorage.removeItem(key);
      return null;
    }
  }
}
