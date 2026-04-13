import { Component, OnDestroy, AfterViewInit, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import maplibregl, {
  Map as MapLibreMap,
  GeoJSONSource,
  MapMouseEvent,
  MapStyleImageMissingEvent,
} from 'maplibre-gl';

const MAP_STYLE_URL = '/map-style.json';

// OSRM public API
const OSRM_URL = 'https://router.project-osrm.org/route/v1/foot';

interface Place {
  name: string;
  category: string;
  rating: string;
  distance: string;
  status: string;
  lat: number;
  lon: number;
}

@Component({
  selector: 'app-map-page',
  imports: [CommonModule, FormsModule],
  templateUrl: './map-page.component.html',
  styleUrl: './map-page.component.css',
})
export class MapPageComponent implements AfterViewInit, OnDestroy {
  private map!: MapLibreMap;

  destinationName = 'Search Your Destination';
  destinationLat = 44.0165;
  destinationLon = 21.0059;

  isLoading = false;
  isLocating = false;
  isLoadingRoute = false;
  activeTab = 'map';
  selectedPlace: Place | null = null;
  hasActiveFilters = false;
  isUserLocation = false;

  private userLat: number | null = null;
  private userLon: number | null = null;

  private readonly USER_SOURCE   = 'user-location-source';
  private readonly USER_LAYER    = 'user-location-layer';
  private readonly USER_RING     = 'user-location-ring';
  private readonly ROUTE_SOURCE  = 'route-source';
  private readonly ROUTE_LAYER   = 'route-layer';
  private readonly ROUTE_OUTLINE = 'route-layer-outline';

  activeCategory = 'all';
  categories = [
    { id: 'all',         label: 'All' },
    { id: 'restaurants', label: 'Restaurants' },
    { id: 'hotels',      label: 'Hotels' },
    { id: 'attractions', label: 'Attractions' },
    { id: 'events',      label: 'Events' },
    { id: 'shopping',    label: 'Shopping' },
  ];

  searchQuery = '';
  searchResults: any[] = [];
  private searchTimeout?: ReturnType<typeof setTimeout>;

  constructor(public router: Router, private cdr: ChangeDetectorRef) {
    const nav = this.router.getCurrentNavigation();
    const state = (nav?.extras?.state ?? history.state) as any;
    const savedLocation = sessionStorage.getItem('mapLocation');

    if (savedLocation) {
      const loc = JSON.parse(savedLocation);
      this.destinationName = loc.name ?? this.destinationName;
      this.destinationLat  = loc.lat  ?? this.destinationLat;
      this.destinationLon  = loc.lon  ?? this.destinationLon;
      this.isUserLocation  = loc.isUserLocation === true;
      if (loc.isUserLocation) { this.userLat = loc.lat; this.userLon = loc.lon; }
    }
    if (state?.name) {
      this.destinationName = state.name;
      this.destinationLat  = state.lat ?? this.destinationLat;
      this.destinationLon  = state.lon ?? this.destinationLon;
      this.isUserLocation  = !!(state.isUserLocation);
      sessionStorage.setItem('mapLocation', JSON.stringify({
        name: this.destinationName, lat: this.destinationLat,
        lon: this.destinationLon, isUserLocation: this.isUserLocation,
      }));
    }
    if (state?.fromFilters) this.activeCategory = 'all';
    if (state?.cleared) { this.hasActiveFilters = false; }
    else { this.hasActiveFilters = !!sessionStorage.getItem('mapFilters'); }
  }

  ngAfterViewInit(): void { setTimeout(() => this.initMap(), 100); }

  ngOnDestroy(): void {
    this.map?.remove();
    window.removeEventListener('resize', this.resizeHandler);
  }

  private resizeHandler = () => this.map?.resize();

  // ── Map init ─────────────────────────────────────────────────────────────

  private initMap(): void {
    const container = document.getElementById('map');
    if (!container) return;

    this.map = new maplibregl.Map({
      container,
      style: MAP_STYLE_URL,
      center: [this.destinationLon, this.destinationLat],
      zoom: 13,
      attributionControl: false,
    });

    window.addEventListener('resize', this.resizeHandler);
    this.map.touchZoomRotate.enable();
    this.map.touchPitch.enable();

    this.map.on('load', () => {
      this.map.resize();
      this.map.on('styleimagemissing', (e: MapStyleImageMissingEvent) => {
        this.map.addImage(e.id, { width: 1, height: 1, data: new Uint8Array([0, 0, 0, 0]) });
      });

      const savedFilters = sessionStorage.getItem('mapFilters');
      if (savedFilters) {
        const f = JSON.parse(savedFilters);
        if (f.categories?.length > 0) this.applyMultipleFilters(f.categories);
        else this.applyFilter('more');
      } else {
        this.applyFilter(this.activeCategory);
      }

      if (this.isUserLocation) {
        setTimeout(() => this.placeUserMarker(this.destinationLat, this.destinationLon), 300);
      }
    });

    this.map.on('click', (e: MapMouseEvent) => {
      const features = this.map.queryRenderedFeatures(e.point);
      const poi = features.find((f: maplibregl.MapGeoJSONFeature) =>
        f.properties?.['name'] && f.geometry.type === 'Point'
      );

      if (poi) {
        const name = poi.properties?.['name'] ?? 'Unknown Place';
        const type = poi.properties?.['class'] ?? poi.properties?.['type'] ?? poi.layer?.id ?? 'Place';
        const coords = (poi.geometry as GeoJSON.Point).coordinates;
        const poiLon = coords[0];
        const poiLat = coords[1];

        let distance = '—';
        if (this.userLat !== null && this.userLon !== null) {
          distance = this.formatDistance(this.haversineKm(this.userLat, this.userLon, poiLat, poiLon));
        }

        this.selectedPlace = {
          name,
          category: this.formatCategory(type),
          rating: (3.5 + Math.random() * 1.5).toFixed(1),
          distance,
          status: 'Open Now • Closes 10 PM',
          lat: poiLat,
          lon: poiLon,
        };
        this.clearRoute(); // ukloni staru rutu
      } else {
        this.selectedPlace = null;
        this.clearRoute();
      }
      this.cdr.detectChanges();
    });
  }

  // ── Routing (OSRM) ───────────────────────────────────────────────────────

  openDirections(): void {
    if (!this.selectedPlace) return;

    // Nema korisnikove lokacije → fallback Google Maps
    if (this.userLat === null || this.userLon === null) {
      const dest = `${this.selectedPlace.lat},${this.selectedPlace.lon}`;
      const name = encodeURIComponent(this.selectedPlace.name);
      window.open(`https://www.google.com/maps/search/?api=1&query=${name}@${dest}`, '_blank');
      return;
    }

    this.isLoadingRoute = true;
    this.cdr.detectChanges();

    // OSRM: koordinate su lon,lat
    const origin = `${this.userLon},${this.userLat}`;
    const dest   = `${this.selectedPlace.lon},${this.selectedPlace.lat}`;
    const url    = `${OSRM_URL}/${origin};${dest}?overview=full&geometries=geojson`;

    fetch(url)
      .then(r => r.json())
      .then(data => {
        if (data.code !== 'Ok' || !data.routes?.length) {
          console.warn('OSRM: nema rute', data);
          this.isLoadingRoute = false;
          this.cdr.detectChanges();
          return;
        }
        const geometry: GeoJSON.LineString = data.routes[0].geometry;
        this.drawRoute(geometry);

        const coords = geometry.coordinates as [number, number][];
        const bounds = coords.reduce(
          (b, c) => b.extend(c as maplibregl.LngLatLike),
          new maplibregl.LngLatBounds(coords[0], coords[0])
        );
        this.map.fitBounds(bounds, { padding: 70, duration: 900 });

        this.isLoadingRoute = false;
        this.cdr.detectChanges();
      })
      .catch(err => {
        console.warn('OSRM greška:', err);
        this.isLoadingRoute = false;
        this.cdr.detectChanges();
      });
  }

  private drawRoute(geometry: GeoJSON.LineString): void {
    const data: GeoJSON.FeatureCollection = {
      type: 'FeatureCollection',
      features: [{ type: 'Feature', geometry, properties: {} }],
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
      paint: { 'line-color': '#e8722a', 'line-width': 5, 'line-opacity': 1 },
    });
  }

  clearRoute(): void {
    if (this.map?.getLayer(this.ROUTE_LAYER))   this.map.removeLayer(this.ROUTE_LAYER);
    if (this.map?.getLayer(this.ROUTE_OUTLINE)) this.map.removeLayer(this.ROUTE_OUTLINE);
    if (this.map?.getSource(this.ROUTE_SOURCE)) this.map.removeSource(this.ROUTE_SOURCE);
  }
  closePlace(): void {
    this.selectedPlace = null;
    this.clearRoute();
  }
  // ── User marker ──────────────────────────────────────────────────────────

  private placeUserMarker(lat: number, lon: number): void {
    const geojson: GeoJSON.FeatureCollection = {
      type: 'FeatureCollection',
      features: [{ type: 'Feature', geometry: { type: 'Point', coordinates: [lon, lat] }, properties: {} }],
    };
    if (this.map.getSource(this.USER_SOURCE)) {
      (this.map.getSource(this.USER_SOURCE) as GeoJSONSource).setData(geojson);
      return;
    }
    this.map.addSource(this.USER_SOURCE, { type: 'geojson', data: geojson });
    this.map.addLayer({
      id: this.USER_RING, type: 'circle', source: this.USER_SOURCE,
      paint: { 'circle-radius': 18, 'circle-color': '#e8722a', 'circle-opacity': 0.2,
               'circle-stroke-width': 1.5, 'circle-stroke-color': '#e8722a', 'circle-stroke-opacity': 0.4 },
    });
    this.map.addLayer({
      id: this.USER_LAYER, type: 'circle', source: this.USER_SOURCE,
      paint: { 'circle-radius': 9, 'circle-color': '#e8722a', 'circle-stroke-width': 3,
               'circle-stroke-color': '#ffffff', 'circle-opacity': 1, 'circle-stroke-opacity': 1 },
    });
  }

  // ── Locate me ────────────────────────────────────────────────────────────

  locateMe(): void {
    if (!navigator.geolocation || this.isLocating) return;
    const isSecure = location.protocol === 'https:' || location.hostname === 'localhost';
    if (!isSecure) { console.warn('Geolocation requires HTTPS.'); return; }

    this.isLocating = true;
    this.cdr.detectChanges();

    navigator.geolocation.getCurrentPosition(
      ({ coords }) => this.handleLocationSuccess(coords.latitude, coords.longitude),
      (err) => {
        if (err.code === GeolocationPositionError.PERMISSION_DENIED) {
          this.isLocating = false; this.cdr.detectChanges(); return;
        }
        navigator.geolocation.getCurrentPosition(
          ({ coords }) => this.handleLocationSuccess(coords.latitude, coords.longitude),
          (err2) => { console.warn('Location unavailable:', err2.message); this.isLocating = false; this.cdr.detectChanges(); },
          { timeout: 15000, maximumAge: 0, enableHighAccuracy: true }
        );
      },
      { timeout: 10000, maximumAge: 60000, enableHighAccuracy: false }
    );
  }

  private handleLocationSuccess(lat: number, lon: number): void {
    this.map.flyTo({ center: [lon, lat], zoom: 15, duration: 1200 });
    if (this.map.isStyleLoaded()) this.placeUserMarker(lat, lon);
    else this.map.once('load', () => this.placeUserMarker(lat, lon));

    this.userLat = lat; this.userLon = lon; this.isUserLocation = true;
    sessionStorage.setItem('mapLocation', JSON.stringify({ name: 'My Location', lat, lon, isUserLocation: true }));
    this.isLocating = false;
    this.cdr.detectChanges();
  }

  // ── Filters ──────────────────────────────────────────────────────────────

  selectCategory(cat: { id: string; label: string }): void {
    this.activeCategory = cat.id;
    this.selectedPlace = null;
    this.clearRoute();
    sessionStorage.removeItem('mapFilters');
    this.hasActiveFilters = false;
    if (this.map.isStyleLoaded()) this.applyFilter(cat.id);
  }

  private applyFilter(categoryId: string): void {
    const poiLayers = ['poi_z14', 'poi_z15', 'poi_z16', 'poi_transit'];
    const categoryMap: Record<string, string[]> = {
      all: [], restaurants: ['restaurant','cafe','fast_food','bar','pub'],
      hotels: ['hotel','hostel','motel','guest_house','lodging'],
      attractions: ['attraction','museum','monument','gallery','viewpoint','tourism'],
      events: ['theatre','cinema','events_venue','nightclub'],
      shopping: ['shop','supermarket','mall','convenience','clothes','grocery'],
    };
    poiLayers.forEach(id => {
      if (!this.map.getLayer(id)) return;
      if (categoryId === 'all') this.map.setFilter(id, null);
      else this.map.setFilter(id, ['in', ['get', 'class'], ['literal', categoryMap[categoryId] ?? []]]);
    });
  }

  private applyMultipleFilters(categoryIds: string[]): void {
    const poiLayers = ['poi_z14', 'poi_z15', 'poi_z16', 'poi_transit'];
    const categoryMap: Record<string, string[]> = {
      restaurants:['restaurant','cafe','fast_food','bar','pub'],
      hotels:['hotel','hostel','motel','guest_house','lodging'],
      attractions:['attraction','museum','monument','gallery','viewpoint','tourism'],
      events:['theatre','cinema','events_venue','nightclub'],
      shopping:['shop','supermarket','mall','convenience','clothes','grocery'],
      cafes:['cafe','coffee'], bars:['bar','pub'], parks:['park','playground'],
      museums:['museum','gallery'], transport:['bus_stop','subway','train_station','tram_stop'],
      health:['hospital','pharmacy','clinic','doctor'], sports:['sports_centre','stadium','gym','fitness'],
    };
    const unique = [...new Set(categoryIds.flatMap(id => categoryMap[id] ?? []))];
    poiLayers.forEach(id => {
      if (!this.map.getLayer(id)) return;
      if (unique.length === 0) this.map.setFilter(id, null);
      else this.map.setFilter(id, ['in', ['get', 'class'], ['literal', unique]]);
    });
  }

  // ── Helpers ──────────────────────────────────────────────────────────────

  private haversineKm(lat1: number, lon1: number, lat2: number, lon2: number): number {
    const R = 6371, dLat = (lat2-lat1)*Math.PI/180, dLon = (lon2-lon1)*Math.PI/180;
    const a = Math.sin(dLat/2)**2 + Math.cos(lat1*Math.PI/180)*Math.cos(lat2*Math.PI/180)*Math.sin(dLon/2)**2;
    return R * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1-a));
  }

  private formatDistance(km: number): string {
    return km < 1 ? Math.round(km * 1000) + ' m' : km.toFixed(1) + ' km';
  }

  private formatCategory(raw: string): string {
    const map: Record<string, string> = {
      restaurant:'Restaurant', cafe:'Café', hotel:'Hotel', museum:'Museum',
      attraction:'Attraction', shop:'Shop', bar:'Bar', park:'Park',
    };
    return map[raw.toLowerCase()] ?? raw.charAt(0).toUpperCase() + raw.slice(1);
  }

  zoomIn():  void { this.map.zoomIn({ duration: 300 }); }
  zoomOut(): void { this.map.zoomOut({ duration: 300 }); }
  goBack():  void { this.router.navigate(['/home']); }

  onSearchInput(value: string): void {
    clearTimeout(this.searchTimeout);
    if (value.trim().length < 2) { this.searchResults = []; return; }
    this.searchTimeout = setTimeout(() => this.geocode(value), 250);
  }

  private geocode(query: string): void {
    fetch(`https://nominatim.openstreetmap.org/search?format=json&q=${encodeURIComponent(query)}&limit=5&accept-language=en`)
      .then(r => r.json())
      .then(data => { this.searchResults = data; this.cdr.detectChanges(); })
      .catch(() => this.searchResults = []);
  }

  selectSearchResult(result: any): void {
    const lat = parseFloat(result.lat), lon = parseFloat(result.lon);
    const name = result.display_name?.split(',')[0] ?? '';
    this.searchResults = []; this.searchQuery = name;
    this.destinationName = name; this.destinationLat = lat; this.destinationLon = lon;
    this.selectedPlace = null; this.clearRoute();
    sessionStorage.setItem('mapLocation', JSON.stringify({ name, lat, lon }));
    this.map.flyTo({ center: [lon, lat], zoom: 14, duration: 1200 });
    this.hasActiveFilters = !!sessionStorage.getItem('mapFilters');
  }

  get isMoreActive(): boolean { return this.hasActiveFilters; }
}