import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import maplibregl, { Map } from 'maplibre-gl';
import { LocationService } from '../../../../core/services/location.service';
import { ExploreService } from '../../../explore/services/explore.service';

const MAP_STYLE_URL = '/map-style.json';

@Component({
  selector: 'app-home',
  imports: [FormsModule],
  templateUrl: './home.component.html',
  styleUrl: './home.component.css',
})
export class Home implements OnInit, OnDestroy {
  private readonly router = inject(Router);
  private readonly locationSvc = inject(LocationService);
  private readonly exploreSvc = inject(ExploreService);

  private bgMap!: Map;
  private searchTimeout?: ReturnType<typeof setTimeout>;

  searchQuery = '';
  searchResults: any[] = [];
  isLocating = false;

  ngOnInit(): void {
    // Returning users who already completed location entry → skip to Explore
    if (this.locationSvc.hasCompletedLocationEntry()) {
      this.router.navigate(['/explore'], { replaceUrl: true });
      return;
    }
    this.initBgMap();
  }

  ngOnDestroy(): void { this.bgMap?.remove(); }

  private initBgMap(): void {
    this.bgMap = new maplibregl.Map({
      container: 'bg-map',
      style: MAP_STYLE_URL,
      center: [14.0, 20.0],
      zoom: 1.8,
      pitch: 30,
      bearing: 0,
      interactive: false,
      attributionControl: false,
    });

    this.bgMap.on('load', () => {
      const logo = document.querySelector('.maplibregl-ctrl-logo') as HTMLElement;
      const attrib = document.querySelector('.maplibregl-ctrl-attrib') as HTMLElement;
      if (logo) logo.style.display = 'none';
      if (attrib) attrib.style.display = 'none';

      this.bgMap.getStyle().layers?.forEach(layer => {
        if (layer.type === 'symbol') {
          this.bgMap.setLayoutProperty(layer.id, 'visibility', 'none');
        }
      });
      this.startCinematicLoop();
    });
  }

  private startCinematicLoop(): void {
    let lng = 14.0;
    const animate = () => {
      lng += 0.10;
      if (lng > 180) lng = -180;
      this.bgMap.setCenter([lng, 20.0]);
      requestAnimationFrame(animate);
    };
    animate();
  }

  /* ── Search / manual destination ───────────────────────────────────── */

  onSearch(value: string): void {
    clearTimeout(this.searchTimeout);
    if (value.trim().length < 2) { this.searchResults = []; return; }
    this.searchTimeout = setTimeout(() => this.geocode(value), 150);
  }

  private geocode(query: string): void {
    fetch(`https://nominatim.openstreetmap.org/search?format=json&q=${encodeURIComponent(query)}&limit=5&accept-language=en&addressdetails=0&namedetails=0`)
      .then(r => r.json())
      .then(data => this.searchResults = data)
      .catch(() => this.searchResults = []);
  }

  /**
   * Manual destination flow — no GPS permission needed.
   * Sets the destination in LocationService, preloads Explore data, then navigates.
   */
  selectResult(result: any): void {
    this.searchResults = [];
    const name = this.getResultName(result);
    const lat = parseFloat(result.lat);
    const lon = parseFloat(result.lon);

    this.searchQuery = name;
    this.isLocating = true;

    // Set manual destination in LocationService (IP fallback handles general context)
    this.locationSvc.setManualDestination(lat, lon, name);

    // Preload explore data, then navigate
    this.exploreSvc.loadForLocation(lat, lon).then(() => {
      this.isLocating = false;
      this.locationSvc.markLocationEntryComplete();
      this.router.navigate(['/explore'], { replaceUrl: true });
    });
  }

  clearSearch(): void {
    this.searchQuery = '';
    this.searchResults = [];
  }

  getResultName(result: any): string {
    return result.display_name?.split(',')[0] ?? '';
  }

  getResultSubtitle(result: any): string {
    const parts = result.display_name?.split(',') ?? [];
    return parts.slice(1, 3).join(',').trim();
  }

  /* ── Use my current location ───────────────────────────────────────── */

  /**
   * GPS flow:
   * 1. Triggers browser permission popup via LocationService.requestGps()
   * 2. On success: preloads Explore data with real coords, marks entry complete, navigates
   * 3. On denial: falls back to IP-based location, marks entry complete, navigates
   */
  async requestLocation(): Promise<void> {
    this.isLocating = true;

    try {
      // Request GPS — triggers browser permission popup
      const coords = await this.locationSvc.requestGps();

      // Preload explore data with real GPS coordinates BEFORE navigating
      await this.exploreSvc.loadForLocation(coords.latitude, coords.longitude);

      this.isLocating = false;
      this.locationSvc.markLocationEntryComplete();
      this.router.navigate(['/explore'], { replaceUrl: true });

    } catch {
      // GPS denied or failed → fall back to IP-based location
      const ipLoc = await this.locationSvc.ensureIpFallback();

      this.isLocating = false;
      this.locationSvc.markLocationEntryComplete();

      if (ipLoc) {
        await this.exploreSvc.loadForLocation(ipLoc.latitude, ipLoc.longitude);
      }

      this.router.navigate(['/explore'], { replaceUrl: true });
    }
  }
}
