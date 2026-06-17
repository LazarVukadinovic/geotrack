import type { ChangeDetectorRef } from '@angular/core';
import type { NgZone } from '@angular/core';
import type { Router } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import type { AnalyticsService } from '../../../../core/services/analytics.service';
import type { DestinationService } from '../../../../core/services/destination.service';
import type { LocationService } from '../../../../core/services/location.service';
import type { MapApiService } from '../../../../core/services/map-api.service';
import type { MapFilterStateService } from '../../services/map-filter-state.service';
import { MapPageComponent } from './map-page.component';

describe('MapPageComponent', () => {
  beforeEach(() => {
    sessionStorage.clear();
    history.replaceState({}, '', '/map');
  });

  it('restores the saved map location from session storage', () => {
    sessionStorage.setItem('mapLocation', JSON.stringify({
      name: 'Dubrovnik',
      lat: 42.6507,
      lon: 18.0944,
      isUserLocation: false,
    }));

    const component = createComponent();

    expect(component.destinationName).toBe('Dubrovnik');
    expect(component.destinationLat).toBe(42.6507);
    expect(component.destinationLon).toBe(18.0944);
    expect(component.isUserLocation).toBe(false);

    component.ngOnDestroy();
  });

  it('marks the More filter control active when persisted filters exist', () => {
    const component = createComponent({
      categories: ['place-type:7'],
      categoryLabelIds: ['tag:12'],
    });

    expect(component.hasActiveFilters).toBe(true);
    expect(component.isMoreActive).toBe(true);

    component.ngOnDestroy();
  });

  it('loads and filters all backend-provided destinations for the no-data modal', () => {
    const mapApi = createMapApi({
      getDestinations: vi.fn(() => of([
        {
          id: 1,
          type: 'destination',
          name: 'Dubrovnik',
          subtitle: 'Dubrovnik-Neretva',
          latitude: 42.6507,
          longitude: 18.0944,
        },
        {
          id: 2,
          type: 'destination',
          name: 'Kragujevac',
          subtitle: 'Sumadija District',
          latitude: 44.0106,
          longitude: 20.9153,
        },
      ])),
    });
    const component = createComponent(undefined, mapApi);

    (component as any).loadDestinationsIfNeeded();

    expect(mapApi.getDestinations).toHaveBeenCalledTimes(1);
    expect(component.allDestinations.map(destination => destination.name)).toEqual(['Dubrovnik', 'Kragujevac']);

    component.noDataSearchQuery = 'sumadija';
    expect(component.filteredDestinations.map(destination => destination.name)).toEqual(['Kragujevac']);

    component.ngOnDestroy();
  });

  it('keeps API errors separate from empty destination results', () => {
    const mapApi = createMapApi({
      getDestinations: vi.fn(() => throwError(() => new Error('offline'))),
    });
    const component = createComponent(undefined, mapApi);

    (component as any).loadDestinationsIfNeeded();

    expect(component.destinationLoadError).toBe('Destinations are unavailable right now.');
    expect(component.noDataHeading).toBe("GeoTrack doesn't have content for this area yet.");

    component.ngOnDestroy();
  });

  it('uses explicit copy when the backend has no public destinations', () => {
    const mapApi = createMapApi({
      getDestinations: vi.fn(() => of([])),
    });
    const component = createComponent(undefined, mapApi);

    (component as any).loadDestinationsIfNeeded();

    expect(component.allDestinations).toEqual([]);
    expect(component.noDataHeading).toBe('GeoTrack does not have public destinations available yet.');

    component.ngOnDestroy();
  });

  it('cancels stale searches so an older response cannot replace newer results', () => {
    vi.useFakeTimers();
    const firstSearch = new Subject<any[]>();
    const secondSearch = new Subject<any[]>();
    const mapApi = createMapApi({
      search: vi.fn((query: string) => query === 'be' ? firstSearch : secondSearch),
    });
    const component = createComponent(undefined, mapApi);

    component.onSearchInput('be');
    vi.advanceTimersByTime(150);
    component.onSearchInput('bel');
    vi.advanceTimersByTime(150);

    firstSearch.next([{
      id: 1,
      type: 'destination',
      name: 'Old result',
      latitude: 1,
      longitude: 1,
    }]);
    expect(component.searchResults).toEqual([]);

    secondSearch.next([{
      id: 2,
      type: 'destination',
      name: 'Belgrade',
      latitude: 44.8,
      longitude: 20.4,
    }]);
    expect(component.searchResults.map(result => result.name)).toEqual(['Belgrade']);
    expect(component.isSearching).toBe(false);

    component.ngOnDestroy();
    vi.useRealTimers();
  });


  it('keeps a stable quick filter row in the preferred place-type order', () => {
    const component = createComponent();
    component.categories = [
      { id: 'all', label: 'All' },
      { id: 'place-type:5', label: 'Museum' },
      { id: 'place-type:4', label: 'Landmark' },
      { id: 'place-type:3', label: 'Cafe' },
      { id: 'place-type:2', label: 'Restaurant' },
      { id: 'place-type:1', label: 'Stay' },
    ];

    expect(component.primaryCategories.map(category => category.label)).toEqual([
      'All',
      'Stay',
      'Restaurant',
      'Cafe',
      'Landmark',
    ]);
    expect(component.morePlaceTypeCategories).toHaveLength(5);

    component.ngOnDestroy();
  });

  it('replaces More tag filters when a quick place type is selected', () => {
    const component = createComponent({
      categories: ['place-type:1'],
      categoryLabelIds: ['tag:12', 'tag:13'],
    });

    component.selectCategory({ id: 'place-type:2', label: 'Restaurant' });

    expect(component.activeCategory).toBe('place-type:2');
    expect((component as any).filterCategories).toEqual(['place-type:2']);
    expect((component as any).selectedCategoryLabelFilters).toEqual([]);

    component.ngOnDestroy();
  });

  it('caches the same padded viewport that was actually requested', () => {
    const component = createComponent();
    const internals = component as any;
    const visibleBounds = { north: 45, south: 44, east: 21, west: 20 };
    const requestedBounds = internals.expandViewportBounds(visibleBounds, 0.1);

    internals.lastFetchedViewport = { ...requestedBounds, mode: 'entities' };

    expect(internals.isWithinCachedViewport(visibleBounds, 'entities')).toBe(true);
    expect(internals.isWithinCachedViewport({
      ...visibleBounds,
      east: requestedBounds.east + 0.01,
    }, 'entities')).toBe(false);

    component.ngOnDestroy();
  });

  it('keeps destination mode available when zoomed out even if place filters are selected', () => {
    const mapApi = createMapApi({
      getMarkersInViewport: vi.fn(() => of({ mode: 'destinations' as const, markers: [] })),
    });
    const component = createComponent({ categories: ['place-type:7'], categoryLabelIds: [] }, mapApi);
    const internals = component as any;
    internals.map = {
      getZoom: vi.fn(() => 8),
      getBounds: vi.fn(() => ({
        getNorth: () => 45,
        getSouth: () => 43,
        getEast: () => 22,
        getWest: () => 19,
      })),
    };

    internals.loadMarkersForViewport();

    expect(mapApi.getMarkersInViewport).toHaveBeenCalledWith(
      expect.any(Object),
      8,
      [],
    );
    expect(component.currentMode).toBe('destinations');

    internals.map = undefined;
    component.ngOnDestroy();
  });

  it('normalizes and caches viewports that cross the international date line', () => {
    const component = createComponent();
    const internals = component as any;
    const visibleBounds = internals.normalizeViewportBounds({
      north: 20,
      south: 10,
      west: 170,
      east: 190,
    });
    const requestedBounds = internals.expandViewportBounds(visibleBounds, 0.1);

    expect(visibleBounds).toEqual({ north: 20, south: 10, west: 170, east: -170 });
    internals.lastFetchedViewport = { ...requestedBounds, mode: 'destinations' };
    expect(internals.isWithinCachedViewport(visibleBounds, 'destinations')).toBe(true);

    component.ngOnDestroy();
  });
});

