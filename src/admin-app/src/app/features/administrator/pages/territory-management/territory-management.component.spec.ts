import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormsModule } from '@angular/forms';
import { of } from 'rxjs';
import { vi } from 'vitest';

import { AdminDashboardService } from '../../../../core/services/administrator/admin-dashboard.service';
import { TerritoryService } from '../../../../core/services/administrator/territory.service';
import { GeoJsonGeometry, TerritoryDetail, TerritoryManager, TerritoryMapFeatureCollection } from '../../../../core/models/administrator/territory.model';
import { TerritoryManagementComponent } from './territory-management.component';
import { CountryMapAssignment, CountryMapClick } from './territory-world-map.component';

@Component({
  selector: 'app-sidebar',
  standalone: true,
  template: '<aside></aside>',
})
class SidebarStubComponent {
  @Input() supportReportsCount: number | null = null;
}

@Component({
  selector: 'app-topbar',
  standalone: true,
  template: '<header>{{ title }}</header>',
})
class TopbarStubComponent {
  @Input() title = '';
  @Input() showBack = false;
  @Input() backRoute = '';
}

@Component({
  selector: 'app-territory-world-map',
  standalone: true,
  template: '<div></div>',
})
class TerritoryWorldMapStubComponent {
  @Input() featureCollection: TerritoryMapFeatureCollection | null = null;
  @Input() assignments: Map<string, CountryMapAssignment> = new Map();
  @Input() highlightCode = '';
  @Input() isLoading = false;
  @Input() loadFailed = false;
  @Output() countryClick = new EventEmitter<CountryMapClick>();
  @Output() retryLoad = new EventEmitter<void>();
}

@Component({
  selector: 'app-country-mini-map',
  standalone: true,
  template: '<div></div>',
})
class CountryMiniMapStubComponent {
  @Input() countryCode = '';
  @Input() countryName = '';
  @Input() geometry: GeoJsonGeometry | null = null;
}

