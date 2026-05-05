import { Component, OnInit, OnDestroy, NgZone, ChangeDetectorRef } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';

declare const L: any;

interface MockPlace {
  id: string;
  name: string;
  type: 'Place' | 'Location' | 'Destination';
  category: string;
  description: string;
  status: string;
  eventsToday: number;
  lat: number;
  lng: number;
  zone?: [number, number][];
}

@Component({
  selector: 'app-map-page',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './map-page.component.html',
})
export class MapPageComponent implements OnInit, OnDestroy {
  constructor(private zone: NgZone, private cdr: ChangeDetectorRef, private router: Router) {}

  region   = 'Šumadija, Srbija';
  userName = 'Sarah Jenkins';
  userRole = 'Regional Manager';

  searchQuery = '';
  searchResults: MockPlace[] = [];
  searchDropdownOpen = false;

  private map: any = null;
  private markers: any[] = [];
  private zonePolygons: any[] = [];
  private zoneLabels: any[] = [];

  selectedPlace: MockPlace | null = null;
  popupPosition: { x: number; y: number } | null = null;

  droppedPin: { lat: number; lng: number; address: string } | null = null;
  droppedPinMarker: any = null;
  droppedPinPosition: { x: number; y: number } | null = null;

  readonly mockPlaces: MockPlace[] = [
    {
      id: '1', name: 'Šumarice Memorial Park', type: 'Location', category: 'Park & Nature',
      description: 'Memorial park dedicated to victims of the Kragujevac massacre in 1941.',
      status: 'Active', eventsToday: 1, lat: 44.0213, lng: 20.8943,
      zone: [[44.0250, 20.8890], [44.0270, 20.8980], [44.0240, 20.9020], [44.0200, 20.9010], [44.0185, 20.8950], [44.0200, 20.8890]],
    },
    {
      id: '2', name: 'Knjazevsko-srpski teatar', type: 'Place', category: 'Culture & Arts',
      description: 'One of the oldest theatres in Serbia, founded in 1835.',
      status: 'Active', eventsToday: 2, lat: 44.0106, lng: 20.9103,
    },
    {
      id: '3', name: 'Sumadija sajam', type: 'Place', category: 'Community Hub',
      description: 'A mixed-use facility serving as an incubator for local tech startups and events.',
      status: 'Active', eventsToday: 3, lat: 44.0248, lng: 20.9305,
    },
    {
      id: '4', name: 'Bubanj Lake', type: 'Location', category: 'Nature & Leisure',
      description: 'Popular recreational lake area perfect for walks and outdoor activities.',
      status: 'Active', eventsToday: 0, lat: 44.0169, lng: 20.9258,
      zone: [[44.0185, 20.9220], [44.0190, 20.9310], [44.0155, 20.9315], [44.0148, 20.9225]],
    },
    {
      id: '5', name: 'Stadion Cika Daca', type: 'Place', category: 'Sports',
      description: 'Home stadium of FK Radnički Kragujevac.',
      status: 'Active', eventsToday: 1, lat: 44.0212, lng: 20.8991,
    },
    {
      id: '6', name: 'Kragujevac Old Town', type: 'Destination', category: 'Historic Quarter',
      description: 'The historic heart of Kragujevac with cultural landmarks and old architecture.',
      status: 'Active', eventsToday: 0, lat: 44.0128, lng: 20.9142,
      zone: [[44.0135, 20.9090], [44.0140, 20.9200], [44.0100, 20.9220], [44.0085, 20.9130], [44.0110, 20.9080]],
    },
    {
      id: '7', name: 'Hala Jezero', type: 'Place', category: 'Sports & Events',
      description: 'Multi-purpose sports and events hall.',
      status: 'Active', eventsToday: 2, lat: 44.0139, lng: 20.9241,
    },
  ];

  ngOnInit(): void {
    setTimeout(() => this.initMap(), 100);
  }

  ngOnDestroy(): void {
    if (this.map) { this.map.remove(); this.map = null; }
  }

  // Search

  onSearchInput(): void {
    const q = this.searchQuery.trim().toLowerCase();
    if (q.length < 1) {
      this.searchResults = [];
      this.searchDropdownOpen = false;
      return;
    }
    this.searchResults = this.mockPlaces.filter(p =>
      p.name.toLowerCase().includes(q) ||
      p.category.toLowerCase().includes(q) ||
      p.type.toLowerCase().includes(q)
    );
    this.searchDropdownOpen = true;
  }