function createComponent(
  savedFilters = { categories: [] as string[], categoryLabelIds: [] as string[] },
  mapApi = createMapApi(),
): MapPageComponent {
  return new MapPageComponent(
    {
      getCurrentNavigation: vi.fn(() => null),
      navigate: vi.fn(),
      navigateByUrl: vi.fn(),
      url: '/map',
    } as unknown as Router,
    { detectChanges: vi.fn() } as unknown as ChangeDetectorRef,
    { run: (fn: () => unknown) => fn() } as unknown as NgZone,
    mapApi,
    { track: vi.fn() } as unknown as AnalyticsService,
    {
      stopMapLocationUpdates: vi.fn(),
      location: vi.fn(() => null),
      setGpsLocation: vi.fn(),
      startMapLocationUpdates: vi.fn(),
      resolveRoutingOrigin: vi.fn(() => Promise.resolve(null)),
      ensureIpFallback: vi.fn(() => Promise.resolve(null)),
    } as unknown as LocationService,
    {
      readSelection: vi.fn(() => savedFilters),
      clearSelection: vi.fn(),
      toggleCategoryLabelFilter: vi.fn((ids: string[]) => ids),
      persistSelection: vi.fn(),
      labelsForCategoryLabelIds: vi.fn(() => []),
      rebuildAvailablePlaceTypes: vi.fn(() => []),
      setRecommendedTagFilters: vi.fn(() => []),
      filterMarkers: vi.fn((markers) => markers),
      availablePlaceTypes: vi.fn(() => []),
      availableMoreFilters: vi.fn(() => []),
    } as unknown as MapFilterStateService,
    {
      isDestinationMode: vi.fn(() => false),
      latitude: vi.fn(() => null),
      longitude: vi.fn(() => null),
      name: vi.fn(() => ''),
      setDestinationOverride: vi.fn(),
      useLocationMode: vi.fn(),
    } as unknown as DestinationService,
  );
}

function createMapApi(overrides: Partial<MapApiService> = {}): MapApiService {
  return {
    getDestinations: vi.fn(() => of([])),
    search: vi.fn(() => of([])),
    getMarkersInViewport: vi.fn(() => of({ mode: 'entities', markers: [] })),
    getWalkingRoute: vi.fn(() => of({ distanceMeters: 0, durationSeconds: 0, coordinates: [] })),
    getDrivingRoute: vi.fn(() => of({ distanceMeters: 0, durationSeconds: 0, coordinates: [] })),
    getEntities: vi.fn(() => of({ markers: [] })),
    reverseGeocode: vi.fn(() => of({
      address: '',
      city: '',
      region: '',
      country: '',
      displayLabel: '',
    })),
    ...overrides,
  } as unknown as MapApiService;
}
