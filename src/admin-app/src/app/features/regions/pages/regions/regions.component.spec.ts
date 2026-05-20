import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of } from 'rxjs';
import { vi } from 'vitest';

import { AllRegionsComponent } from './regions.component';
import { AuthService } from '../../../../core/services/auth.service';
import { TerritoryDashboardService } from '../../../../core/services/territory-dashboard.service';
import {
  RegionListItem,
  RegionPageRequest,
  RegionsService,
} from '../../../../core/services/national/regions.service';

describe('AllRegionsComponent', () => {
  let component: AllRegionsComponent;
  let fixture: ComponentFixture<AllRegionsComponent>;
  let getPage$: ReturnType<typeof vi.fn>;

  const authService = {
    fullName: 'Mika Mikic',
    getUser: vi.fn(() => ({ role: 'Territory Manager', region: '' })),
    isTerritoryManager: vi.fn(() => true),
    canAccessTerritoryAdmin: vi.fn(() => true),
    logout: vi.fn(),
  };

  const territoryService = {
    getAssignedTerritories: vi.fn(() => of([{ id: 10, name: 'Croatia', displayName: 'Croatia' }])),
    getActiveTerritoryId: vi.fn(() => 10),
    setActiveTerritoryId: vi.fn(),
  };

  const makeRegion = (id: number): RegionListItem => ({
    id: String(id),
    regionId: id,
    name: `Region ${id}`,
    description: 'Regional Manager: Lead',
    territoryId: 10,
    territoryDisplayName: 'Croatia',
    destinations: id === 11 ? 4 : 0,
    locations: 2,
    places: 3,
    stays: 1,
    events: 5,
    assignedPersons: 1,
    managerNames: ['Lead Manager'],
    managerDisplayName: 'Lead Manager',
    assignedManagers: [
      {
        userId: id,
        displayName: 'Lead Manager',
        email: 'lead@example.com',
        initials: 'LM',
        avatarUrl: '/media/uploads/avatars/lead.png',
        role: 'RegionalManager',
        assignedAt: '2026-05-01T00:00:00Z',
      },
    ],
    status: 'Active',
    imageBg: 'linear-gradient(135deg,#dbeafe 0%,#bfdbfe 100%)',
    pendingRequests: 1,
    healthScore: 82,
    activityScore: 80,
    detailRoute: `/territory/regions/${id}`,
  });

  const allRegions = Array.from({ length: 30 }, (_, index) => makeRegion(index + 11));

  beforeEach(async () => {
    vi.clearAllMocks();

    getPage$ = vi.fn((req: RegionPageRequest) => {
      const filtered = req.search
        ? allRegions.filter(region => region.name.toLowerCase().includes(req.search.toLowerCase()))
        : allRegions;

      return of({
        items: filtered.slice(req.skip, req.skip + req.take),
        total: filtered.length,
        skip: req.skip,
        take: req.take,
        hasMore: req.skip + req.take < filtered.length,
        territory: { id: 10, name: 'Croatia', displayName: 'Croatia' },
      });
    });

    const regionsService = {
      getListState: vi.fn(() => null),
      clearListState: vi.fn(),
      saveListState: vi.fn(),
      setActiveTerritoryId: vi.fn(),
      getActiveTerritoryId: vi.fn(() => 10),
      getPage$,
    };

    await TestBed.configureTestingModule({
      imports: [AllRegionsComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: authService },
        { provide: TerritoryDashboardService, useValue: territoryService },
        { provide: RegionsService, useValue: regionsService },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AllRegionsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  });

  it('loads the first 25 regions from the backend service', () => {
    expect(component.items.length).toBe(25);
    expect(component.totalItems).toBe(30);
    expect(getPage$).toHaveBeenCalledWith({
      territoryId: 10,
      search: '',
      sort: 'status',
      skip: 0,
      take: 25,
    });
  });

  it('loads more regions with the next skip value', () => {
    component.loadMore();
    fixture.detectChanges();

    expect(getPage$).toHaveBeenLastCalledWith({
      territoryId: 10,
      search: '',
      sort: 'status',
      skip: 25,
      take: 25,
    });
    expect(component.items.length).toBe(30);
  });

  it('search triggers a backend query with the current search text', async () => {
    component.searchQuery = 'Region 12';
    component.onSearchChange();
    await new Promise(resolve => setTimeout(resolve, 320));
    fixture.detectChanges();

    expect(getPage$).toHaveBeenLastCalledWith({
      territoryId: 10,
      search: 'Region 12',
      sort: 'status',
      skip: 0,
      take: 25,
    });
  });

  it('sort triggers a backend query with the selected sort', () => {
    component.applySortOption({ label: 'Most Events', value: 'mostEvents' });

    expect(getPage$).toHaveBeenLastCalledWith({
      territoryId: 10,
      search: '',
      sort: 'mostEvents',
      skip: 0,
      take: 25,
    });
  });

  it('clicking a region navigates to the real detail route', () => {
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate');

    component.viewRegion(component.items[0]);

    expect(navigate).toHaveBeenCalledWith(['/territory/regions/11'], {
      state: { regionId: 11, territoryId: 10 },
    });
  });

  it('renders read-only rows without edit or delete actions', () => {
    const text = fixture.nativeElement.textContent;

    expect(text).toContain('Regional Management');
    expect(text).toContain('Region 11');
    expect(text).not.toContain('Delete Region');
    expect(text).not.toContain('Edit Region');
  });
});
