import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { Subject, of } from 'rxjs';
import { vi } from 'vitest';
import { TerritoryRegionMapComponent } from './region-map.component';
import { AuthService } from '../../../../core/services/auth.service';
import { TerritoryDashboardService } from '../../../../core/services/territory-dashboard.service';
import { MapTileLayerService } from '../../../../core/services/map-tile-layer.service';
import {
  TerritoryAssignedTerritory,
  TerritoryRegionGeometry,
  TerritoryRegionMapResponse,
} from '../../../../core/models/dashboard.model';

describe('TerritoryRegionMapComponent', () => {
  let component: TerritoryRegionMapComponent;
  let fixture: ComponentFixture<TerritoryRegionMapComponent>;
  let territoryDashboard: {
    getAssignedTerritories: ReturnType<typeof vi.fn>;
    getActiveTerritoryId: ReturnType<typeof vi.fn>;
    setActiveTerritoryId: ReturnType<typeof vi.fn>;
    getRegionMap: ReturnType<typeof vi.fn>;
    regionSetupChanged$: ReturnType<Subject<void>['asObservable']>;
  };
  let mapTileLayer: { addTo: ReturnType<typeof vi.fn> };

  const assignedTerritories: TerritoryAssignedTerritory[] = [
    { id: 10, name: 'Croatia', displayName: 'Croatia', countryCode: 'HRV' },
  ];

  beforeEach(async () => {
    territoryDashboard = {
      getAssignedTerritories: vi.fn(() => of(assignedTerritories)),
      getActiveTerritoryId: vi.fn(() => 10),
      setActiveTerritoryId: vi.fn(),
      getRegionMap: vi.fn(() => of(mapResponse())),
      regionSetupChanged$: new Subject<void>().asObservable(),
    };
    mapTileLayer = {
      addTo: vi.fn((leaflet: any, map: any, options: any = {}) =>
        leaflet.tileLayer('shared://tiles', options).addTo(map)),
    };

    await TestBed.configureTestingModule({
      imports: [TerritoryRegionMapComponent],
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            fullName: 'Mika Territory Manager',
            getUser: vi.fn(() => ({ role: 'Territory Manager', activeRoleCode: 'TerritoryManager' })),
            isAdministrator: vi.fn(() => false),
            isContentModerator: vi.fn(() => false),
            isRegionalManager: vi.fn(() => false),
            isTerritoryManager: vi.fn(() => true),
            canAccessTerritoryAdmin: vi.fn(() => true),
            getAvailableAdminViews: vi.fn(() => []),
            getActiveAdminView: vi.fn(() => ({ roleCode: 'TerritoryManager', label: 'Territory Manager' })),
            switchAdminView: vi.fn(() => false),
            logout: vi.fn(),
          },
        },
        { provide: TerritoryDashboardService, useValue: territoryDashboard },
        { provide: MapTileLayerService, useValue: mapTileLayer },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(TerritoryRegionMapComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('loads scoped region map data and draws persisted region geometries', async () => {
    const leaflet = installLeafletStub();

    fixture.detectChanges();
    await settleMapInit();
    fixture.detectChanges();

    expect(territoryDashboard.getRegionMap).toHaveBeenCalledWith(10);
    expect(component.activeTerritoryId).toBe(10);
    expect(component.totalRegions).toBe(3);
    expect(component.assignedRegionCount).toBe(1);
    expect(component.missingBoundaryCount).toBe(1);
    expect(fixture.nativeElement.textContent).toContain('Dubrovnik-Neretva');
    expect(fixture.nativeElement.textContent).toContain('No manager assigned');

    const drawnGeometries = leaflet.geoJsonLayers.map(layer => layer.geometry);
    expect(drawnGeometries).toContain(component.mapData?.territory?.geometryGeoJson);
    expect(drawnGeometries).toContain(component.mapData?.regions[0].geometryGeoJson);
    expect(drawnGeometries).toContain(component.mapData?.regions[1].geometryGeoJson);
    expect(drawnGeometries).not.toContain(component.mapData?.regions[2].geometryGeoJson);
    expect(leaflet.controlZoom).toHaveBeenCalledWith({ position: 'topleft' });
    expect(leaflet.zoomControl.addTo).toHaveBeenCalledWith(leaflet.map);
    expect(mapTileLayer.addTo).toHaveBeenCalledWith((globalThis as any).L, leaflet.map);
  });

  it('does not draw unmarked display geometries as precise boundaries', async () => {
    const leaflet = installLeafletStub();
    const response = mapResponse();
    response.territory = response.territory
      ? { ...response.territory, geometrySource: 'Territory.BoundaryDisplayGeometry' }
      : response.territory;
    response.regions = response.regions.map((region, index) => index === 0
      ? { ...region, geometrySource: 'Region.BoundaryDisplayGeometry' }
      : region);
    territoryDashboard.getRegionMap.mockReturnValueOnce(of(response));

    fixture.detectChanges();
    await settleMapInit();
    fixture.detectChanges();

    const drawnGeometries = leaflet.geoJsonLayers.map(layer => layer.geometry);
    expect(drawnGeometries).not.toContain(response.territory?.geometryGeoJson);
    expect(drawnGeometries).not.toContain(response.regions[0].geometryGeoJson);
    expect(fixture.nativeElement.textContent).toContain('Display boundary ignored');
  });

  it('selects and fits a region from the list', async () => {
    const leaflet = installLeafletStub();

    fixture.detectChanges();
    await settleMapInit();
    fixture.detectChanges();

    const istriaButton = Array.from(
      fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>,
    ).find(button => button.textContent?.includes('Istria'));

    istriaButton?.click();
    fixture.detectChanges();

    expect(component.selectedRegionId).toBe(2);
    expect(leaflet.map.fitBounds).toHaveBeenCalled();
  });

  it('selects a region when its polygon is clicked', async () => {
    const leaflet = installLeafletStub();

    fixture.detectChanges();
    await settleMapInit();

    const istriaLayer = leaflet.geoJsonLayers.find(
      layer => layer.geometry === component.mapData?.regions[1].geometryGeoJson,
    );
    istriaLayer.handlers.click({ originalEvent: new Event('click') });
    fixture.detectChanges();

    expect(component.selectedRegionId).toBe(2);
  });

  it('routes assignment management through the existing user administration flow', async () => {
    installLeafletStub();
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    fixture.detectChanges();
    await settleMapInit();

    component.manageSelectedAssignment();

    expect(navigateSpy).toHaveBeenCalledWith(['/territory/users'], {
      queryParams: {
        territoryId: 10,
        regionId: 1,
      },
      state: {
        territoryId: 10,
        regionId: 1,
        regionName: 'Dubrovnik-Neretva',
      },
    });
  });

  it('shows the safe empty state when no regions are available', async () => {
    installLeafletStub();
    territoryDashboard.getRegionMap.mockReturnValueOnce(of({
      territory: {
        territoryId: 10,
        name: 'Croatia',
        displayName: 'Croatia',
        countryIso3: 'HRV',
        hasBoundary: true,
        geometrySource: 'Territory.BoundaryGeometry',
        geometryGeoJson: polygon(13, 42, 20, 47),
        viewport: viewport(13, 42, 20, 47),
      },
      viewport: viewport(13, 42, 20, 47),
      regions: [],
      emptyStateMessage: 'No regions are available for this territory. Sync ADM1 regions first.',
      boundaryWarningMessage: null,
    } satisfies TerritoryRegionMapResponse));

    fixture.detectChanges();
    await settleMapInit();
    fixture.detectChanges();

    expect(component.totalRegions).toBe(0);
    expect(fixture.nativeElement.textContent).toContain(
      'No regions are available for this territory. Sync ADM1 regions first.',
    );
  });

  function mapResponse(): TerritoryRegionMapResponse {
    return {
      territory: {
        territoryId: 10,
        name: 'Croatia',
        displayName: 'Croatia',
        countryIso3: 'HRV',
        hasBoundary: true,
        geometrySource: 'Territory.BoundaryGeometry',
        geometryGeoJson: polygon(13, 42, 20, 47),
        viewport: viewport(13, 42, 20, 47),
      },
      viewport: viewport(13, 42, 20, 47),
      regions: [
        {
          regionId: 1,
          name: 'Dubrovnik-Neretva',
          displayName: 'Dubrovnik-Neretva',
          territoryId: 10,
          territoryDisplayName: 'Croatia',
          status: 'Active',
          healthScore: 73,
          health: null,
          assignedManagers: [{
            userId: 44,
            displayName: 'Pera',
            email: 'pera@example.com',
            initials: 'P',
            avatarUrl: null,
            role: 'Regional Manager',
            assignedAt: '2026-05-01T00:00:00Z',
          }],
          managerDisplayName: 'Pera',
          contentCounts: {
            destinations: 1,
            locations: 8,
            places: 31,
            stays: 7,
            events: 11,
            pending: 2,
          },
          hasBoundary: true,
          geometrySource: 'Region.BoundaryGeometry',
          geometryGeoJson: polygon(17, 42, 18, 43),
          viewport: viewport(17, 42, 18, 43),
          centerLatitude: 42.5,
          centerLongitude: 17.5,
          detailRoute: '/territory/regions/1',
        },
        {
          regionId: 2,
          name: 'Istria',
          displayName: 'Istria',
          territoryId: 10,
          territoryDisplayName: 'Croatia',
          status: 'Inactive',
          healthScore: 0,
          health: null,
          assignedManagers: [],
          managerDisplayName: null,
          contentCounts: {
            destinations: 0,
            locations: 0,
            places: 0,
            stays: 0,
            events: 0,
            pending: 0,
          },
          hasBoundary: true,
          geometrySource: 'Region.BoundaryGeometry',
          geometryGeoJson: polygon(13, 44, 14, 45),
          viewport: viewport(13, 44, 14, 45),
          centerLatitude: 44.5,
          centerLongitude: 13.5,
          detailRoute: '/territory/regions/2',
        },
        {
          regionId: 3,
          name: 'Missing Boundary',
          displayName: 'Missing Boundary',
          territoryId: 10,
          territoryDisplayName: 'Croatia',
          status: 'Inactive',
          healthScore: null,
          health: null,
          assignedManagers: [],
          managerDisplayName: null,
          contentCounts: {
            destinations: 0,
            locations: 0,
            places: 0,
            stays: 0,
            events: 0,
            pending: 0,
          },
          hasBoundary: false,
          geometrySource: null,
          boundaryMissingReason: 'Full region boundary is missing.',
          geometryGeoJson: null,
          viewport: { isEmpty: true },
          centerLatitude: null,
          centerLongitude: null,
          detailRoute: '/territory/regions/3',
        },
      ],
      emptyStateMessage: null,
      boundaryWarningMessage: 'Boundary data is missing for this territory or some regions.',
    };
  }

  function polygon(west: number, south: number, east: number, north: number): TerritoryRegionGeometry {
    return {
      type: 'Polygon',
      coordinates: [[
        [west, south],
        [east, south],
        [east, north],
        [west, north],
        [west, south],
      ]],
    };
  }

  function viewport(west: number, south: number, east: number, north: number) {
    return {
      centerLatitude: (south + north) / 2,
      centerLongitude: (west + east) / 2,
      zoom: 7,
      bounds: { north, south, east, west },
      isEmpty: false,
    };
  }

  function settleMapInit(): Promise<void> {
    return new Promise(resolve => setTimeout(resolve, 0))
      .then(() => new Promise(resolve => setTimeout(resolve, 0)));
  }

  function installLeafletStub(): {
    map: any;
    geoJSON: ReturnType<typeof vi.fn>;
    controlZoom: ReturnType<typeof vi.fn>;
    zoomControl: { addTo: ReturnType<typeof vi.fn> };
    geoJsonLayers: any[];
  } {
    const panes = new Map<string, HTMLElement>();
    const geoJsonLayers: any[] = [];

    const map = {
      fitWorld: vi.fn(),
      fitBounds: vi.fn(),
      setView: vi.fn(),
      invalidateSize: vi.fn(),
      remove: vi.fn(),
      getContainer: vi.fn(() => document.createElement('div')),
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
      return group;
    });

    const geoJSON = vi.fn((geometry: unknown, options: any) => {
      const layer = {
        geometry,
        options,
        handlers: {} as Record<string, (event: any) => void>,
        bindTooltip: vi.fn(() => layer),
        on: vi.fn((eventName: string, handler: (event: any) => void) => {
          layer.handlers[eventName] = handler;
          return layer;
        }),
        addTo: vi.fn((target: any) => {
          target?.addLayer?.(layer);
          return layer;
        }),
        getBounds: vi.fn(() => [[42, 13], [47, 20]]),
        setStyle: vi.fn(),
      };
      geoJsonLayers.push(layer);
      return layer;
    });

    const zoomControl = {
      addTo: vi.fn(() => zoomControl),
    };
    const controlZoom = vi.fn(() => zoomControl);

    vi.stubGlobal('L', {
      map: vi.fn(() => map),
      control: {
        zoom: controlZoom,
      },
      tileLayer: vi.fn(() => ({ addTo: vi.fn() })),
      layerGroup,
      geoJSON,
      DomEvent: {
        stopPropagation: vi.fn(),
      },
    });

    return { map, geoJSON, controlZoom, zoomControl, geoJsonLayers };
  }
});
