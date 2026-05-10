import { Component, OnInit, OnDestroy, NgZone, ChangeDetectorRef } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { MapPlace } from '../../../../core/models/map-place.model';
import { MapPlacesService } from '../../../../core/services/map-places.service';
declare const L: any;


@Component({
  selector: 'app-map-page',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './map-page.component.html',
})
export class MapPageComponent implements OnInit, OnDestroy {
  constructor(private zone: NgZone, private cdr: ChangeDetectorRef, private router: Router, private mapPlacesService:MapPlacesService) {}

  region   = 'Šumadija, Srbija';
  userName = 'Sarah Jenkins';
  userRole = 'Regional Manager';

  searchQuery = '';
  searchResults: MapPlace[] = [];
  searchDropdownOpen = false;

  private map: any = null;
  private markers: any[] = [];
  private zonePolygons: any[] = [];
  private zoneLabels: any[] = [];

  selectedPlace: MapPlace | null = null;
  popupPosition: { x: number; y: number } | null = null;

  droppedPin: { lat: number; lng: number; address: string } | null = null;
  droppedPinMarker: any = null;
  droppedPinPosition: { x: number; y: number } | null = null;

  
  private dynamicPlaces: MapPlace[] = [];
  ngOnInit(): void {
      const state = history.state as { newPlace?: MapPlace };
      if (state?.newPlace) {
        this.dynamicPlaces = [state.newPlace];
      }
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
    this.searchResults = this.mapPlacesService.getAll().filter(p =>
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

  selectSearchResult(place: MapPlace): void {
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
    const found = this.mapPlacesService.getAll().find(p => p.name.toLowerCase().includes(q));
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

    L.tileLayer('https://tiles.stadiamaps.com/tiles/alidade_smooth/{z}/{x}/{y}{r}.png', {
      maxZoom: 19,
    }).addTo(this.map);

    L.control.zoom({ position: 'bottomright' }).addTo(this.map);

    this.mapPlacesService.getAll().forEach(place => {
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
        background: rgba(255,255,255,0.92);
        color: #1e3a5f;
        font-size: 10px;
        font-weight: 700;
        letter-spacing: 0.05em;
        padding: 2px 8px;
        border-radius: 20px;
        border: 1px solid ${color};
        white-space: nowrap;
        box-shadow: 0 1px 4px rgba(0,0,0,0.12);
        pointer-events: none;
      ">${place.name}</div>`,
        className: '',
        iconAnchor: [0, 0],
      });

      const labelMarker = L.marker(center, { icon: label, interactive: false }).addTo(this.map);
      this.zoneLabels.push(labelMarker);
      this.zonePolygons.push(polygon);
    });

    this.mapPlacesService.getAll().forEach(place => {
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
    this.dynamicPlaces.forEach(place => this._addPlaceToMap(place));

    if (this.dynamicPlaces.length > 0) {
      const p = this.dynamicPlaces[0];
      setTimeout(() => {
        this.map.flyTo([p.lat, p.lng], 15, { duration: 1 });
        setTimeout(() => {
          this.zone.run(() => {
            this.selectedPlace = p;
            const point = this.map.latLngToContainerPoint([p.lat, p.lng]);
            const mapEl = document.getElementById('main-map')!.getBoundingClientRect();
            this.popupPosition = {
              x: point.x + mapEl.left,
              y: point.y + mapEl.top - 36,
            };
            this.cdr.detectChanges();
          });
        }, 1100);
      }, 300);
    }
  }
  private _addPlaceToMap(place: MapPlace): void {
    if (place.zone) {
      const { color, fill } = this.zoneColor(place.type);
      const polygon = L.polygon(place.zone, {
        color, fillColor: fill, fillOpacity: 0.15, weight: 2, dashArray: '6 4', opacity: 0.8,
      }).addTo(this.map);

      polygon.on('click', (e: any) => {
        L.DomEvent.stopPropagation(e);
        this.zone.run(() => {
          this.selectedPlace = place;
          this.droppedPin = null;
          this.droppedPinPosition = null;
          const point = this.map.latLngToContainerPoint([place.lat, place.lng]);
          const mapEl = document.getElementById('main-map')!.getBoundingClientRect();
          this.popupPosition = { x: point.x + mapEl.left, y: point.y + mapEl.top - 36 };
          this.cdr.detectChanges();
        });
      });

      const bounds = polygon.getBounds();
      const center = bounds.getCenter();
      const { color: c } = this.zoneColor(place.type);
      const label = L.divIcon({
        html: `<div style="background:rgba(255,255,255,0.92);color:#1e3a5f;font-size:10px;font-weight:700;padding:2px 8px;border-radius:20px;border:1px solid ${c};white-space:nowrap;box-shadow:0 1px 4px rgba(0,0,0,0.12);pointer-events:none;">${place.name}</div>`,
        className: '', iconAnchor: [0, 0],
      });
      L.marker(center, { icon: label, interactive: false }).addTo(this.map);
      this.zonePolygons.push(polygon);
    }

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
        this.popupPosition = { x: point.x + mapEl.left, y: point.y + mapEl.top - 36 };
        this.cdr.detectChanges();
      });
    });

    this.markers.push(marker);
    this.mapPlacesService.addPlace(place);
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
    if (type === 'Location')    return 'bg-cyan-100 text-cyan-700 border-cyan-300';
    if (type === 'Destination') return 'bg-purple-100 text-purple-700 border-purple-300';
    return 'bg-blue-100 text-blue-700 border-blue-300';
  }

  editPlace(place: MapPlace): void {
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
    this.router.navigate(['/events/new'], { state: { place: this.selectedPlace, returnTo: '/map', } });
    this.closePopup();
  }
  navigateToNewPlaceWithPin(): void {
    if (!this.droppedPin) return;
    this.router.navigate(['/places/new'], {
      state: {
        droppedPin: {
          lat: this.droppedPin.lat,
          lng: this.droppedPin.lng,
        },
        returnTo: '/map',
      }
    });
    this.cancelDroppedPin();
  }
}