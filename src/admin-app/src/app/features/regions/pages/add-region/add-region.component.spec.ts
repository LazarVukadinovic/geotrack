import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { AddRegionComponent } from './add-region.component';
import { AuthService } from '../../../../core/services/auth.service';
import { TerritoryDashboardService } from '../../../../core/services/territory-dashboard.service';
import {
  BoundaryRegionCandidate,
  BoundaryRegionCandidateResponse,
  TerritoryRegionCreateContext,
} from '../../../../core/models/dashboard.model';

describe('AddRegionComponent', () => {
  const territoryBoundary = {
    type: 'Polygon',
    coordinates: [[
      [18, 42],
      [20, 42],
      [20, 44],
      [18, 44],
      [18, 42],
    ]],
  };

  const availableCandidate: BoundaryRegionCandidate = {
    featureKind: 'Adm1Candidate',
    candidateId: '101',
    boundaryCandidateId: 101,
    externalKey: 'geoBoundaries:ADM1:TAA:INSIDE-1',
    boundaryExternalKey: 'geoBoundaries:ADM1:TAA:INSIDE-1',
    admLevel: 'ADM1',
    iso3: 'TAA',
    countryIso3: 'TAA',
    shapeGroup: 'TAA',
    shapeId: 'INSIDE-1',
    shapeName: 'Inside Region',
    shapeType: 'ADM1',
    name: 'Inside Region',
    displayName: 'Inside Region',
    status: 'Available',
    hasBoundary: true,
    hasDisplayBoundary: true,
    canSelect: true,
    existingRegionId: null,
    isInsideAssignedTerritory: true,
    validationMessage: null,
    geometryGeoJson: territoryBoundary,
  };

  const existingCandidate: BoundaryRegionCandidate = {
    ...availableCandidate,
    candidateId: '102',
    boundaryCandidateId: 102,
    externalKey: 'geoBoundaries:ADM1:TAA:EXISTING-1',
    boundaryExternalKey: 'geoBoundaries:ADM1:TAA:EXISTING-1',
    shapeId: 'EXISTING-1',
    shapeName: 'Existing Region',
    name: 'Existing Region',
    displayName: 'Existing Region',
    status: 'ExistingRegion',
    canSelect: false,
    existingRegionId: 12,
    existingRegionName: 'Dalmatia',
    validationMessage: 'This administrative region has already been created.',
  };

  const createContext = (canCreate = true): TerritoryRegionCreateContext => ({
    territory: { id: 10, name: 'Assigned Territory', displayName: 'Assigned Territory' },
    territoryBoundaryGeoJson: canCreate ? territoryBoundary : null,
    existingRegions: [],
    viewport: {
      isEmpty: false,
      bounds: { south: 42, west: 18, north: 44, east: 20 },
    },
    canCreate,
    boundaryRequiredMessage: canCreate
      ? null
      : 'Assigned country/territory must have a boundary before adding regions.',
  });

  const candidateResponse = (
    items: BoundaryRegionCandidate[] = [availableCandidate, existingCandidate],
  ): BoundaryRegionCandidateResponse => ({
    source: 'geoBoundaries',
    admLevel: 'ADM1',
    iso3: 'TAA',
    scannedFeatures: 2,
    matchedFeatures: items.length,
    returnedCount: items.length,
    items,
    attribution: 'geoBoundaries',
  });

  const setup = async (
    context = createContext(),
    candidates$ = of(candidateResponse()),
  ) => {
    const createRegion = vi.fn(() => of({
      id: 44,
      regionId: 44,
      name: 'Inside Region',
      displayName: 'Inside Region',
      territoryId: 10,
      territoryDisplayName: 'Assigned Territory',
      status: 'Inactive',
      boundarySource: 'geoBoundaries',
      boundaryShapeType: 'Polygon',
      boundaryShapeGroup: 'TAA',
      boundaryShapeId: 'INSIDE-1',
      boundaryExternalKey: 'geoBoundaries:ADM1:TAA:INSIDE-1',
      boundaryAdmLevel: 'ADM1',
      boundaryReleaseType: 'gbOpen',
      boundaryCandidateId: 101,
      createdAt: '2026-05-20T00:00:00Z',
      detailRoute: '/territory/regions/44',
    }));

    const territoryService = {
      getAssignedTerritories: vi.fn(() => of([
        { id: 10, name: 'Assigned Territory', displayName: 'Assigned Territory' },
      ])),
      getActiveTerritoryId: vi.fn(() => 10),
      setActiveTerritoryId: vi.fn(),
      getRegionCreateContext: vi.fn(() => of(context)),
      getRegionBoundaryCandidates: vi.fn(() => candidates$),
      createRegion,
    };

    const authService = {
      fullName: 'Mika Mikic',
      getUser: vi.fn(() => ({ role: 'Territory Manager', region: '' })),
      isAdministrator: vi.fn(() => false),
      canAccessTerritoryAdmin: vi.fn(() => true),
      logout: vi.fn(),
    };

    await TestBed.configureTestingModule({
      imports: [AddRegionComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: authService },
        { provide: TerritoryDashboardService, useValue: territoryService },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(AddRegionComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    return {
      component: fixture.componentInstance,
      fixture,
      territoryService,
    };
  };

  afterEach(() => {
    vi.unstubAllGlobals();
    TestBed.resetTestingModule();
  });

  it('shows the assigned country and removes manual boundary controls', async () => {
    const { fixture } = await setup();
    const text = fixture.nativeElement.textContent;

    expect(text).toContain('Assigned Territory');
    expect(text).toContain('geoBoundaries ADM1');
    expect(text).not.toContain('Polygon');
    expect(text).not.toContain('Rectangle');
    expect(text).not.toContain('Manual draw');
  });

  it('loads ADM1 candidates from the backend', async () => {
    const { territoryService } = await setup();

    expect(territoryService.getRegionBoundaryCandidates).toHaveBeenCalledWith(10, 'ADM1');
  });

  it('selects an available ADM1 candidate and fills the region name', async () => {
    const { component, fixture } = await setup();

    expect(component.canSave).toBe(false);
    component.selectCandidate(availableCandidate);
    fixture.detectChanges();

    expect(component.selectedCandidate).toEqual(availableCandidate);
    expect(component.regionName).toBe('Inside Region');
    expect(component.canSave).toBe(true);
  });

  it('does not select an existing ADM1 candidate', async () => {
    const { component, fixture } = await setup();

    component.selectCandidate(existingCandidate);
    fixture.detectChanges();

    expect(component.selectedCandidate).toBeNull();
    expect(component.canSave).toBe(false);
    expect(fixture.nativeElement.textContent).toContain('Already created as: Dalmatia.');
  });

  it('renders existing regions from the existing-region layer without duplicating ExistingRegion candidates', async () => {
    const existingRegionGeometry = {
      type: 'Polygon',
      coordinates: [[
        [18.2, 42.2],
        [18.6, 42.2],
        [18.6, 42.6],
        [18.2, 42.6],
        [18.2, 42.2],
      ]],
    };
    const context = createContext();
    context.existingRegions = [
      {
        featureKind: 'ExistingRegion',
        id: 12,
        regionId: 12,
        name: 'Dalmatia',
        displayName: 'Dalmatia',
        geometryGeoJson: existingRegionGeometry,
      },
    ];
    const { component } = await setup(context);
    const layers: Array<Record<string, any>> = [];

    vi.stubGlobal('L', {
      geoJSON: vi.fn((geometry: unknown, options: any) => {
        const layer: Record<string, any> = {
          geometry,
          options,
          bindTooltip: vi.fn(),
          on: vi.fn(),
          setStyle: vi.fn(),
          addTo: vi.fn(() => {
            layers.push(layer);
            return layer;
          }),
        };

        options?.onEachFeature?.({}, layer);
        return layer;
      }),
    });

    (component as any).map = {
      eachLayer: vi.fn(),
      removeLayer: vi.fn(),
      fitBounds: vi.fn(),
      fitWorld: vi.fn(),
      setView: vi.fn(),
    };

    (component as any).renderMapLayers(false);

    const availableLayers = layers.filter(layer => layer['_isAdm1Candidate']);
    expect(availableLayers).toHaveLength(1);
    expect(availableLayers[0]['geometry']).toBe(availableCandidate.geometryGeoJson);
    expect(layers.some(layer => layer['geometry'] === existingCandidate.geometryGeoJson)).toBe(false);

    const existingLayer = layers.find(layer => layer['_isExistingRegionBoundary']);
    expect(existingLayer?.['geometry']).toBe(existingRegionGeometry);
    expect(existingLayer?.['bindTooltip']).toHaveBeenCalledWith(
      expect.stringContaining('Already created as: Dalmatia'),
      { sticky: true },
    );
  });

  it('sends the selected ADM1 candidate to the backend create endpoint', async () => {
    const { component, fixture, territoryService } = await setup();
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate');

    component.selectCandidate(availableCandidate);
    fixture.detectChanges();

    component.save();

    expect(territoryService.createRegion).toHaveBeenCalledWith({
      territoryId: 10,
      name: 'Inside Region',
      admLevel: 'ADM1',
      shapeGroup: 'TAA',
      shapeId: 'INSIDE-1',
      candidateExternalKey: 'geoBoundaries:ADM1:TAA:INSIDE-1',
      boundaryCandidateId: '101',
    });
    expect(navigate).toHaveBeenCalledWith(['/territory/regions'], {
      state: {
        feedbackMessage: 'Inside Region was added.',
        createdRegionName: 'Inside Region',
        territoryId: 10,
      },
    });
  });

  it('renders empty and error states for ADM1 candidate loading', async () => {
    const empty = await setup(createContext(), of(candidateResponse([])));
    expect(empty.fixture.nativeElement.textContent)
      .toContain('ADM1 boundary data is not available for this territory.');

    TestBed.resetTestingModule();

    const failing = await setup(
      createContext(),
      throwError(() => ({ error: { message: 'Unable to load ADM1 boundaries.' } })),
    );
    expect(failing.fixture.nativeElement.textContent)
      .toContain('Unable to load ADM1 boundary candidates for this territory.');
    expect(failing.fixture.nativeElement.textContent).toContain('Retry');
  });
});
