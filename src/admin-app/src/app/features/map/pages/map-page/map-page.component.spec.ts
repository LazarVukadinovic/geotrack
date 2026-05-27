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
    vi.unstubAllGlobals();
  });

  it('loads active assigned region map data from the backend', () => {
    fixture.detectChanges();
    flushRegions([{ id: 17, name: 'North', displayName: 'North Region', territoryName: 'Testland' }]);
    flushMap(17, mapResponse(17));

    expect(component.activeRegionId).toBe(17);
    expect(component.regionDisplayName).toBe('North Region');
    expect(component.mapLoading).toBe(false);
    expect(component.mapData?.markers[0].title).toBe('Real Place');
    expect(component.mapData?.assignedRegionBoundary?.hasBoundary).toBe(true);
    expect(component.mapData?.assignedRegionBoundary?.geometrySource).toBe('Region.BoundaryGeometry');
    expect(sessionStorage.getItem('geotrack_admin_active_region_id')).toBe('17');
  });

  it('reloads markers and zones when the active region changes', () => {
    fixture.detectChanges();
    flushRegions([
      { id: 17, name: 'North', displayName: 'North Region', territoryName: 'Testland' },
      { id: 18, name: 'South', displayName: 'South Region', territoryName: 'Testland' },
    ]);
    flushMap(17, mapResponse(17));

    component.onRegionSelected(18);
    flushMap(18, mapResponse(18, 'Other Place'));

    expect(component.activeRegionId).toBe(18);
    expect(component.mapData?.markers[0].title).toBe('Other Place');
    expect(sessionStorage.getItem('geotrack_admin_active_region_id')).toBe('18');
  });

  it('keeps the map empty when no assigned region exists', () => {
    fixture.detectChanges();
    flushRegions([]);
    fixture.detectChanges();

    expect(component.activeRegionId).toBeNull();
    expect(component.mapData).toBeNull();
    expect(component.mapLoading).toBe(false);
    expect(component.emptyMessage).toBe('No active assigned region.');
    expect(fixture.nativeElement.textContent).toContain('No assigned region');
  });

  it('renders the assigned region boundary as a non-blocking overlay and keeps markers visible', () => {
    const leaflet = installLeafletStub();
    const candidateLikeGeometry = {
      type: 'Polygon' as const,
      coordinates: [[
        [17.0, 46.0],
        [17.4, 46.0],
        [17.4, 46.4],
        [17.0, 46.4],
        [17.0, 46.0],
      ]],
    };

    fixture.detectChanges();
    flushRegions([{ id: 17, name: 'North', displayName: 'North Region', territoryName: 'Testland' }]);
    flushMap(17, mapResponse(17, 'Real Place', {}, {
      geometryGeoJson: candidateLikeGeometry,
      geometrySource: 'BoundaryCandidateDisplayGeometry',
      isGeometryFallback: true,
    }));

    const regionLayers = leaflet.geoJsonLayers.filter(layer => layer.options.pane === 'admin-region-zone-pane');
    const regionLayer = regionLayers[0];

    expect(regionLayers).toHaveLength(1);
    expect(regionLayer.geometry).toBe(component.mapData?.assignedRegionBoundary?.geometryGeoJson);
    expect(regionLayer.geometry).not.toBe(component.mapData?.zones[0].geometryGeoJson);
    expect(regionLayer?.options.pane).toBe('admin-region-zone-pane');
    expect(regionLayer?.options.interactive).toBe(false);
    expect(leaflet.marker).toHaveBeenCalled();
    expect(leaflet.map.fitBounds).toHaveBeenCalledWith(
      [
        [45, 16],
        [45.4, 16.4],
      ],
      {
        padding: [60, 60],
        maxZoom: 14,
        animate: false,
      },
    );
  });

  it('clears the old assigned region boundary when the active region changes', () => {
    const leaflet = installLeafletStub();

    fixture.detectChanges();
    flushRegions([
      { id: 17, name: 'North', displayName: 'North Region', territoryName: 'Testland' },
      { id: 18, name: 'South', displayName: 'South Region', territoryName: 'Testland' },
    ]);
    flushMap(17, mapResponse(17));

    component.onRegionSelected(18);
    flushMap(18, mapResponse(18, 'Other Place'));

    const regionLayerGroup = leaflet.layerGroups[0];
    expect(regionLayerGroup.clearLayers).toHaveBeenCalled();
    expect(regionLayerGroup.layers).toHaveLength(1);
    expect(regionLayerGroup.layers[0].geometry).toBe(component.mapData?.assignedRegionBoundary?.geometryGeoJson);
  });

  it('shows a missing boundary setup notice without drawing fallback geometry', () => {
    const leaflet = installLeafletStub();

    fixture.detectChanges();
    flushRegions([{ id: 17, name: 'North', displayName: 'North Region', territoryName: 'Testland' }]);
    const response = mapResponse(17, 'Real Place', {
      hasBoundary: false,
      geometryGeoJson: null,
      viewport: { isEmpty: true },
      message: 'Assigned region boundary is missing. Please contact a territory manager or administrator.',
    });
    response.zones = [];
    flushMap(17, response);
    fixture.detectChanges();

    expect(component.regionBoundaryNotice).toBe(
      'Assigned region boundary is missing. Please contact a territory manager or administrator.',
    );
    expect(leaflet.geoJsonLayers.filter(layer => layer.options.pane === 'admin-region-zone-pane')).toHaveLength(0);
    expect(leaflet.marker).toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('Assigned region boundary is missing');
  });

  it('shows a clean error state when map loading fails', () => {
    fixture.detectChanges();
    flushRegions([{ id: 17, name: 'North', displayName: 'North Region', territoryName: 'Testland' }]);

    const request = http.expectOne(req =>
      req.url === '/api/admin/regional/map/entities' &&
      req.params.get('regionId') === '17',
    );
    request.flush({ message: 'Unable to load map data for the active region.' }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(component.errorMessage).toBe('Unable to load map data for the active region.');
    expect(fixture.nativeElement.textContent).toContain('Unable to load map data for the active region.');
  });

  it('searches only inside the active assigned region', () => {
    fixture.detectChanges();
    flushRegions([{ id: 17, name: 'North', displayName: 'North Region', territoryName: 'Testland' }]);
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

  function mapResponse(
    regionId: number,
    placeTitle = 'Real Place',
    boundaryOverrides: Partial<NonNullable<AdminMapResponse['assignedRegionBoundary']>> = {},
    zoneOverrides: Partial<AdminMapResponse['zones'][number]> = {},
  ): AdminMapResponse {
    const assignedRegionGeometry = {
      type: 'Polygon' as const,
      coordinates: [[
        [16.0, 45.0],
        [16.4, 45.0],
        [16.4, 45.4],
        [16.0, 45.4],
        [16.0, 45.0],
      ]],
    };

    return {
      viewport: {
        centerLatitude: 45.1,
        centerLongitude: 16.2,
        zoom: 10,
        bounds: {
          south: 45.0,
          west: 16.0,
          north: 45.4,
          east: 16.4,
        },
        isEmpty: false,
      },
      assignedRegionBoundary: {
        regionId,
        regionName: 'North',
        displayName: 'North Region',
        geometrySource: 'Region.BoundaryGeometry',
        hasBoundary: true,
        geometryGeoJson: assignedRegionGeometry,
        viewport: {
          centerLatitude: 45.2,
          centerLongitude: 16.2,
          zoom: 10,
          bounds: {
            south: 45.0,
            west: 16.0,
            north: 45.4,
            east: 16.4,
          },
          isEmpty: false,
        },
        message: null,
        ...boundaryOverrides,
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
          geometryGeoJson: assignedRegionGeometry,
          centerLatitude: 45.2,
          centerLongitude: 16.2,
          detailRoute: '/regions/detail/17',
          ...zoneOverrides,
        },
      ],
    };
  }

  function installLeafletStub(): {
    map: any;
    geoJSON: ReturnType<typeof vi.fn>;
    marker: ReturnType<typeof vi.fn>;
    geoJsonLayers: any[];
    layerGroups: any[];
  } {
    const panes = new Map<string, HTMLElement>();
    const geoJsonLayers: any[] = [];
    const layerGroups: any[] = [];

    const map = {
      fitWorld: vi.fn(),
      fitBounds: vi.fn(),
      setView: vi.fn(),
      on: vi.fn(),
      remove: vi.fn(),
      removeLayer: vi.fn(),
      getContainer: vi.fn(() => document.createElement('div')),
      latLngToContainerPoint: vi.fn(() => ({ x: 0, y: 0 })),
      getPane: vi.fn((name: string) => panes.get(name) ?? null),
      createPane: vi.fn((name: string) => {
        const pane = document.createElement('div');
        panes.set(name, pane);
        return pane;
      }),
    };

    const layerGroup = vi.fn(() => {
      const group = {
        layers: [] as any[],
        addTo: vi.fn(() => group),
        clearLayers: vi.fn(() => {
          group.layers = [];
        }),
        addLayer: vi.fn((layer: any) => {
          group.layers.push(layer);
          return group;
        }),
      };
      layerGroups.push(group);
      return group;
    });

    const geoJSON = vi.fn((geometry: unknown, options: any) => {
      const layer = {
        geometry,
        options,
        on: vi.fn(),
        addTo: vi.fn((target: any) => {
          target?.addLayer?.(layer);
          return layer;
        }),
        getBounds: vi.fn(),
      };
      geoJsonLayers.push(layer);
      return layer;
    });

    const marker = vi.fn(() => {
      const layer = {
        on: vi.fn(),
        addTo: vi.fn((target: any) => {
          target?.addLayer?.(layer);
          return layer;
        }),
      };
      return layer;
    });

    vi.stubGlobal('L', {
      map: vi.fn(() => map),
      tileLayer: vi.fn(() => ({ addTo: vi.fn() })),
      layerGroup,
      control: {
        zoom: vi.fn(() => ({ addTo: vi.fn() })),
      },
      geoJSON,
      marker,
      divIcon: vi.fn((options: unknown) => options),
      DomEvent: {
        stopPropagation: vi.fn(),
      },
    });

    return { map, geoJSON, marker, geoJsonLayers, layerGroups };
  }
});
