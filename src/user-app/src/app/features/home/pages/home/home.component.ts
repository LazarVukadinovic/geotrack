import { ChangeDetectorRef, Component, OnDestroy, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import maplibregl, { Map } from 'maplibre-gl';
import { DestinationService } from '../../../../core/services/destination.service';
import { LocationService } from '../../../../core/services/location.service';
import { MapApiService, MapSearchResult } from '../../../../core/services/map-api.service';
import { AnalyticsService } from '../../../../core/services/analytics.service';
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
  private readonly destinationSvc = inject(DestinationService);
  private readonly locationSvc = inject(LocationService);
  private readonly mapApi = inject(MapApiService);
  private readonly analytics = inject(AnalyticsService);
  private readonly exploreSvc = inject(ExploreService);
  private readonly cdr = inject(ChangeDetectorRef);

  private bgMap?: Map;
  private searchTimeout?: ReturnType<typeof setTimeout>;
  private resizeObserver?: ResizeObserver;
  private animationFrameId: number | null = null;
  private isDestroyed = false;

  searchQuery = '';
  searchResults: MapSearchResult[] = [];
  isLocating = false;

  ngOnInit(): void {
    this.preloadMapExperience();
    this.initBgMap();
  }

  ngOnDestroy(): void {
    this.isDestroyed = true;

    if (this.searchTimeout) {
      clearTimeout(this.searchTimeout);
    }

    if (this.animationFrameId !== null) {
      cancelAnimationFrame(this.animationFrameId);
    }

    this.resizeObserver?.disconnect();
    this.bgMap?.remove();
  }

  private initBgMap(): void {
    const container = document.getElementById('bg-map');
    if (!container) return;

    this.bgMap = new maplibregl.Map({
      container,
      style: MAP_STYLE_URL,
      center: [14.0, 20.0],
      zoom: 1.8,
      pitch: 30,
      bearing: 0,
      interactive: false,
      attributionControl: false,
      fadeDuration: 0,
      trackResize: true,
    });

    this.resizeObserver = new ResizeObserver(() => {
      this.bgMap?.resize();
    });
    this.resizeObserver.observe(container);

    this.startCinematicLoop();

    this.bgMap.on('style.load', () => {
      this.bgMap?.resize();
      requestAnimationFrame(() => this.bgMap?.resize());

      this.bgMap?.getStyle().layers?.forEach(layer => {
        if (layer.type === 'symbol') {
          this.bgMap?.setLayoutProperty(layer.id, 'visibility', 'none');
        }
      });
    });
  }

  private preloadMapExperience(): void {
    void import('../../../map/pages/map-page/map-page.component');
    void fetch(MAP_STYLE_URL, { cache: 'force-cache' }).catch(() => {});
  }

  private startCinematicLoop(): void {
    let lng = 14.0;

    const animate = () => {
      if (this.isDestroyed || !this.bgMap) return;

      lng += 0.10;
      if (lng > 180) lng = -180;

      this.bgMap.setCenter([lng, 20.0]);
      this.animationFrameId = requestAnimationFrame(animate);
    };

    this.animationFrameId = requestAnimationFrame(animate);
  }

  /* Search / manual destination */

  onSearch(value: string): void {
    clearTimeout(this.searchTimeout);

    if (value.trim().length < 2) {
      this.searchResults = [];
      return;
    }

    this.searchTimeout = setTimeout(() => this.geocode(value), 150);
  }

  private geocode(query: string): void {
    this.mapApi.search(query, 'destination').subscribe({
      next: (results) => {
        this.searchResults = results;
      },
      error: () => {
        this.searchResults = [];
      },
    });
  }

  /**
   * Manual destination flow — no GPS permission needed.
   * Sets the destination in LocationService, preloads Explore data, then opens the map.
   */
  selectResult(result: MapSearchResult): void {
    const committedQuery = this.searchQuery.trim();
    const resultCount = this.searchResults.length;
    const selectedIndex = this.searchResults.findIndex((candidate) =>
      candidate.name === result.name &&
      candidate.latitude === result.latitude &&
      candidate.longitude === result.longitude);

    this.searchResults = [];

    const name = result.name;
    const lat = result.latitude;
    const lon = result.longitude;

    this.searchQuery = name;
    this.isLocating = true;

    this.locationSvc.setManualDestination(lat, lon, name);
    this.analytics.track('search_performed', {
      metadata: {
        surface: 'home_destination_entry',
        queryLength: committedQuery.length || name.length,
        resultCount,
        selectedIndex: selectedIndex >= 0 ? selectedIndex : null,
      },
    });

    if (result.type === 'destination' && typeof result.id === 'number' && result.id > 0) {
      this.destinationSvc.setDestinationOverride({
        id: result.id,
        name,
        latitude: lat,
        longitude: lon,
      });

      this.exploreSvc.loadForDestination(result.id).finally(() => {
        this.finishLocationFlow(() => this.router.navigate(['/map'], {
          replaceUrl: true,
          queryParams: { destinationId: result.id },
          state: {
            name,
            lat,
            lon,
            fromExplore: true,
          },
        }));
      });
      return;
    }

    this.exploreSvc.loadForLocation(lat, lon).then(() => {
      this.finishLocationFlow(() => this.router.navigate(['/map'], {
        replaceUrl: true,
        state: {
          name,
          lat,
          lon,
        },
      }));
    });
  }

  clearSearch(): void {
    this.searchQuery = '';
    this.searchResults = [];
  }

  getResultName(result: MapSearchResult): string {
    return result.name;
  }

  getResultSubtitle(result: MapSearchResult): string {
    return result.subtitle ?? '';
  }

  /* Use my current location */

  /**
   * GPS flow:
   * 1. Triggers browser permission popup via LocationService.requestGps().
   * 2. On success: preloads Explore data with real coords, marks entry complete, navigates.
   * 3. On denial: uses approximate location automatically, marks entry complete, navigates.
   */
  async requestLocation(): Promise<void> {
    this.isLocating = true;

    try {
      const coords = await this.locationSvc.requestGps();
      await this.exploreSvc.loadForLocation(coords.latitude, coords.longitude);

      this.finishLocationFlow(() => this.router.navigate(['/map'], {
        replaceUrl: true,
        state: {
          name: 'Current Location',
          lat: coords.latitude,
          lon: coords.longitude,
          isUserLocation: true,
        },
      }));
    } catch {
      const approximateLoc = await this.locationSvc.ensureIpFallback();

      if (approximateLoc) {
        await this.exploreSvc.loadForLocation(approximateLoc.latitude, approximateLoc.longitude);
        this.finishLocationFlow(() => this.router.navigate(['/map'], {
          replaceUrl: true,
          state: {
            name: 'Current Location',
            lat: approximateLoc.latitude,
            lon: approximateLoc.longitude,
            isUserLocation: true,
          },
        }));
        return;
      }

      this.finishLocationFlow();
    }
  }

  private finishLocationFlow(navigate?: () => Promise<boolean>): void {
    setTimeout(() => {
      if (this.isDestroyed) return;
      this.isLocating = false;
      this.locationSvc.markLocationEntryComplete();
      this.cdr.detectChanges();
      void navigate?.();
    }, 0);
  }
}
