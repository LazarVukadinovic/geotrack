import { ChangeDetectorRef, Component, OnDestroy, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import maplibregl, { Map } from 'maplibre-gl';
import { Subscription } from 'rxjs';
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
  private searchSubscription?: Subscription;
  private searchRequestSeq = 0;
  private searchBlurTimeout?: ReturnType<typeof setTimeout>;
  private resizeObserver?: ResizeObserver;
  private animationFrameId: number | null = null;
  private isDestroyed = false;

  searchQuery = '';
  searchResults: MapSearchResult[] = [];
  allDestinations: MapSearchResult[] = [];
  isSearching = false;
  searchErrorMessage = '';
  isSearchFocused = false;
  isLoadingDestinations = false;
  destinationLoadError = '';
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

    if (this.searchBlurTimeout) {
      clearTimeout(this.searchBlurTimeout);
    }

    this.cancelActiveSearch();

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

      this.bgMap?.getStyle().layers?.forEach((layer) => {
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

      lng += 0.1;
      if (lng > 180) lng = -180;

      this.bgMap.setCenter([lng, 20.0]);
      this.animationFrameId = requestAnimationFrame(animate);
    };

    this.animationFrameId = requestAnimationFrame(animate);
  }

  /* Search / manual destination */

  onSearchFocus(): void {
    if (this.searchBlurTimeout) {
      clearTimeout(this.searchBlurTimeout);
      this.searchBlurTimeout = undefined;
    }

    this.isSearchFocused = true;
    this.loadDestinationsIfNeeded();
    this.cdr.detectChanges();
  }

  onSearchBlur(): void {
    this.searchBlurTimeout = setTimeout(() => {
      this.isSearchFocused = false;
      this.cdr.detectChanges();
    }, 200);
  }

  onSearchInput(value: string): void {
    this.searchQuery = value;
    this.isSearchFocused = true;

    if (this.searchTimeout) {
      clearTimeout(this.searchTimeout);
      this.searchTimeout = undefined;
    }

    this.cancelActiveSearch();
    this.searchErrorMessage = '';

    const trimmedQuery = value.trim();
    if (trimmedQuery.length < 2) {
      this.isSearching = false;
      this.searchResults = [];
      this.cdr.detectChanges();
      return;
    }

    this.isSearching = true;
    this.searchResults = [];
    const requestId = this.searchRequestSeq;

    this.searchTimeout = setTimeout(() => {
      this.searchTimeout = undefined;
      this.searchSubscription = this.mapApi.search(trimmedQuery).subscribe({
        next: (results) => {
          if (requestId !== this.searchRequestSeq || this.searchQuery.trim() !== trimmedQuery)
            return;
          this.searchResults = results;
          this.isSearching = false;
          this.searchErrorMessage = '';
          this.cdr.detectChanges();
        },
        error: () => {
          if (requestId !== this.searchRequestSeq || this.searchQuery.trim() !== trimmedQuery)
            return;
          this.searchResults = [];
          this.isSearching = false;
          this.searchErrorMessage = 'Search is unavailable right now. Please try again.';
          this.cdr.detectChanges();
        },
      });
    }, 150);

    this.cdr.detectChanges();
  }

  private loadDestinationsIfNeeded(force = false): void {
    if (!force && (this.allDestinations.length > 0 || this.isLoadingDestinations)) return;

    this.isLoadingDestinations = true;
    this.destinationLoadError = '';
    this.mapApi.getDestinations().subscribe({
      next: (results) => {
        this.allDestinations = results;
        this.isLoadingDestinations = false;
        this.destinationLoadError = '';
        this.cdr.detectChanges();
      },
      error: () => {
        this.isLoadingDestinations = false;
        this.destinationLoadError = 'Destinations are unavailable right now.';
        this.cdr.detectChanges();
      },
    });
  }

  retryLoadDestinations(): void {
    this.loadDestinationsIfNeeded(true);
  }

  /**
   * Opens the map at the selected destination, location, place, stay, or event.
   * Destination results also preload the destination Explore feed.
   */
  selectResult(result: MapSearchResult): void {
    if (this.searchTimeout) {
      clearTimeout(this.searchTimeout);
      this.searchTimeout = undefined;
    }
    this.cancelActiveSearch();

    const committedQuery = this.searchQuery.trim();
    const resultCount = this.searchResults.length;
    const selectedIndex = this.searchResults.findIndex(
      (candidate) =>
        candidate.name === result.name &&
        candidate.latitude === result.latitude &&
        candidate.longitude === result.longitude,
    );

    this.searchResults = [];
    this.isSearching = false;
    this.searchErrorMessage = '';
    this.isSearchFocused = false;

    const name = result.name;
    const lat = result.latitude;
    const lon = result.longitude;

    this.searchQuery = name;
    this.isLocating = true;

    this.locationSvc.setManualDestination(lat, lon, name);
    this.analytics.track('search_performed', {
      metadata: {
        surface: 'home',
        queryLength: committedQuery.length || name.length,
        resultCount,
        selectedIndex: selectedIndex >= 0 ? selectedIndex : null,
        resultType: result.type ?? null,
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
        this.finishLocationFlow(() =>
          this.router.navigate(['/map'], {
            replaceUrl: true,
            queryParams: { destinationId: result.id },
            state: {
              name,
              lat,
              lon,
              fromExplore: true,
            },
          }),
        );
      });
      return;
    }

    const mapFocus = this.toMapFocus(result);
    this.exploreSvc.loadForLocation(lat, lon).finally(() => {
      this.finishLocationFlow(() =>
        this.router.navigate(['/map'], {
          replaceUrl: true,
          state: {
            name,
            lat,
            lon,
            ...(mapFocus ? { mapFocus } : {}),
          },
        }),
      );
    });
  }

  clearSearch(): void {
    if (this.searchTimeout) {
      clearTimeout(this.searchTimeout);
      this.searchTimeout = undefined;
    }
    this.cancelActiveSearch();
    this.searchQuery = '';
    this.searchResults = [];
    this.isSearching = false;
    this.searchErrorMessage = '';
    this.cdr.detectChanges();
  }

  getResultSubtitle(result: MapSearchResult): string {
    return result.subtitle ?? result.parentName ?? '';
  }

  searchResultTrackKey(result: MapSearchResult, index: number): string {
    if (result.type && result.id) return `${result.type}:${result.id}`;
    return [result.type ?? 'unknown', result.name, result.latitude, result.longitude, index].join(
      ':',
    );
  }

  private cancelActiveSearch(): void {
    this.searchRequestSeq += 1;
    this.searchSubscription?.unsubscribe();
    this.searchSubscription = undefined;
  }

  private toMapFocus(result: MapSearchResult): Record<string, unknown> | null {
    if (!result.id || !result.type || !['place', 'stay', 'event'].includes(result.type)) {
      return null;
    }

    const entityType = result.type;
    return {
      entityType,
      entityId: result.id,
      title: result.name,
      subtitle: result.subtitle ?? result.parentName ?? null,
      categoryLabel: entityType === 'stay' ? 'Stay' : entityType === 'event' ? 'Event' : 'Place',
      imageUrl: null,
      detailRoute:
        entityType === 'stay'
          ? `/stays/${result.id}`
          : entityType === 'event'
            ? `/events/${result.id}`
            : `/places/${result.id}`,
      latitude: result.latitude,
      longitude: result.longitude,
      isSaved: false,
    };
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

      this.finishLocationFlow(() =>
        this.router.navigate(['/map'], {
          replaceUrl: true,
          state: {
            name: 'Current Location',
            lat: coords.latitude,
            lon: coords.longitude,
            isUserLocation: true,
          },
        }),
      );
    } catch {
      const approximateLoc = await this.locationSvc.ensureIpFallback();

      if (approximateLoc) {
        await this.exploreSvc.loadForLocation(approximateLoc.latitude, approximateLoc.longitude);
        this.finishLocationFlow(() =>
          this.router.navigate(['/map'], {
            replaceUrl: true,
            state: {
              name: 'Current Location',
              lat: approximateLoc.latitude,
              lon: approximateLoc.longitude,
              isUserLocation: true,
            },
          }),
        );
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
