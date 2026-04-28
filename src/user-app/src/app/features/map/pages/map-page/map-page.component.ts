import { AfterViewInit, ChangeDetectorRef, Component, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import maplibregl, {
  GeoJSONSource,
  Map as MapLibreMap,
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
import { ImageFallbackDirective } from '../../../../shared/directives/image-fallback.directive';
import { EXPLORE_IMAGE_FALLBACK } from '../../../../core/constants/explore-media.constants';
import type { AnalyticsEntityType } from '../../../../core/services/analytics.types';

const MAP_STYLE_URL = '/map-style.json';

const DESTINATION_ZOOM_THRESHOLD = 10;
const VIEWPORT_DEBOUNCE_MS = 250;
const VIEWPORT_CACHE_PADDING = 0.1;

interface MarkerEntry {
  data: MapEntityMarker;
  marker: maplibregl.Marker;
  imageElement: HTMLImageElement;
}

interface CachedViewport extends MapViewportBounds {
  mode: MapMarkersMode;
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
  private userLat: number | null = null;
  private userLon: number | null = null;
  private searchTimeout?: ReturnType<typeof setTimeout>;
  private filterCategories: string[] = [];
  private readonly markersByKey = new Map<string, MarkerEntry>();
  private readonly markerImageFallbackUsed = new Set<string>();
  private readonly allowedCategoryIds = new Set(['places', 'stays', 'events']);
  private viewportLoadTimeout?: ReturnType<typeof setTimeout>;
  private requestSeq = 0;
  private lastFetchedViewport: CachedViewport | null = null;

  currentMode: MapMarkersMode = 'entities';

  destinationName = 'Search Your Destination';
  destinationLat = 44.0165;
  destinationLon = 21.0059;

  isLoading = false;
  isLocating = false;
  isLoadingRoute = false;
  activeCategory = 'all';
  hasActiveFilters = false;
  isUserLocation = false;

  searchQuery = '';
  searchResults: MapSearchResult[] = [];

  selectedPlace: MapEntityMarker | null = null;
  emptyStateMessage = '';
  loadErrorMessage = '';
  routeErrorMessage = '';

  categories = [
    { id: 'all', label: 'All' },
    { id: 'places', label: 'Places' },
    { id: 'stays', label: 'Stays' },
    { id: 'events', label: 'Events' },
  ];

  constructor(
    public router: Router,
    private readonly cdr: ChangeDetectorRef,
    private readonly mapApi: MapApiService,
    private readonly analytics: AnalyticsService,
  ) {
    const nav = this.router.getCurrentNavigation();
    const state = (nav?.extras?.state ?? history.state) as {
      name?: string;
      lat?: number;
      lon?: number;
      isUserLocation?: boolean;
      fromFilters?: boolean;
      cleared?: boolean;
    };
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
      if (this.isUserLocation) {
        this.userLat = this.destinationLat;
        this.userLon = this.destinationLon;
      }
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

    const savedFilters = this.readSessionJson<{ categories?: string[] }>('mapFilters');
    if (savedFilters) {
      this.filterCategories = (savedFilters.categories ?? []).filter((category) => this.allowedCategoryIds.has(category));
      this.hasActiveFilters = this.filterCategories.length > 0;
    }

    if (state?.cleared) {
      this.filterCategories = [];
      this.hasActiveFilters = false;
    }
  }

  ngAfterViewInit(): void {
    setTimeout(() => this.initMap(), 0);
  }

  ngOnDestroy(): void {
    if (this.viewportLoadTimeout) {
      clearTimeout(this.viewportLoadTimeout);
    }

    if (this.map) {
      const center = this.map.getCenter();
      const zoom = this.map.getZoom();
      sessionStorage.setItem('mapViewState', JSON.stringify({
        center: [center.lng, center.lat],
        zoom,
      }));

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
    return this.hasActiveFilters;
  }

  zoomIn(): void {
    this.map?.zoomIn({ duration: 300 });
  }

  zoomOut(): void {
    this.map?.zoomOut({ duration: 300 });
  }

  goBack(): void {
    this.router.navigate(['/home']);
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
    this.searchQuery = result.name;
    this.destinationName = result.name;
    this.destinationLat = result.latitude;
    this.destinationLon = result.longitude;
    this.isUserLocation = false;
    this.selectedPlace = null;
    this.routeErrorMessage = '';
    this.clearRoute();

    sessionStorage.setItem('mapLocation', JSON.stringify({
      name: result.name,
      lat: result.latitude,
      lon: result.longitude,
      isUserLocation: false,
    }));

    this.map.jumpTo({ center: [result.longitude, result.latitude], zoom: 14 });
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
    if (!navigator.geolocation || this.isLocating) return;

    const isSecure = location.protocol === 'https:' || location.hostname === 'localhost';
    if (!isSecure) return;

    this.isLocating = true;
    this.cdr.detectChanges();

    navigator.geolocation.getCurrentPosition(
      ({ coords }) => this.handleLocationSuccess(coords.latitude, coords.longitude),
      (err) => {
        if (err.code === GeolocationPositionError.PERMISSION_DENIED) {
          this.isLocating = false;
          this.cdr.detectChanges();
          return;
        }

        navigator.geolocation.getCurrentPosition(
          ({ coords }) => this.handleLocationSuccess(coords.latitude, coords.longitude),
          () => {
            this.isLocating = false;
            this.cdr.detectChanges();
          },
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
    this.hasActiveFilters = false;
    this.selectedPlace = null;
    this.routeErrorMessage = '';
    this.clearRoute();
    sessionStorage.removeItem('mapFilters');

    this.analytics.track('filter_applied', {
      metadata: {
        surface: 'map',
        category: category.id,
      },
    });

    this.invalidateMarkerCache();
    this.scheduleViewportLoad(0);
  }

  openDirections(): void {
    if (!this.selectedPlace) return;

    const originLat = this.userLat ?? this.destinationLat;
    const originLon = this.userLon ?? this.destinationLon;

    this.isLoadingRoute = true;
    this.routeErrorMessage = '';
    this.analytics.track('directions_started', {
      entityType: this.toAnalyticsEntityType(this.selectedPlace),
      entityId: this.selectedPlace.entityId,
      metadata: {
        surface: 'map',
        provider: 'osrm',
      },
    });
    this.cdr.detectChanges();

    this.mapApi.getWalkingRoute(
      originLat,
      originLon,
      this.selectedPlace.latitude,
      this.selectedPlace.longitude,
    ).subscribe({
      next: (route) => {
        this.drawRoute(route);
        this.fitRoute(route);
        this.isLoadingRoute = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.isLoadingRoute = false;
        this.routeErrorMessage = 'Walking directions are unavailable right now.';
        this.cdr.detectChanges();
      },
    });
  }

  openDetails(): void {
    if (!this.selectedPlace?.detailRoute) return;
    void this.router.navigateByUrl(this.selectedPlace.detailRoute);
  }

  closePlace(): void {
    this.selectedPlace = null;
    this.routeErrorMessage = '';
    this.clearRoute();
  }

  private readonly resizeHandler = () => this.map?.resize();

  private initMap(): void {
    const container = document.getElementById('map');
    if (!container) return;

    let initCenter: [number, number] = [this.destinationLon, this.destinationLat];
    let initZoom = 13;

    const savedView = this.readSessionJson<{ center?: [number, number]; zoom?: number }>('mapViewState');
    if (savedView?.center && savedView.center.length === 2) {
      initCenter = savedView.center;
    }
    if (typeof savedView?.zoom === 'number') {
      initZoom = savedView.zoom;
    }

    if (savedView) {
      sessionStorage.removeItem('mapViewState');
    }

    this.map = new maplibregl.Map({
      container,
      style: MAP_STYLE_URL,
      center: initCenter,
      zoom: initZoom,
      attributionControl: false,
      fadeDuration: 0,
      trackResize: true,
    });

    window.addEventListener('resize', this.resizeHandler);
    this.map.touchZoomRotate.enable();
    this.map.touchPitch.enable();

    this.map.on('load', () => {
      this.map.resize();
      this.map.on('styleimagemissing', (event: MapStyleImageMissingEvent) => {
        this.map.addImage(event.id, { width: 1, height: 1, data: new Uint8Array([0, 0, 0, 0]) });
      });

      if (this.isUserLocation) {
        this.placeUserMarker(this.destinationLat, this.destinationLon);
      }

      this.scheduleViewportLoad(0);
    });

    this.map.on('moveend', () => {
      this.scheduleViewportLoad();
    });

    this.map.on('click', () => {
      if (this.selectedPlace) {
        this.closePlace();
        this.cdr.detectChanges();
      }
    });
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
    const markers = response.markers ?? [];
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

    this.currentMode = response.mode;

    if (this.selectedPlace) {
      const selectedKey = this.getMarkerKey(this.selectedPlace);
      const stillVisible = this.markersByKey.get(selectedKey);
      if (stillVisible) {
        this.selectedPlace = stillVisible.data;
      } else {
        this.selectedPlace = null;
        this.clearRoute();
      }
    }

    this.emptyStateMessage = markers.length === 0
      ? (response.mode === 'destinations'
        ? 'Pan or zoom in to discover destinations.'
        : 'GeoTrack does not have backend-backed places, stays, or events mapped for this area yet.')
      : '';
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

    button.addEventListener('click', (event) => {
      event.stopPropagation();
      const entry = this.markersByKey.get(key);
      this.onMarkerClick(entry?.data ?? marker);
    });

    const mapMarker = new maplibregl.Marker({ element: button, anchor: 'center' })
      .setLngLat([marker.longitude, marker.latitude])
      .addTo(this.map);

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

  private onMarkerClick(marker: MapEntityMarker): void {
    if (marker.entityType === 'destination') {
      this.selectedPlace = null;
      this.routeErrorMessage = '';
      this.searchResults = [];
      this.clearRoute();

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
    if (this.hasActiveFilters) {
      return this.filterCategories;
    }

    return this.activeCategory === 'all' ? [] : [this.activeCategory];
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

  private drawRoute(route: MapRouteResult): void {
    const coordinates = route.coordinates.map((point) => [point.longitude, point.latitude] as [number, number]);
    if (coordinates.length < 2) {
      this.routeErrorMessage = 'Walking directions are unavailable right now.';
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

    this.map.fitBounds(bounds, { padding: 70, duration: 900 });
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
    this.userLat = lat;
    this.userLon = lon;
    this.isUserLocation = true;
    this.selectedPlace = null;
    this.routeErrorMessage = '';
    this.clearRoute();

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
