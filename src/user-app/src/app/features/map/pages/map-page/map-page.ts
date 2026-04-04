import { Component, OnInit, OnDestroy, AfterViewInit, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import maplibregl, { Map, LngLatLike } from 'maplibre-gl';

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
  private map!: Map;

  destinationName = 'Search Your Destination';
  destinationLat = 44.0165;
  destinationLon = 21.0059;

  isLoading = true;
  isLocating = false;
  activeTab = 'map';
  selectedPlace: Place | null = null;

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
    if (state?.name) {
      this.destinationName = state.name;
      this.destinationLat  = state.lat ?? 44.0165;
      this.destinationLon  = state.lon ?? 21.0059;
    }
  }

  ngAfterViewInit(): void {
    this.isLoading = false;
    this.cdr.detectChanges();
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
      this.map.on('styleimagemissing', (e) => {
        const imageData = {
          width: 1,
          height: 1,
          data: new Uint8Array([0, 0, 0, 0])
        };
        this.map.addImage(e.id, imageData);
      });
      this.applyFilter(this.activeCategory);
    });

    this.map.on('click', (e) => {
      const features = this.map.queryRenderedFeatures(e.point);

      const poi = features.find(f =>
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
      shopping:    ['shop', 'supermarket', 'mall', 'convenience', 'clothes'],
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

  locateMe(): void {
    if (!navigator.geolocation || this.isLocating) return;
    this.isLocating = true;
    navigator.geolocation.getCurrentPosition(
      ({ coords }) => {
        const center: LngLatLike = [coords.longitude, coords.latitude];
        this.map.flyTo({ center, zoom: 15, duration: 1200 });
        this.isLocating = false;
      },
      (err) => {
        console.warn('Location unavailable:', err.message);
        this.isLocating = false;
      },
      { timeout: 10000 }
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

    this.map.flyTo({ center: [lon, lat], zoom: 14, duration: 1200 });
  }
}