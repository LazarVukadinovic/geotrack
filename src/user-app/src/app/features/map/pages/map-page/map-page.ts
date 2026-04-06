import { Component, OnInit, OnDestroy, AfterViewInit, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import maplibregl, { Map as MapLibreMap, GeoJSONSource, MapMouseEvent, MapStyleImageMissingEvent } from 'maplibre-gl';

const MAP_STYLE_URL = '/map-style.json';

interface Place {
  name: string;
  category: string;
  rating: string;
  distance: string;
  status: string;
}

@Component({
  selector: 'app-map-page',
  imports: [CommonModule, FormsModule],
  templateUrl: './map-page.html',
  styleUrl: './map-page.css',
})
export class MapPage implements AfterViewInit, OnDestroy {
  private map!: MapLibreMap;

  destinationName = 'Search Your Destination';
  destinationLat = 44.0165;
  destinationLon = 21.0059;

  isLoading = false;
  isLocating = false;
  activeTab = 'map';
  selectedPlace: Place | null = null;
  hasActiveFilters = false;
  isUserLocation = false;

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
  private readonly USER_SOURCE = 'user-location-source';
  private readonly USER_LAYER  = 'user-location-layer';
  private readonly USER_RING   = 'user-location-ring';
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
    }
    if (state?.name) {
      this.destinationName = state.name;
      this.destinationLat  = state.lat ?? this.destinationLat;
      this.destinationLon  = state.lon ?? this.destinationLon;
      this.isUserLocation  = !!(state.isUserLocation);
      sessionStorage.setItem('mapLocation', JSON.stringify({
        name: this.destinationName,
        lat:  this.destinationLat,
        lon:  this.destinationLon,
        isUserLocation: this.isUserLocation,
      }));
    }
    if (state?.fromFilters) {
      this.activeCategory = 'all';
    }
    if (state?.cleared) {
      this.hasActiveFilters = false;
    } else {
      this.hasActiveFilters = !!sessionStorage.getItem('mapFilters');
    }
  }

  ngAfterViewInit(): void {
    setTimeout(() => this.initMap(), 100);
  }

  ngOnDestroy(): void {
    this.map?.remove();
    window.removeEventListener('resize', this.resizeHandler);
  }

  private resizeHandler = () => this.map?.resize();

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
        const imageData = {
          width: 1,
          height: 1,
          data: new Uint8Array([0, 0, 0, 0])
        };
        this.map.addImage(e.id, imageData);
      });
      const savedFilters = sessionStorage.getItem('mapFilters');
      if (savedFilters) 
      {
        const f = JSON.parse(savedFilters);
        if (f.categories?.length > 0) {
          this.applyMultipleFilters(f.categories);
        } else {
          this.applyFilter('more');
        }
      }
      else 
      {
        this.applyFilter(this.activeCategory);
      }
      if (this.isUserLocation) {
        setTimeout(() => {
          this.placeUserMarker(this.destinationLat, this.destinationLon);
        }, 300);
      }
    });

    this.map.on('click', (e: MapMouseEvent) => {
      const features = this.map.queryRenderedFeatures(e.point);
      const poi = features.find((f: maplibregl.MapGeoJSONFeature) =>
        f.properties?.['name'] &&
        f.geometry.type === 'Point'
      );

      if (poi) {
        const name = poi.properties?.['name'] ?? 'Unknown Place';
        const type = poi.properties?.['class'] ??
                     poi.properties?.['type'] ??
                     poi.layer?.id ?? 'Place';

        this.selectedPlace = {
          name,
          category: this.formatCategory(type),
          rating: (3.5 + Math.random() * 1.5).toFixed(1),
          distance: (Math.random() * 0.9 + 0.1).toFixed(1) + ' mi',
          status: 'Open Now • Closes 10 PM',
        };
      } else {
        this.selectedPlace = null;
      }

      this.cdr.detectChanges();
    });
  }

  private placeUserMarker(lat: number, lon: number): void {
    const geojson: GeoJSON.FeatureCollection = {
      type: 'FeatureCollection',
      features: [{
        type: 'Feature',
        geometry: { type: 'Point', coordinates: [lon, lat] },
        properties: {}
      }]
    };
    if (this.map.getSource(this.USER_SOURCE)) {
      (this.map.getSource(this.USER_SOURCE) as GeoJSONSource)
        .setData(geojson);
      return;
    }

    this.map.addSource(this.USER_SOURCE, {
      type: 'geojson',
      data: geojson
    });

    this.map.addLayer({
      id: this.USER_RING,
      type: 'circle',
      source: this.USER_SOURCE,
      paint: {
        'circle-radius': 18,
        'circle-color': '#e8722a',
        'circle-opacity': 0.2,
        'circle-stroke-width': 1.5,
        'circle-stroke-color': '#e8722a',
        'circle-stroke-opacity': 0.4,
      }
    });

    this.map.addLayer({
      id: this.USER_LAYER,
      type: 'circle',
      source: this.USER_SOURCE,
      paint: {
        'circle-radius': 9,
        'circle-color': '#e8722a',
        'circle-stroke-width': 3,
        'circle-stroke-color': '#ffffff',
        'circle-opacity': 1,
        'circle-stroke-opacity': 1,
      }
    });
  }

  private formatCategory(raw: string): string {
    const map: Record<string, string> = {
      restaurant: 'Restaurant',
      cafe: 'Café',
      hotel: 'Hotel',
      museum: 'Museum',
      attraction: 'Attraction',
      shop: 'Shop',
      bar: 'Bar',
      park: 'Park',
    };
    return map[raw.toLowerCase()] ?? raw.charAt(0).toUpperCase() + raw.slice(1);
  }

  selectCategory(cat: { id: string; label: string }): void {
    this.activeCategory = cat.id;
    this.selectedPlace = null;
    sessionStorage.removeItem('mapFilters');
    this.hasActiveFilters = false;
    if (this.map.isStyleLoaded()) {
      this.applyFilter(cat.id);
    }
  }
  private applyFilter(categoryId: string): void {
    const poiLayers = ['poi_z14', 'poi_z15', 'poi_z16', 'poi_transit'];

    const categoryMap: Record<string, string[]> = {
      all:         [],
      restaurants: ['restaurant', 'cafe', 'fast_food', 'bar', 'pub'],
      hotels:      ['hotel', 'hostel', 'motel', 'guest_house', 'lodging'],
      attractions: ['attraction', 'museum', 'monument', 'gallery', 'viewpoint', 'tourism'],
      events:      ['theatre', 'cinema', 'events_venue', 'nightclub'],
      shopping:    ['shop', 'supermarket', 'mall', 'convenience', 'clothes', 'grocery'],
    };

    poiLayers.forEach(layerId => {
      if (!this.map.getLayer(layerId)) return;

      if (categoryId === 'all') {
        this.map.setFilter(layerId, null);
      } else {
        const classes = categoryMap[categoryId] ?? [];
        this.map.setFilter(layerId, ['in', ['get', 'class'], ['literal', classes]]);
      }
    });
  }

  private applyMultipleFilters(categoryIds: string[]): void {
    const poiLayers = ['poi_z14', 'poi_z15', 'poi_z16', 'poi_transit'];

    const categoryMap: Record<string, string[]> = {
      restaurants: ['restaurant', 'cafe', 'fast_food', 'bar', 'pub'],
      hotels:      ['hotel', 'hostel', 'motel', 'guest_house', 'lodging'],
      attractions: ['attraction', 'museum', 'monument', 'gallery', 'viewpoint', 'tourism'],
      events:      ['theatre', 'cinema', 'events_venue', 'nightclub'],
      shopping:    ['shop', 'supermarket', 'mall', 'convenience', 'clothes', 'grocery'],
      cafes:       ['cafe', 'coffee'],
      bars:        ['bar', 'pub'],
      parks:       ['park', 'playground'],
      museums:     ['museum', 'gallery'],
      transport:   ['bus_stop', 'subway', 'train_station', 'tram_stop'],
      health:      ['hospital', 'pharmacy', 'clinic', 'doctor'],
      sports:      ['sports_centre', 'stadium', 'gym', 'fitness'],
    };

    const allClasses = categoryIds.flatMap(id => categoryMap[id] ?? []);
    const unique = [...new Set(allClasses)];

    poiLayers.forEach(layerId => {
      if (!this.map.getLayer(layerId)) return;
      if (unique.length === 0) {
        this.map.setFilter(layerId, null);
      } else {
        this.map.setFilter(layerId, ['in', ['get', 'class'], ['literal', unique]]);
      }
    });
  }

  locateMe(): void {
    if (!navigator.geolocation || this.isLocating) return;
    this.isLocating = true;
    this.cdr.detectChanges();
    navigator.geolocation.getCurrentPosition(
      ({ coords }) => {
        const lat = coords.latitude;
        const lon = coords.longitude;
 
        this.map.flyTo({ center: [lon, lat], zoom: 15, duration: 1200 });
        if (this.map.isStyleLoaded()) {
          this.placeUserMarker(lat, lon);
        } else {
          this.map.once('load', () => this.placeUserMarker(lat, lon));
        }

        this.isUserLocation = true;
        sessionStorage.setItem('mapLocation', JSON.stringify({
          name: 'My Location',
          lat, lon,
          isUserLocation: true,
        }));
 
        this.isLocating = false;
        this.cdr.detectChanges();
      },
      (err) => {
        console.warn('Location unavailable:', err.code, err.message);
        this.isLocating = false;
        this.cdr.detectChanges();
      },
      {
        timeout: 30000,
        maximumAge: 60000,
        enableHighAccuracy: false,
      }
    );
  }
    
  zoomIn():  void { this.map.zoomIn({ duration: 300 }); }
  zoomOut(): void { this.map.zoomOut({ duration: 300 }); }
  goBack():  void { this.router.navigate(['/home']); }

  onSearchInput(value: string): void {
    clearTimeout(this.searchTimeout);
    if (value.trim().length < 2) {
      this.searchResults = [];
      return;
    }
    this.searchTimeout = setTimeout(() => this.geocode(value), 250);
  }

  private geocode(query: string): void {
    const url = `https://nominatim.openstreetmap.org/search` +
      `?format=json&q=${encodeURIComponent(query)}&limit=5&accept-language=en`;
    fetch(url)
      .then(r => r.json())
      .then(data => {
        this.searchResults = data;
        this.cdr.detectChanges();
      })
      .catch(() => this.searchResults = []);
  }

  selectSearchResult(result: any): void {
    const lat = parseFloat(result.lat);
    const lon = parseFloat(result.lon);
    const name = result.display_name?.split(',')[0] ?? '';

    this.searchResults = [];
    this.searchQuery = name;
    this.destinationName = name;
    this.destinationLat = lat;
    this.destinationLon = lon;
    this.selectedPlace = null;

    sessionStorage.setItem('mapLocation', JSON.stringify({ name, lat, lon }));
    this.map.flyTo({ center: [lon, lat], zoom: 14, duration: 1200 });
    this.hasActiveFilters = !!sessionStorage.getItem('mapFilters');
  }
  get isMoreActive(): boolean {
    return this.hasActiveFilters;
  }
}