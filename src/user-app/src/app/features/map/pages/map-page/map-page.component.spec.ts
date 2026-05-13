import type { ChangeDetectorRef } from '@angular/core';
import type { Location as AngularLocation } from '@angular/common';
import type { Router } from '@angular/router';
import { of } from 'rxjs';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import type { AnalyticsService } from '../../../../core/services/analytics.service';
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
});

function createComponent(savedFilters = { categories: [] as string[], categoryLabelIds: [] as string[] }): MapPageComponent {
  return new MapPageComponent(
    {
      getCurrentNavigation: vi.fn(() => null),
      navigate: vi.fn(),
      navigateByUrl: vi.fn(),
      url: '/map',
    } as unknown as Router,
    { detectChanges: vi.fn() } as unknown as ChangeDetectorRef,
    {
      search: vi.fn(() => of([])),
      getMarkersInViewport: vi.fn(() => of({ mode: 'entities', markers: [] })),
      getWalkingRoute: vi.fn(() => of({ distanceMeters: 0, durationSeconds: 0, coordinates: [] })),
    } as unknown as MapApiService,
    { track: vi.fn() } as unknown as AnalyticsService,
    {
      stopMapLocationUpdates: vi.fn(),
      location: vi.fn(() => null),
      setGpsLocation: vi.fn(),
      startMapLocationUpdates: vi.fn(),
      resolveRoutingOrigin: vi.fn(() => Promise.resolve(null)),
      ensureIpFallback: vi.fn(() => Promise.resolve(null)),
    } as unknown as LocationService,
    { replaceState: vi.fn() } as unknown as AngularLocation,
    {
      readSelection: vi.fn(() => savedFilters),
      clearSelection: vi.fn(),
      toggleCategoryLabelFilter: vi.fn(),
      persistSelection: vi.fn(),
      labelsForCategoryLabelIds: vi.fn(() => []),
      rebuildAvailableMoreFilters: vi.fn(() => []),
      filterMarkersByCategoryLabels: vi.fn((markers) => markers),
    } as unknown as MapFilterStateService,
  );
}