  onSearchFocus(): void {
    if (this.searchQuery.trim().length > 0) {
      this.searchDropdownOpen = true;
    }
  }

  closeSearchDropdown(): void {
    setTimeout(() => { this.searchDropdownOpen = false; }, 150);
  }

  selectSearchResult(place: MockPlace): void {
    this.searchDropdownOpen = false;
    this.searchQuery = place.name;

    if (!this.map) return;

    // Fly to place
    this.map.flyTo([place.lat, place.lng], 15, { duration: 1 });

    // Show popup after fly
    setTimeout(() => {
      this.zone.run(() => {
        this.selectedPlace = place;
        this.droppedPin = null;
        this.droppedPinPosition = null;
        const point = this.map.latLngToContainerPoint([place.lat, place.lng]);
        const mapEl = document.getElementById('main-map')!.getBoundingClientRect();
        this.popupPosition = {
          x: point.x + mapEl.left,
          y: point.y + mapEl.top - 36,
        };
        this.cdr.detectChanges();
      });
    }, 1100);
  }

  doSearch(): void {
    const q = this.searchQuery.trim().toLowerCase();
    if (!q) return;
    const found = this.mockPlaces.find(p => p.name.toLowerCase().includes(q));
    if (found) this.selectSearchResult(found);
  }

  typeIcon(type: string): string {
    if (type === 'Location')    return '🔵';
    if (type === 'Destination') return '🟣';
    return '📍';
  }

  // Icons & Colors

  private makePinIcon(color: string = '#2563eb'): any {
    return L.divIcon({
      html: `<svg width="24" height="32" viewBox="0 0 24 32" xmlns="http://www.w3.org/2000/svg">
               <path d="M12 0C5.373 0 0 5.373 0 12c0 8 12 20 12 20S24 20 24 12C24 5.373 18.627 0 12 0z"
                     fill="${color}" stroke="white" stroke-width="1.5"/>
               <circle cx="12" cy="12" r="4" fill="white"/>
             </svg>`,
      iconSize: [24, 32], iconAnchor: [12, 32], className: '',
    });
  }

  private makeDroppedPinIcon(): any {
    return L.divIcon({
      html: `<svg width="28" height="36" viewBox="0 0 24 32" xmlns="http://www.w3.org/2000/svg">
               <path d="M12 0C5.373 0 0 5.373 0 12c0 8 12 20 12 20S24 20 24 12C24 5.373 18.627 0 12 0z"
                     fill="#f97316" stroke="white" stroke-width="1.5"/>
               <circle cx="12" cy="12" r="4" fill="white"/>
             </svg>`,
      iconSize: [28, 36], iconAnchor: [14, 36], className: '',
    });
  }

  private pinColor(type: string): string {
    if (type === 'Location')    return '#0891b2';
    if (type === 'Destination') return '#7c3aed';
    return '#2563eb';
  }

  private zoneColor(type: string): { color: string; fill: string } {
    if (type === 'Location')    return { color: '#0891b2', fill: '#06b6d4' };
    if (type === 'Destination') return { color: '#7c3aed', fill: '#a855f7' };
    return { color: '#2563eb', fill: '#3b82f6' };
  }

  // Map Init

