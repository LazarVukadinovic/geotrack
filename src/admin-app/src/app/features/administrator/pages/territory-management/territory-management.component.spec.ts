import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormsModule } from '@angular/forms';
import { of } from 'rxjs';
import { vi } from 'vitest';

import { AdminDashboardService } from '../../../../core/services/administrator/admin-dashboard.service';
import { TerritoryService } from '../../../../core/services/administrator/territory.service';
import { GeoJsonGeometry, TerritoryDetail, TerritoryManager } from '../../../../core/models/administrator/territory.model';
import { TerritoryManagementComponent } from './territory-management.component';
import { CountryMapAssignment, CountryMapClick } from './territory-world-map.component';

@Component({
  selector: 'app-sidebar',
  standalone: true,
  template: '<aside></aside>',
})
class SidebarStubComponent {
  @Input() reportedReviewsCount: number | null = null;
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
  @Input() assignments: Map<string, CountryMapAssignment> = new Map();
  @Input() highlightCode = '';
  @Output() countryClick = new EventEmitter<CountryMapClick>();
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
      searchManagerCandidates$: vi.fn(() => of([])),
      getAssignableManagers$: vi.fn(() => of([candidateUser()])),
      updateManager$: vi.fn(() => of(detail({ assignedManagerUserId: 22 }))),
      getCountryByCode: vi.fn((code: string) => ({ code, name: code, continent: '', flag: '' })),
    };

    await TestBed.configureTestingModule({
      imports: [TerritoryManagementComponent],
      providers: [
        { provide: TerritoryService, useValue: territoryService },
        { provide: AdminDashboardService, useValue: { loadDashboard: () => of({ badges: { pendingReportsCount: 0 } }) } },
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
