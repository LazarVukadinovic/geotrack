import { AfterViewInit, ChangeDetectorRef, Component, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import maplibregl, {
  GeoJSONSource,
  Map as MapLibreMap,
  MapMouseEvent,
  MapStyleImageMissingEvent,
} from 'maplibre-gl';
import {
  MapApiService,
  MapEntitiesResponse,
  MapEntityMarker,
  MapRouteResult,
  MapSearchResult,
} from '../../../../core/services/map-api.service';
import { AnalyticsService } from '../../../../core/services/analytics.service';
import type { AnalyticsEntityType } from '../../../../core/services/analytics.types';

const MAP_STYLE_URL = '/map-style.json';

const DISTANCE_RADIUS_KM: Record<string, number | null> = {
  Any: null,
  '0.5km': 0.5,
  '1km': 1,
  '2km': 2,
  '5km': 5,
};

@Component({
  selector: 'app-map-page',
  imports: [CommonModule, FormsModule],
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
  private readonly ENTITY_SOURCE = 'geotrack-entity-source';
  private readonly ENTITY_LAYER = 'geotrack-entity-layer';

  private map!: MapLibreMap;
  private userLat: number | null = null;
  private userLon: number | null = null;
  private searchTimeout?: ReturnType<typeof setTimeout>;
  private filterCategories: string[] = [];
  private readonly markersByKey = new Map<string, MapEntityMarker>();
  private readonly allowedCategoryIds = new Set(['places', 'stays', 'events']);

  destinationName = 'Search Your Destination';
  destinationLat = 44.0165;
  destinationLon = 21.0059;

  isLoading = false;
  isLocating = false;
  isLoadingRoute = false;
  activeCategory = 'all';
  activeDistance = 'Any';
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

    const savedFilters = this.readSessionJson<{ categories?: string[]; distance?: string }>('mapFilters');
    if (savedFilters) {
      this.filterCategories = (savedFilters.categories ?? []).filter((category) => this.allowedCategoryIds.has(category));
      this.activeDistance = savedFilters.distance ?? 'Any';
      this.hasActiveFilters = this.filterCategories.length > 0 || this.activeDistance !== 'Any';
    }

    if (state?.cleared) {
      this.filterCategories = [];
      this.activeDistance = 'Any';
      this.hasActiveFilters = false;
    }
  }

  ngAfterViewInit(): void {
    setTimeout(() => this.initMap(), 0);
  }

  ngOnDestroy(): void {
    if (this.map) {
      const center = this.map.getCenter();
      const zoom = this.map.getZoom();
      sessionStorage.setItem('mapViewState', JSON.stringify({
        center: [center.lng, center.lat],
        zoom,
      }));
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
          this.analytics.track('search_performed', {
            metadata: {
              surface: 'map',
              queryLength: value.trim().length,
              resultCount: results.length,
            },
          });
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
    this.loadEntities();
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
    this.activeCategory = category.id;
    this.filterCategories = [];
    this.activeDistance = 'Any';
    this.hasActiveFilters = false;
    this.selectedPlace = null;
    this.routeErrorMessage = '';
    this.clearRoute();
    sessionStorage.removeItem('mapFilters');

    this.analytics.track('filter_applied', {
      metadata: {
        surface: 'map',
        category: category.id,
        distance: 'Any',
      },
    });

    this.loadEntities();
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

      this.ensureEntityLayer();

      if (this.isUserLocation) {
        this.placeUserMarker(this.destinationLat, this.destinationLon);
      }

      this.loadEntities();
    });

    this.map.on('click', this.ENTITY_LAYER, (event) => {
      const key = event.features?.[0]?.properties?.['key'];
      if (typeof key === 'string') {
        const marker = this.markersByKey.get(key) ?? null;
        this.selectedPlace = marker;
        this.routeErrorMessage = '';
        this.clearRoute();
        if (marker) {
          this.analytics.track('map_marker_opened', {
            entityType: this.toAnalyticsEntityType(marker),
            entityId: marker.entityId,
            metadata: {
              surface: 'map',
              category: marker.categoryLabel ?? marker.entityType,
            },
          });
        }
        this.cdr.detectChanges();
      }
    });

    this.map.on('click', (event: MapMouseEvent) => {
      const features = this.map.queryRenderedFeatures(event.point, { layers: [this.ENTITY_LAYER] });
      if (features.length === 0 && this.selectedPlace) {
        this.closePlace();
        this.cdr.detectChanges();
      }
    });

    this.map.on('mouseenter', this.ENTITY_LAYER, () => {
      this.map.getCanvas().style.cursor = 'pointer';
    });

    this.map.on('mouseleave', this.ENTITY_LAYER, () => {
      this.map.getCanvas().style.cursor = '';
    });
  }

  private loadEntities(): void {
    if (!this.map) return;

    this.isLoading = true;
    this.loadErrorMessage = '';
    this.emptyStateMessage = '';
    this.cdr.detectChanges();

    this.mapApi.getEntities(
      this.destinationLat,
      this.destinationLon,
      this.getRequestedCategories(),
      DISTANCE_RADIUS_KM[this.activeDistance],
    ).subscribe({
      next: (response) => {
        this.applyEntities(response);
        this.isLoading = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.markersByKey.clear();
        this.setEntityData([]);
        this.selectedPlace = null;
        this.emptyStateMessage = '';
        this.loadErrorMessage = "GeoTrack couldn't load backend-backed map results right now.";
        this.isLoading = false;
        this.cdr.detectChanges();
      },
    });
  }

  private applyEntities(response: MapEntitiesResponse): void {
    const markers = response.markers ?? [];
    const validKeys = new Set<string>();

    this.markersByKey.clear();
    for (const marker of markers) {
      const key = this.getMarkerKey(marker);
      validKeys.add(key);
      this.markersByKey.set(key, marker);
    }

    this.setEntityData(markers);

    if (this.selectedPlace && !validKeys.has(this.getMarkerKey(this.selectedPlace))) {
      this.selectedPlace = null;
      this.clearRoute();
    } else if (this.selectedPlace) {
      this.selectedPlace = this.markersByKey.get(this.getMarkerKey(this.selectedPlace)) ?? this.selectedPlace;
    }

    this.emptyStateMessage = markers.length === 0
      ? 'GeoTrack does not have backend-backed places, stays, or events mapped for this area yet.'
      : '';
  }

  private getRequestedCategories(): string[] {
    if (this.hasActiveFilters) {
      return this.filterCategories;
    }

    return this.activeCategory === 'all' ? [] : [this.activeCategory];
  }

  private ensureEntityLayer(): void {
    if (!this.map.getSource(this.ENTITY_SOURCE)) {
      this.map.addSource(this.ENTITY_SOURCE, {
        type: 'geojson',
        data: { type: 'FeatureCollection', features: [] },
      });
    }

    if (!this.map.getLayer(this.ENTITY_LAYER)) {
      this.map.addLayer({
        id: this.ENTITY_LAYER,
        type: 'circle',
        source: this.ENTITY_SOURCE,
        paint: {
          'circle-radius': 7,
          'circle-stroke-width': 2,
          'circle-stroke-color': '#ffffff',
          'circle-color': [
            'match',
            ['get', 'entityType'],
            'stay', '#e8570a',
            'event', '#1a2b4a',
            '#2f855a',
          ],
        },
      });
    }
  }

  private setEntityData(markers: MapEntityMarker[]): void {
    const data: GeoJSON.FeatureCollection = {
      type: 'FeatureCollection',
      features: markers.map((marker) => ({
        type: 'Feature',
        geometry: {
          type: 'Point',
          coordinates: [marker.longitude, marker.latitude],
        },
        properties: {
          key: this.getMarkerKey(marker),
          entityType: marker.entityType,
        },
      })),
    };

    const source = this.map.getSource(this.ENTITY_SOURCE) as GeoJSONSource | undefined;
    source?.setData(data);
  }

  private getMarkerKey(marker: MapEntityMarker): string {
    return `${marker.entityType}:${marker.entityId}`;
  }

  private toAnalyticsEntityType(marker: MapEntityMarker): AnalyticsEntityType {
    return marker.entityType === 'stay' ? 'hotel' : marker.entityType;
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
    this.loadEntities();
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