describe('TerritoryManagementComponent', () => {
  let fixture: ComponentFixture<TerritoryManagementComponent>;
  let component: TerritoryManagementComponent;
  let territoryService: {
    getAllCountries: ReturnType<typeof vi.fn>;
    getAllManagers: ReturnType<typeof vi.fn>;
    getActiveManagers: ReturnType<typeof vi.fn>;
    getPage$: ReturnType<typeof vi.fn>;
    getMap$: ReturnType<typeof vi.fn>;
    refreshMap$: ReturnType<typeof vi.fn>;
    refreshMapStatus$: ReturnType<typeof vi.fn>;
    getDetail$: ReturnType<typeof vi.fn>;
    searchManagerCandidates$: ReturnType<typeof vi.fn>;
    getAssignableManagers$: ReturnType<typeof vi.fn>;
    updateManager$: ReturnType<typeof vi.fn>;
    getCountryByCode: ReturnType<typeof vi.fn>;
  };

  beforeEach(async () => {
    territoryService = {
      getAllCountries: vi.fn(() => []),
      getAllManagers: vi.fn(() => []),
      getActiveManagers: vi.fn(() => []),
      getPage$: vi.fn(() => of({ items: [], total: 0 })),
      getMap$: vi.fn(() => of(mapCollection())),
      refreshMap$: vi.fn(() => of(mapCollection())),
      refreshMapStatus$: vi.fn(() => of(mapCollection())),
      getDetail$: vi.fn(() => of(detail())),
      searchManagerCandidates$: vi.fn(() => of([])),
      getAssignableManagers$: vi.fn(() => of([candidateUser()])),
      updateManager$: vi.fn(() => of(detail({ assignedManagerUserId: 22 }))),
      getCountryByCode: vi.fn((code: string) => ({ code, name: code, continent: '', flag: '' })),
    };

    await TestBed.configureTestingModule({
      imports: [TerritoryManagementComponent],
      providers: [
        { provide: TerritoryService, useValue: territoryService },
        { provide: AdminDashboardService, useValue: { loadDashboard: () => of({ badges: { pendingReportsCount: 0, supportReportsCount: 0 } }) } },
      ],
    })
      .overrideComponent(TerritoryManagementComponent, {
        set: {
          imports: [
            CommonModule,
            FormsModule,
            SidebarStubComponent,
            TopbarStubComponent,
            TerritoryWorldMapStubComponent,
            CountryMiniMapStubComponent,
          ],
        },
      })
      .compileComponents();

    fixture = TestBed.createComponent(TerritoryManagementComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('renders the edit modal with rich backend user rows and role pills instead of a select', () => {
    component.openEdit(detail());

    fixture.detectChanges();

    const text = normalizedText();
    expect(text).toContain('Edit Territory');
    expect(text).toContain('candidate@example.com');
    expect(text).toContain('Regional Manager');
    expect(text).toContain('Current');
    expect(fixture.nativeElement.querySelector('select')).toBeNull();
  });

  it('highlights the selected manager row and marks the current assignment', () => {
    component.openEdit(detail());

    fixture.detectChanges();

    const selected = fixture.nativeElement.querySelector('button.border-l-blue-600') as HTMLButtonElement | null;
    expect(selected?.textContent).toContain('Candidate User');
    expect(normalizedText()).toContain('Current');
  });

  it('saves manager changes through the backend update endpoint and updates detail state', () => {
    component.editTarget = detail();
    component.editAssignedManagerId = '22';

    component.saveEdit();

    expect(territoryService.updateManager$).toHaveBeenCalledWith(101, '22');
    expect(component.editTarget).toBeNull();
    expect(component.detailTarget?.assignedManagerUserId).toBe(22);
  });

  it('disables saving an assignment when boundary geometry is missing', () => {
    component.openEdit(detail({ hasBoundary: false, boundaryGeoJson: null }));
    component.editAssignedManagerId = '11';

    fixture.detectChanges();

    const save = findButton('Save Changes');
    expect(save?.disabled).toBe(true);
    expect(normalizedText()).toContain('Boundary missing');
  });

  it('loads backend map features instead of frontend country geometry', () => {
    expect(territoryService.getMap$).toHaveBeenCalled();
    expect(component.mapFeatureCollection?.features[0]?.properties.iso3).toBe('AAA');
    expect(component.mapFeatureCollection?.features[0]?.geometry?.type).toBe('Polygon');
  });

  it('does not render ISO3 import controls in the normal map UI', () => {
    const text = normalizedText();
    expect(text).not.toContain('ISO3');
    expect(text).not.toContain('Import boundary');
    expect(text).not.toContain('Enter a valid ISO3 code');
  });

  it('opens the map assignment flow for available backend features and assigns through the backend territory id', () => {
    component.onMapCountryClick({
      code: 'AA',
      iso2: 'AA',
      iso3: 'AAA',
      name: 'Backend Territory',
      territoryKind: 'Country',
      shapeGroup: 'AAA',
      shapeType: 'ADM0',
      shapeId: 'AAA-ADM0',
      territoryId: 101,
      status: 'Available',
      canAssign: true,
      hasBoundary: true,
      hasValidationBoundary: true,
      hasDisplayBoundary: true,
      boundaryRequiredMessage: null,
      boundaryMissingReason: null,
      geometry: polygonGeometry(),
    });
    component.selectedMapManager = candidateUser();

    expect(component.mapClickedCountry?.name).toBe('Backend Territory');
    component.confirmMapAssign();

    expect(territoryService.updateManager$).toHaveBeenCalledWith(101, 11);
    expect(territoryService.refreshMapStatus$).toHaveBeenCalled();
  });

  it('opens territory detail when an assigned backend map feature is clicked', () => {
    component.onMapCountryClick({
      code: 'AA',
      iso2: 'AA',
      iso3: 'AAA',
      name: 'Backend Territory',
      territoryKind: 'Country',
      shapeGroup: 'AAA',
      shapeType: 'ADM0',
      shapeId: 'AAA-ADM0',
      territoryId: 101,
      status: 'Assigned',
      canAssign: false,
      hasBoundary: true,
      hasValidationBoundary: true,
      hasDisplayBoundary: true,
      boundaryRequiredMessage: null,
      boundaryMissingReason: null,
      geometry: polygonGeometry(),
    });

    expect(territoryService.getDetail$).toHaveBeenCalledWith(101);
    expect(component.detailTarget?.territoryId).toBe(101);
    expect(component.mapClickedCountry).toBeNull();
  });

  it('tracks only assigned territories for map assignment styling', () => {
    component.territories = [
      detail({ assignedManagerId: null, assignedManagerUserId: null, assignedManager: null, status: 'Unassigned' }),
      detail({ id: 'TERR-102', territoryId: 102, countryCode: 'AB', countries: ['AB'] }),
    ];

    component['buildCountryAssignments']();

    expect(component.countryAssignments.has('AA')).toBe(false);
    expect(component.countryAssignments.has('AB')).toBe(true);
  });

  it('blocks map assignment when backend marks the boundary missing', () => {
    component.onMapCountryClick({
      code: 'AB',
      iso2: 'AB',
      iso3: 'AAB',
      name: 'Missing Boundary',
      territoryKind: 'Country',
      shapeGroup: 'AAB',
      shapeType: 'ADM0',
      shapeId: 'AAB-ADM0',
      territoryId: 102,
      status: 'MissingBoundary',
      canAssign: false,
      hasBoundary: false,
      hasValidationBoundary: false,
      hasDisplayBoundary: false,
      boundaryRequiredMessage: 'Boundary data is missing. Run the backend ADM0 boundary setup before assigning this country.',
      boundaryMissingReason: 'Boundary data is missing. Run the backend ADM0 boundary setup before assigning this country.',
      geometry: null,
    });

    expect(component.mapClickedCountry).toBeNull();
    expect(component.mapAssignError).toContain('backend ADM0 boundary setup');
  });

  it('opens disputed territory detail instead of the assignment modal', () => {
    territoryService.getDetail$.mockReturnValueOnce(of(detail({
      id: 'TERR-202',
      territoryId: 202,
      countryId: 202,
      name: 'Abyei',
      displayName: 'Abyei',
      territoryKind: 'DisputedTerritory',
      countryCode: null,
      iso2: null,
      iso3: null,
      countries: [],
      status: 'DisputedViewOnly',
      assignedManager: null,
      assignedManagerId: null,
      assignedManagerUserId: null,
      boundaryShapeType: 'DISP',
      boundaryAdmLevel: 'DISP',
      boundaryShapeGroup: '111',
      canAssign: false,
      boundaryRequiredMessage: 'This disputed boundary is shown for map completeness and cannot be assigned as a country territory.',
    })));

    component.onMapCountryClick({
      code: 'territory-202',
      iso2: null,
      iso3: null,
      name: 'Abyei',
      territoryKind: 'DisputedTerritory',
      shapeGroup: '111',
      shapeType: 'DISP',
      shapeId: 'ABYEI-DISP',
      territoryId: 202,
      status: 'DisputedViewOnly',
      canAssign: false,
      hasBoundary: true,
      hasValidationBoundary: true,
      hasDisplayBoundary: true,
      boundaryRequiredMessage: 'This disputed boundary is shown for map completeness and cannot be assigned as a country territory.',
      boundaryMissingReason: null,
      geometry: polygonGeometry(),
    });

    expect(component.mapClickedCountry).toBeNull();
    expect(territoryService.getDetail$).toHaveBeenCalledWith(202);
    expect(component.detailTarget?.territoryKind).toBe('DisputedTerritory');
    expect(component.detailTarget?.canAssign).toBe(false);
  });

  it('uses the explicit map refresh path for retry', () => {
    component.retryTerritoryMap();

    expect(territoryService.refreshMap$).toHaveBeenCalled();
  });

  function normalizedText(): string {
    return (fixture.nativeElement.textContent as string).replace(/\s+/g, ' ').trim();
  }

  function findButton(label: string): HTMLButtonElement | null {
    return Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>)
      .find(button => button.textContent?.includes(label)) ?? null;
  }

  function detail(overrides: Partial<TerritoryDetail> = {}): TerritoryDetail {
    return {
      id: 'TERR-101',
      territoryId: 101,
      countryId: 101,
      name: 'Backend Territory',
      displayName: 'Backend Territory',
      internalReference: 'TERR-101',
      territoryKind: 'Country',
      countryCode: 'AA',
      countries: ['AA'],
      assignedManagerUserId: 11,
      assignedManagerId: '11',
      assignedManagerName: 'Candidate User',
      assignedManagerEmail: 'candidate@example.com',
      assignedManager: {
        id: '11',
        userId: 11,
        displayName: 'Candidate User',
        name: 'Candidate User',
        email: 'candidate@example.com',
        avatarUrl: null,
        initials: 'CU',
        roles: ['TerritoryManager'],
        assignedAt: '2026-05-03T00:00:00Z',
      },
      status: 'Assigned',
      boundarySource: 'IntegrationTest',
      boundaryShapeType: 'Polygon',
      hasBoundary: true,
      boundaryGeoJson: {
        type: 'Polygon',
        coordinates: [
          [
            [20, 10],
            [21, 10],
            [21, 11],
            [20, 11],
            [20, 10],
          ],
        ],
      },
      viewport: { isEmpty: false },
      canAssign: true,
      boundaryRequiredMessage: null,
      createdAt: '2026-05-03T00:00:00Z',
      updatedAt: '2026-05-03T00:00:00Z',
      ...overrides,
    };
  }

  function mapCollection(): TerritoryMapFeatureCollection {
    return {
      type: 'FeatureCollection',
      attribution: 'Boundary data provided by geoBoundaries (gbOpen), licensed under CC BY 4.0.',
      generatedAt: '2026-05-25T00:00:00Z',
      features: [
        {
          type: 'Feature',
          geometry: polygonGeometry(),
          properties: {
            territoryId: 101,
            countryId: 101,
            iso2: 'AA',
            iso3: 'AAA',
            countryCode: 'AA',
            name: 'Backend Territory',
            displayName: 'Backend Territory',
            territoryKind: 'Country',
            status: 'Available',
            shapeGroup: 'AAA',
            shapeType: 'ADM0',
            shapeId: 'AAA-ADM0',
            assignedManager: null,
            hasBoundary: true,
            canAssign: true,
            canEdit: true,
            boundaryRequiredMessage: null,
            boundarySource: 'geoBoundaries',
            boundarySourceUrl: 'https://www.geoboundaries.org/api/current/gbOpen/AAA/ADM0/',
            boundaryShapeType: 'Polygon',
            boundaryAdmLevel: 'ADM0',
            boundaryReleaseType: 'gbOpen',
            boundaryLicense: 'CC BY 4.0',
            boundaryImportedAt: '2026-05-25T00:00:00Z',
            viewport: { isEmpty: false },
            createdAt: '2026-05-03T00:00:00Z',
            updatedAt: '2026-05-03T00:00:00Z',
          },
        },
      ],
    };
  }

  function polygonGeometry(): GeoJsonGeometry {
    return {
      type: 'Polygon',
      coordinates: [
        [
          [20, 10],
          [21, 10],
          [21, 11],
          [20, 11],
          [20, 10],
        ],
      ],
    };
  }

  function candidateUser(): TerritoryManager {
    return {
      id: '11',
      userId: 11,
      displayName: 'Candidate User',
      name: 'Candidate User',
      firstName: 'Candidate',
      lastName: 'User',
      email: 'candidate@example.com',
      avatarUrl: null,
      initials: 'CU',
      roles: ['RegionalManager'],
      assignedTerritories: [{ territoryId: 101, territoryName: 'Backend Territory', displayName: 'Backend Territory', countryCode: 'AA' }],
      avatarColor: '#3b82f6',
      assignedTerritoryIds: ['TERR-101'],
      status: 'Active',
      isActive: true,
      canAssign: true,
      ineligibilityReason: null,
    };
  }
});
