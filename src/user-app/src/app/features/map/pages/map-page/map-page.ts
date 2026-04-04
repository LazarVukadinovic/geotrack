import { Component, OnInit, OnDestroy, AfterViewInit, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import maplibregl, { Map, Marker, LngLatLike } from 'maplibre-gl';

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
export class MapPage implements OnInit, AfterViewInit, OnDestroy {
  private map!: Map;
  private marker?: Marker;

  destinationName = 'Search Your Destination';
  destinationLat = 44.0165;
  destinationLon = 21.0059;

  isLoading = true;
  isLocating = false;
  activeTab = 'map';

  // Selected place for bottom popup — null means popup is hidden
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

  constructor(public router: Router, private cdr: ChangeDetectorRef) {
    const nav = this.router.getCurrentNavigation();
    const state = (nav?.extras?.state ?? history.state) as any;

    if (state?.name) {
      this.destinationName = state.name;
      this.destinationLat  = state.lat ?? 44.0165;
      this.destinationLon  = state.lon ?? 21.0059;
    }
  }

  ngOnInit(): void {}

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
      this.placeMarker([this.destinationLon, this.destinationLat], this.destinationName);
    });
    this.map.on('click', (e) => {
      // Otvori popup samo ako nije kliknut marker
      if (!(e.originalEvent.target as HTMLElement).classList.contains('custom-marker')) {
        this.selectedPlace = null;
        this.cdr.detectChanges();
      }
    });
  }

  // ── Marker ──

  private placeMarker(center: LngLatLike, label: string): void {
    this.marker?.remove();

    // Custom marker element so we can attach a click handler
    const el = document.createElement('div');
    el.className = 'custom-marker';
    el.style.cssText = `
      width: 36px; height: 36px;
      background: #1a2b4a;
      border-radius: 50% 50% 50% 0;
      transform: rotate(-45deg);
      border: 3px solid white;
      box-shadow: 0 2px 8px rgba(0,0,0,0.3);
      cursor: pointer;
    `;

    el.addEventListener('click', () => {
      this.selectedPlace = {
        name: label,
        category: this.getCategoryLabel(),
        rating: '4.5',
        distance: '0.2 mi',
        status: 'Open Now • Closes 10 PM',
      };
      this.cdr.detectChanges();
    });

    this.marker = new maplibregl.Marker({ element: el })
      .setLngLat(center)
      .addTo(this.map);
  }

  private getCategoryLabel(): string {
    if (this.activeCategory === 'all') {
      return 'Place';
    }
    const cat = this.categories.find(c => c.id === this.activeCategory);
    return cat ? cat.label : 'Place';
  }

  // ── Category selection ──

  selectCategory(cat: { id: string; label: string }): void {
    this.activeCategory = cat.id;
    this.selectedPlace = null;
  }

  // ── Geolocation ──

  locateMe(): void {
    if (!navigator.geolocation || this.isLocating) return;
    this.isLocating = true;

    navigator.geolocation.getCurrentPosition(
      ({ coords }) => {
        const center: LngLatLike = [coords.longitude, coords.latitude];
        this.placeMarker(center, 'Your location');
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

  // ── Zoom ──

  zoomIn():  void { this.map.zoomIn({ duration: 300 }); }
  zoomOut(): void { this.map.zoomOut({ duration: 300 }); }

  goBack(): void { this.router.navigate(['/home']); }

  searchQuery = '';
  searchResults: any[] = [];
  private searchTimeout?: ReturnType<typeof setTimeout>;

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

    this.map.flyTo({ center: [lon, lat], zoom: 14, duration: 1200 });
    setTimeout(() => this.placeMarker([lon, lat], name), 1300);
  }
}