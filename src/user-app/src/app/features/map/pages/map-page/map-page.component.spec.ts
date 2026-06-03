import type { ChangeDetectorRef } from '@angular/core';
import type { NgZone } from '@angular/core';
import type { Router } from '@angular/router';
import { of, throwError } from 'rxjs';
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
      categories: ['places'],
      categoryLabelIds: ['coffee'],
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

    component.onSearchFocus();

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

    component.onSearchFocus();

    expect(component.destinationLoadError).toBe('Destinations are unavailable right now.');
    expect(component.noDataHeading).toBe("GeoTrack doesn't have content for this area yet.");

    component.ngOnDestroy();
  });

  it('uses explicit copy when the backend has no public destinations', () => {
    const mapApi = createMapApi({
      getDestinations: vi.fn(() => of([])),
    });
    const component = createComponent(undefined, mapApi);

    component.onSearchFocus();

    expect(component.allDestinations).toEqual([]);
    expect(component.noDataHeading).toBe('GeoTrack does not have public destinations available yet.');

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
      toggleCategoryLabelFilter: vi.fn(),
      persistSelection: vi.fn(),
      labelsForCategoryLabelIds: vi.fn(() => []),
      rebuildAvailableMoreFilters: vi.fn(() => []),
      filterMarkersByCategoryLabels: vi.fn((markers) => markers),
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
