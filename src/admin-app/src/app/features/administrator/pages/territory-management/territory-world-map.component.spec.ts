import { ComponentFixture, TestBed } from '@angular/core/testing';
import { vi } from 'vitest';

import { MapTileLayerService } from '../../../../core/services/map-tile-layer.service';
import {
  GeoJsonGeometry,
  TerritoryMapFeatureCollection,
  TerritoryMapFeatureProperties,
} from '../../../../core/models/administrator/territory.model';
import { TerritoryWorldMapComponent } from './territory-world-map.component';

describe('TerritoryWorldMapComponent', () => {
  let fixture: ComponentFixture<TerritoryWorldMapComponent>;
  let component: TerritoryWorldMapComponent;
  let geoJsonPayload: any;
  let geoJsonOptions: any;
  let mapOptions: any;
  let tileLayerOptions: any;
  let mapInstance: { removeLayer: ReturnType<typeof vi.fn>; fitBounds: ReturnType<typeof vi.fn>; remove: ReturnType<typeof vi.fn> };
  let layerGroup: any;
  let featureLayers: any[];
  let mapTileLayer: { addTo: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    mapInstance = {
      removeLayer: vi.fn(),
      fitBounds: vi.fn(),
      remove: vi.fn(),
    };
    featureLayers = [];
    layerGroup = {
      addTo: vi.fn(() => layerGroup),
      eachLayer: vi.fn((callback: (layer: any) => void) => {
        featureLayers.forEach(callback);
      }),
    };

    vi.stubGlobal('L', {
      map: vi.fn((_element: HTMLElement, options: any) => {
        mapOptions = options;
        return mapInstance;
      }),
      tileLayer: vi.fn((_url: string, options: any) => {
        tileLayerOptions = options;
        return { addTo: vi.fn() };
      }),
      canvas: vi.fn(() => ({ padding: 0.35 })),
      geoJSON: vi.fn((payload: any, options: any) => {
        geoJsonPayload = payload;
        geoJsonOptions = options;
        featureLayers = (payload.features ?? []).map((feature: any) => {
          const layer: any = {
            feature,
            setStyle: vi.fn(),
            bindTooltip: vi.fn(),
            unbindTooltip: vi.fn(() => layer),
            off: vi.fn(() => layer),
            on: vi.fn(() => layer),
          };
          return layer;
        });
        return layerGroup;
      }),
    });
    mapTileLayer = {
      addTo: vi.fn((leaflet: any, map: any, options: any = {}) =>
        leaflet.tileLayer('shared://tiles', options).addTo(map)),
    };

    await TestBed.configureTestingModule({
      imports: [TerritoryWorldMapComponent],
      providers: [
        { provide: MapTileLayerService, useValue: mapTileLayer },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(TerritoryWorldMapComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => {
    fixture.destroy();
    vi.unstubAllGlobals();
  });

  it('renders available MultiPolygon countries and disputed features without requiring iso3', () => {
    component.featureCollection = mapCollection();

    (component as any).map = mapInstance;
    (component as any).renderLayerNow();

    expect(geoJsonPayload.features).toHaveLength(2);
    expect(geoJsonPayload.features.some((feature: any) =>
      feature.properties.iso3 === 'RUS' &&
      feature.geometry.type === 'MultiPolygon')).toBe(true);
    expect(geoJsonPayload.features.some((feature: any) =>
      feature.properties.territoryKind === 'DisputedTerritory' &&
      feature.properties.iso3 === null)).toBe(true);
    expect(geoJsonOptions.noClip).toBe(true);
    expect(geoJsonOptions.fillRule).toBe('nonzero');
  });

  it('updates feature status without rebuilding stable boundary geometry', () => {
    component.featureCollection = mapCollection();
    (component as any).map = mapInstance;
    (component as any).renderLayerNow();

    const updated = mapCollection();
    updated.features[0].properties.status = 'Assigned';
    updated.features[0].properties.canAssign = false;
    updated.features[0].properties.canEdit = true;
    component.featureCollection = updated;

    component.ngOnChanges({ featureCollection: {} as any });

    expect((globalThis as any).L.geoJSON).toHaveBeenCalledTimes(1);
    expect(featureLayers[0].feature.properties.status).toBe('Assigned');
    expect(featureLayers[0].setStyle).toHaveBeenCalled();
  });

  it('initializes Leaflet without world-copy jumping while allowing the dateline tile copy', () => {
    component.mapEl = { nativeElement: document.createElement('div') } as any;

    (component as any).initMap();

    expect(mapOptions.worldCopyJump).toBe(false);
    expect(mapOptions.maxBounds).toEqual([[-85, -220], [85, 220]]);
    expect(mapTileLayer.addTo).toHaveBeenCalledWith((globalThis as any).L, mapInstance, {
      opacity: 0.35,
      noWrap: false,
    });
    expect(tileLayerOptions.noWrap).toBe(false);
  });

  it('does not filter Russia-like antimeridian MultiPolygons or Kosovo alias features', () => {
    component.featureCollection = mapCollection();
    component.featureCollection.features.push({
      type: 'Feature',
      geometry: {
        type: 'MultiPolygon',
        coordinates: [
          [[[160, 50], [180, 50], [190, 55], [160, 55], [160, 50]]],
        ],
      },
      properties: props({ status: 'Available', iso2: 'RU', iso3: 'RUS', name: 'Russia' }),
    });
    component.featureCollection.features.push({
      type: 'Feature',
      geometry: polygonGeometry(),
      properties: props({
        status: 'Available',
        iso2: 'XK',
        iso3: 'XKK',
        shapeGroup: 'XKX',
        name: 'Kosovo',
      }),
    });

    (component as any).map = mapInstance;
    (component as any).renderLayerNow();

    expect(geoJsonPayload.features.some((feature: any) =>
      feature.properties.iso3 === 'RUS' &&
      feature.geometry.coordinates[0][0].some((coordinate: number[]) => coordinate[0] > 180))).toBe(true);
    expect(geoJsonPayload.features.some((feature: any) =>
      feature.properties.iso3 === 'XKK' &&
      feature.properties.shapeGroup === 'XKX')).toBe(true);
    expect(geoJsonPayload.features.some((feature: any) =>
      feature.properties.iso3 === 'TKM')).toBe(false);
  });

  it('styles available and missing-display states distinctly', () => {
    const available = (component as any).styleFor(props({ status: 'Available', iso2: 'RU', iso3: 'RUS' }));
    const missingDisplay = (component as any).styleFor(props({
      status: 'MissingDisplayGeometry',
      iso2: 'TM',
      iso3: 'TKM',
      hasDisplayBoundary: false,
    }));

    expect(available.color).toBe('#15803d');
    expect(available.fillColor).toBe('#bbf7d0');
    expect(missingDisplay.color).toBe('#d97706');
    expect(missingDisplay.dashArray).toBe('2 4');
  });

  it('emits available country clicks with territory identity and display geometry', () => {
    const emitted: any[] = [];
    component.countryClick.subscribe(value => emitted.push(value));

    (component as any).emitCountryClick(props({
      status: 'Available',
      iso2: 'XK',
      iso3: 'XKK',
      shapeGroup: 'XKX',
      name: 'Kosovo',
      territoryId: 42,
    }), polygonGeometry());

    expect(emitted[0]).toMatchObject({
      code: 'XK',
      iso3: 'XKK',
      name: 'Kosovo',
      territoryKind: 'Country',
      shapeGroup: 'XKX',
      territoryId: 42,
      status: 'Available',
      canAssign: true,
    });
    expect(emitted[0].geometry).toEqual(polygonGeometry());
  });

  it('emits disputed territory clicks as view-only features', () => {
    const emitted: any[] = [];
    component.countryClick.subscribe(value => emitted.push(value));

    (component as any).emitCountryClick(props({
      territoryKind: 'DisputedTerritory',
      status: 'DisputedViewOnly',
      iso2: null,
      iso3: null,
      countryCode: null,
      name: 'Abyei',
      shapeGroup: '111',
      shapeType: 'DISP',
      shapeId: 'ABYEI-DISP',
      territoryId: 77,
      canAssign: false,
    }), polygonGeometry());

    expect(emitted[0]).toMatchObject({
      code: 'ABYEI-DISP',
      iso2: null,
      iso3: null,
      territoryKind: 'DisputedTerritory',
      status: 'DisputedViewOnly',
      canAssign: false,
      territoryId: 77,
    });
  });

  function mapCollection(): TerritoryMapFeatureCollection {
    return {
      type: 'FeatureCollection',
      features: [
        {
          type: 'Feature',
          geometry: {
            type: 'MultiPolygon',
            coordinates: [
              [[[30, 55], [31, 55], [31, 56], [30, 56], [30, 55]]],
              [[[50, 55], [51, 55], [51, 56], [50, 56], [50, 55]]],
            ],
          },
          properties: props({ status: 'Available', iso2: 'RU', iso3: 'RUS', name: 'Russia' }),
        },
        {
          type: 'Feature',
          geometry: null,
          properties: props({
            status: 'MissingDisplayGeometry',
            iso2: 'TM',
            iso3: 'TKM',
            name: 'Turkmenistan',
            hasDisplayBoundary: false,
          }),
        },
        {
          type: 'Feature',
          geometry: polygonGeometry(),
          properties: props({
            territoryKind: 'DisputedTerritory',
            status: 'DisputedViewOnly',
            iso2: null,
            iso3: null,
            countryCode: null,
            name: 'Abyei',
            shapeGroup: '111',
            shapeType: 'DISP',
            canAssign: false,
          }),
        },
      ],
    };
  }

  function props(overrides: Partial<TerritoryMapFeatureProperties> = {}): TerritoryMapFeatureProperties {
    const name = overrides.name ?? 'Feature';
    return {
      territoryId: 1,
      countryId: 1,
      territoryKind: 'Country',
      iso2: 'AA',
      iso3: 'AAA',
      countryCode: 'AA',
      name,
      displayName: name,
      status: 'Available',
      shapeGroup: 'AAA',
      shapeType: 'ADM0',
      shapeId: 'AAA-ADM0',
      assignedManager: null,
      hasBoundary: true,
      hasValidationBoundary: true,
      hasDisplayBoundary: true,
      canAssign: true,
      canEdit: false,
      boundaryRequiredMessage: null,
      boundaryMissingReason: null,
      ...overrides,
    };
  }

  function polygonGeometry(): GeoJsonGeometry {
    return {
      type: 'Polygon',
      coordinates: [
        [
          [28, 9],
          [29, 9],
          [29, 10],
          [28, 10],
          [28, 9],
        ],
      ],
    };
  }
});