  private initMap(): void {
    const el = document.getElementById('main-map');
    if (!el || this.map) return;

    this.map = L.map(el, {
      zoomControl: false,
      attributionControl: false,
    }).setView([44.013, 20.910], 13);

    L.tileLayer('https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png', {
      maxZoom: 19,
    }).addTo(this.map);

    L.control.zoom({ position: 'bottomright' }).addTo(this.map);

    this.mockPlaces.forEach(place => {
      if (!place.zone) return;
      const { color, fill } = this.zoneColor(place.type);

      const polygon = L.polygon(place.zone, {
        color,
        fillColor: fill,
        fillOpacity: 0.15,
        weight: 2,
        dashArray: '6 4',
        opacity: 0.8,
      }).addTo(this.map);

      polygon.on('click', (e: any) => {
        L.DomEvent.stopPropagation(e);
        this.zone.run(() => {
          this.selectedPlace = place;
          this.droppedPin = null;
          this.droppedPinPosition = null;
          const point = this.map.latLngToContainerPoint([place.lat, place.lng]);
          const mapEl = document.getElementById('main-map')!.getBoundingClientRect();
          this.popupPosition = {
            x: point.x + mapEl.left,
            y: point.y + mapEl.top - 36,
          };
          this.cdr.detectChanges();
        });
      });

      // Zone label
      const bounds = polygon.getBounds();
      const center = bounds.getCenter();
      const label = L.divIcon({
        html: `<div style="
          background: rgba(0,0,0,0.55);
          color: white;
          font-size: 10px;
          font-weight: 700;
          letter-spacing: 0.05em;
          padding: 2px 7px;
          border-radius: 20px;
          border: 1px solid ${color};
          white-space: nowrap;
          backdrop-filter: blur(4px);
          pointer-events: none;
        ">${place.name}</div>`,
        className: '',
        iconAnchor: [0, 0],
      });

      const labelMarker = L.marker(center, { icon: label, interactive: false }).addTo(this.map);
      this.zoneLabels.push(labelMarker);
      this.zonePolygons.push(polygon);
    });

    this.mockPlaces.forEach(place => {
      const marker = L.marker([place.lat, place.lng], {
        icon: this.makePinIcon(this.pinColor(place.type)),
      }).addTo(this.map);

      marker.on('click', (e: any) => {
        L.DomEvent.stopPropagation(e);
        this.zone.run(() => {
          this.selectedPlace = place;
          this.droppedPin = null;
          this.droppedPinPosition = null;
          const point = this.map.latLngToContainerPoint([place.lat, place.lng]);
          const mapEl = document.getElementById('main-map')!.getBoundingClientRect();
          this.popupPosition = {
            x: point.x + mapEl.left,
            y: point.y + mapEl.top - 36,
          };
          this.cdr.detectChanges();
        });
      });

      this.markers.push(marker);
    });

    this.map.on('click', (e: any) => {
      this.zone.run(() => {
        this.selectedPlace = null;
        this.popupPosition = null;

        const lat = e.latlng.lat;
        const lng = e.latlng.lng;

        if (this.droppedPinMarker) this.map.removeLayer(this.droppedPinMarker);
        this.droppedPinMarker = L.marker([lat, lng], {
          icon: this.makeDroppedPinIcon(),
        }).addTo(this.map);

        this.droppedPin = { lat, lng, address: `${lat.toFixed(4)}° N, ${lng.toFixed(4)}° W` };

        const point = this.map.latLngToContainerPoint([lat, lng]);
        const mapEl = document.getElementById('main-map')!.getBoundingClientRect();
        this.droppedPinPosition = {
          x: point.x + mapEl.left,
          y: point.y + mapEl.top - 36,
        };

        this.cdr.detectChanges();
      });
    });
  }

  // Popup / Navigation

  closePopup(): void {
    this.selectedPlace = null;
    this.popupPosition = null;
  }

  cancelDroppedPin(): void {
    this.droppedPin = null;
    this.droppedPinPosition = null;
    if (this.droppedPinMarker) {
      this.map.removeLayer(this.droppedPinMarker);
      this.droppedPinMarker = null;
    }
  }

  navigate(path: string): void {
    this.router.navigate([path]);
  }

  typeColor(type: string): string {
    if (type === 'Location')    return 'bg-cyan-900/60 text-cyan-300 border-cyan-700';
    if (type === 'Destination') return 'bg-purple-900/60 text-purple-300 border-purple-700';
    return 'bg-blue-900/60 text-blue-300 border-blue-700';
  }

  editPlace(place: MockPlace): void {
    if (place.type === 'Location') {
      this.router.navigate(['/locations/new'], { state: { place } });
    } else if (place.type === 'Destination') {
      this.router.navigate(['/destinations/new'], { state: { place } });
    } else {
      this.router.navigate(['/places/edit', place.id], { state: { place } });
    }
    this.closePopup();
  }

  navigateToNewEvent(): void {
    this.router.navigate(['/events/new'], { state: { place: this.selectedPlace } });
    this.closePopup();
  }
}