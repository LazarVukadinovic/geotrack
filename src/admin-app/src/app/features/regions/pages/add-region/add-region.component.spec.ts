import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of } from 'rxjs';
import { vi } from 'vitest';

import { AddRegionComponent } from './add-region.component';
import { AuthService } from '../../../../core/services/auth.service';
import { TerritoryDashboardService } from '../../../../core/services/territory-dashboard.service';
import { ZoneDrawService } from '../../../../core/services/zone-draw.service';
import { TerritoryRegionCreateContext } from '../../../../core/models/dashboard.model';

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

  const createContext = (canCreate = true): TerritoryRegionCreateContext => ({
    territory: { id: 10, name: 'Assigned Territory', displayName: 'Assigned Territory' },
    territoryBoundaryGeoJson: canCreate ? territoryBoundary : null,
    existingRegions: [
      {
        id: 1,
        regionId: 1,
        name: 'Existing Region',
        displayName: 'Existing Region',
        geometryGeoJson: territoryBoundary,
      },
    ],
    viewport: {
      isEmpty: false,
      bounds: { south: 42, west: 18, north: 44, east: 20 },
    },
    canCreate,
    boundaryRequiredMessage: canCreate
      ? null
      : 'Assigned country/territory must have a boundary before adding regions.',
  });

  const setup = async (context = createContext()) => {
    const createRegion = vi.fn(() => of({
      id: 44,
      regionId: 44,
      name: 'New Region',
      displayName: 'New Region',
      territoryId: 10,
      territoryDisplayName: 'Assigned Territory',
      status: 'Inactive',
      boundarySource: 'ManualDraw',
      boundaryShapeType: 'Polygon',
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
      createRegion,
    };

    const authService = {
      fullName: 'Mika Mikic',
      getUser: vi.fn(() => ({ role: 'Territory Manager', region: '' })),
      canAccessTerritoryAdmin: vi.fn(() => true),
      logout: vi.fn(),
    };

    const zoneDrawService = {
      drawHints: {
        polygon: 'Polygon hint',
        rectangle: 'Rectangle hint',
        circle: 'Circle hint',
      },
      calculateAreaKm2: vi.fn(() => 12.5),
      extractZoneResult: vi.fn(() => ({
        zone: [
          [42.4, 18.4],
          [42.4, 18.9],
          [42.9, 18.9],
          [42.9, 18.4],
        ],
        centerLat: 42.65,
        centerLng: 18.65,
      })),
    };

    await TestBed.configureTestingModule({
      imports: [AddRegionComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: authService },
        { provide: TerritoryDashboardService, useValue: territoryService },
        { provide: ZoneDrawService, useValue: zoneDrawService },
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
      zoneDrawService,
    };
  };

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('shows the assigned country/territory read-only', async () => {
    const { fixture } = await setup();
    const input = fixture.nativeElement.querySelector('input[readonly]') as HTMLInputElement;

    expect(input.value).toBe('Assigned Territory');
    expect(fixture.nativeElement.textContent).toContain('Add Region');
  });

  it('blocks creation when the assigned territory has no boundary', async () => {
    const { component, fixture } = await setup(createContext(false));

    expect(component.canSave).toBe(false);
    expect(component.canDrawBoundary).toBe(false);
    expect(fixture.nativeElement.textContent)
      .toContain('Assigned country/territory must have a boundary before adding regions.');
  });

  it('sends the backend create DTO after a valid drawn boundary', async () => {
    const { component, fixture, territoryService } = await setup();
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate');

    component.regionName = 'New Region';
    (component as unknown as { drawnLayer: unknown }).drawnLayer = {};
    (component as unknown as { boundaryShapeType: 'Polygon' }).boundaryShapeType = 'Polygon';
    component.boundaryConfirmed = true;
    component.boundaryValidationMessage = null;
    fixture.detectChanges();

    component.save();

    expect(territoryService.createRegion).toHaveBeenCalledWith({
      territoryId: 10,
      name: 'New Region',
      boundarySource: 'ManualDraw',
      boundary: {
        shapeType: 'Polygon',
        coordinates: [
          { latitude: 42.4, longitude: 18.4 },
          { latitude: 42.4, longitude: 18.9 },
          { latitude: 42.9, longitude: 18.9 },
          { latitude: 42.9, longitude: 18.4 },
        ],
      },
    });
    expect(navigate).toHaveBeenCalledWith(['/territory/regions'], {
      state: {
        feedbackMessage: 'New Region was added.',
        createdRegionName: 'New Region',
        territoryId: 10,
      },
    });
  });
});
