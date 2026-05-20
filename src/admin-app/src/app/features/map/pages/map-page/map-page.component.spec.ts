import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { vi } from 'vitest';
import { MapPageComponent } from './map-page.component';
import { AdminMapResponse } from '../../../../core/models/admin-map.model';
import { RegionalAssignedRegion } from '../../../../core/models/dashboard.model';

describe('MapPageComponent', () => {
  let component: MapPageComponent;
  let fixture: ComponentFixture<MapPageComponent>;
  let http: HttpTestingController;

  beforeEach(async () => {
    sessionStorage.clear();

    await TestBed.configureTestingModule({
      imports: [MapPageComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(MapPageComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    sessionStorage.clear();
  });

  it('loads active assigned region map data from the backend', () => {
    fixture.detectChanges();
    flushRegions([{ id: 17, name: 'North', displayName: 'North Region', territoryName: 'Testland' }]);
    flushSidebar(17, 3, [{ id: 17, name: 'North', displayName: 'North Region', territoryName: 'Testland' }]);
    flushMap(17, mapResponse(17));

    expect(component.activeRegionId).toBe(17);
    expect(component.regionDisplayName).toBe('North Region');
    expect(component.pendingRequestsCount).toBe(3);
    expect(component.mapLoading).toBe(false);
    expect(component.mapData?.markers[0].title).toBe('Real Place');
    expect(component.mapData?.zones[0].entityType).toBe('Region');
    expect(sessionStorage.getItem('geotrack_admin_active_region_id')).toBe('17');
  });

  it('reloads markers and zones when the active region changes', () => {
    fixture.detectChanges();
    flushRegions([
      { id: 17, name: 'North', displayName: 'North Region', territoryName: 'Testland' },
      { id: 18, name: 'South', displayName: 'South Region', territoryName: 'Testland' },
    ]);
    flushSidebar(17, 2, [
      { id: 17, name: 'North', displayName: 'North Region', territoryName: 'Testland' },
      { id: 18, name: 'South', displayName: 'South Region', territoryName: 'Testland' },
    ]);
    flushMap(17, mapResponse(17));

    component.onRegionSelected(18);
    flushSidebar(18, 4, [
      { id: 17, name: 'North', displayName: 'North Region', territoryName: 'Testland' },
      { id: 18, name: 'South', displayName: 'South Region', territoryName: 'Testland' },
    ]);
    flushMap(18, mapResponse(18, 'Other Place'));

    expect(component.activeRegionId).toBe(18);
    expect(component.pendingRequestsCount).toBe(4);
    expect(component.mapData?.markers[0].title).toBe('Other Place');
    expect(sessionStorage.getItem('geotrack_admin_active_region_id')).toBe('18');
  });

  it('keeps the map empty when no assigned region exists', () => {
    fixture.detectChanges();
    flushRegions([]);

    expect(component.activeRegionId).toBeNull();
    expect(component.mapData).toBeNull();
    expect(component.mapLoading).toBe(false);
    expect(component.emptyMessage).toBe('No active assigned region.');
  });

  it('searches only inside the active assigned region', () => {
    fixture.detectChanges();
    flushRegions([{ id: 17, name: 'North', displayName: 'North Region', territoryName: 'Testland' }]);
    flushSidebar(17, 0, [{ id: 17, name: 'North', displayName: 'North Region', territoryName: 'Testland' }]);
    flushMap(17, mapResponse(17));

    component.searchQuery = 'museum';
    component.doSearch();

    const request = http.expectOne(req =>
      req.url === '/api/admin/regional/map/search' &&
      req.params.get('regionId') === '17' &&
      req.params.get('query') === 'museum',
    );

    expect(request.request.method).toBe('GET');
    request.flush({
      items: [
        {
          id: 99,
          entityType: 'Place',
          title: 'Museum',
          latitude: 45.1,
          longitude: 16.2,
          imageUrl: null,
          detailRoute: '/places/detail/99',
        },
      ],
    });

    expect(component.searchResults[0].title).toBe('Museum');
  });

  it('passes dropped pin coordinates and active region to New Place', () => {
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    component.activeRegionId = 17;
    (component as unknown as { droppedPin: { latitude: number; longitude: number } }).droppedPin = {
      latitude: 45.1234567,
      longitude: 16.7654321,
    };

    component.navigateToNewPlaceWithPin();

    expect(navigateSpy).toHaveBeenCalledWith(['/places/new'], {
      queryParams: {
        latitude: '45.123457',
        longitude: '16.765432',
        source: 'map',
        regionId: 17,
      },
      state: {
        regionId: 17,
        droppedPin: {
          latitude: 45.1234567,
          longitude: 16.7654321,
        },
        returnTo: '/map',
      },
    });
  });

  it('navigates add actions with active region context', () => {
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    component.activeRegionId = 17;

    component.navigateToNewDestination();
    component.navigateToNewLocation();

    expect(navigateSpy).toHaveBeenCalledWith(['/destinations/new'], {
      queryParams: { regionId: 17 },
      state: { regionId: 17, returnTo: '/map' },
    });
    expect(navigateSpy).toHaveBeenCalledWith(['/locations/new'], {
      queryParams: { regionId: 17 },
      state: { regionId: 17, returnTo: '/map' },
    });
  });

  it('navigates marker edit actions with entity id and active region context', () => {
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    component.activeRegionId = 17;
    component.selectedEntity = {
      id: 8,
      entityType: 'Place',
      title: 'Real Place',
      latitude: 45.1,
      longitude: 16.2,
      editRoute: '/places/edit/8',
    };

    component.editSelected();

    expect(navigateSpy).toHaveBeenCalledWith(['/places/edit/8'], {
      queryParams: { regionId: 17 },
      state: { regionId: 17, returnTo: '/map' },
    });
  });

  function flushRegions(regions: RegionalAssignedRegion[]): void {
    const request = http.expectOne('/api/admin/regional/regions');
    expect(request.request.method).toBe('GET');
    request.flush(regions);
  }

  function flushMap(regionId: number, response: AdminMapResponse): void {
    const request = http.expectOne(req =>
      req.url === '/api/admin/regional/map/entities' &&
      req.params.get('regionId') === String(regionId) &&
      req.params.get('types') === 'destination,location,place',
    );

    expect(request.request.method).toBe('GET');
    request.flush(response);
  }

  function flushSidebar(regionId: number, pendingRequestsCount: number, assignedRegions: RegionalAssignedRegion[]): void {
    const request = http.expectOne(req =>
      req.url === '/api/admin/regional/sidebar-summary' &&
      req.params.get('regionId') === String(regionId),
    );

    expect(request.request.method).toBe('GET');
    const activeRegion = assignedRegions.find(region => region.id === regionId) ?? assignedRegions[0];
    request.flush({
      assignedRegions,
      activeRegion,
      pendingRequestsCount,
    });
  }

  function mapResponse(regionId: number, placeTitle = 'Real Place'): AdminMapResponse {
    return {
      viewport: {
        centerLatitude: 45.1,
        centerLongitude: 16.2,
        zoom: 10,
        isEmpty: false,
      },
      markers: [
        {
          id: 8,
          entityType: 'Place',
          title: placeTitle,
          subtitle: 'Inside assigned region',
          latitude: 45.1,
          longitude: 16.2,
          imageUrl: '/images/place.webp',
          status: 'Published',
          regionId,
          placeId: 8,
          detailRoute: '/places/detail/8',
          editRoute: '/places/edit/8',
        },
      ],
      zones: [
        {
          id: regionId,
          entityType: 'Region',
          name: 'North',
          displayName: 'North Region',
          geometryGeoJson: {
            type: 'Polygon',
            coordinates: [[
              [16.0, 45.0],
              [16.4, 45.0],
              [16.4, 45.4],
              [16.0, 45.4],
              [16.0, 45.0],
            ]],
          },
          centerLatitude: 45.2,
          centerLongitude: 16.2,
          detailRoute: '/regions/detail/17',
        },
      ],
    };
  }
});
