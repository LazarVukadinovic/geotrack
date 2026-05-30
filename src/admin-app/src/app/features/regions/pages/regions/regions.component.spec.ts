import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { BehaviorSubject, of } from 'rxjs';
import { vi } from 'vitest';

import { AllRegionsComponent } from './regions.component';
import { AuthService } from '../../../../core/services/auth.service';
import { TerritoryDashboardService } from '../../../../core/services/territory-dashboard.service';
import {
  TerritoryRegionSetupService,
  TerritoryRegionSetupState,
} from '../../../../core/services/territory-region-setup.service';
import {
  RegionListItem,
  RegionPageRequest,
  RegionsService,
} from '../../../../core/services/national/regions.service';
import { TerritoryRegionSetupStatus } from '../../../../core/models/dashboard.model';

describe('AllRegionsComponent', () => {
  let component: AllRegionsComponent;
  let fixture: ComponentFixture<AllRegionsComponent>;
  let getPage$: ReturnType<typeof vi.fn>;
  let syncFromAdm1$: ReturnType<typeof vi.fn>;
  let refreshSetupStatus: ReturnType<typeof vi.fn>;
  let setupStatus: TerritoryRegionSetupStatus;
  let setupState$: BehaviorSubject<TerritoryRegionSetupState>;

  const authService = {
    fullName: 'Mika Mikic',
    getUser: vi.fn(() => ({
      id: '42',
      firstName: 'Mika',
      lastName: 'Mikic',
      email: 'mika@example.com',
      role: 'Territory Manager',
      activeRoleCode: 'TerritoryManager',
      region: '',
    })),
    getActiveAdminView: vi.fn(() => ({
      roleCode: 'TerritoryManager',
      label: 'Territory Manager',
      dashboardRoute: '/territory/overview',
    })),
    getAvailableAdminViews: vi.fn(() => [{
      roleCode: 'TerritoryManager',
      label: 'Territory Manager',
      dashboardRoute: '/territory/overview',
    }]),
    isAdministrator: vi.fn(() => false),
    isContentModerator: vi.fn(() => false),
    isTerritoryManager: vi.fn(() => true),
    isTerritoryAssociate: vi.fn(() => false),
    isRegionalManager: vi.fn(() => false),
    canAccessTerritoryAdmin: vi.fn(() => true),
    logout: vi.fn(),
  };

  const territoryService = {
    getAssignedTerritories: vi.fn(() => of([{ id: 10, name: 'Croatia', displayName: 'Croatia' }])),
    getActiveTerritoryId: vi.fn(() => 10),
    setActiveTerritoryId: vi.fn(),
    notifyRegionSetupChanged: vi.fn(),
    regionSetupChanged$: of(undefined),
  };

  const makeRegion = (id: number, patch: Partial<RegionListItem> = {}): RegionListItem => ({
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
    ...patch,
  });

  const allRegions = Array.from({ length: 30 }, (_, index) => {
    const id = index + 11;
    return makeRegion(id, id === 36
      ? {
          representativeImage: {
            url: '/media/test/region-36.webp',
            altText: 'Region 36 representative',
            sourceType: 'Place',
            sourceId: 360,
            sourceName: 'Region 36 Place',
          },
          imageUrl: '/media/test/region-36.webp',
        }
      : {});
  });

  beforeEach(async () => {
    vi.clearAllMocks();
    setupStatus = {
      territoryId: 10,
      territoryName: 'Croatia',
      countryIso3: 'HRV',
      requiresInitialSync: false,
      onboardingCompleted: true,
      hasSyncedRegions: true,
      syncedRegionCount: 21,
      candidateCount: 21,
      hasCandidateData: true,
      canSync: true,
      canUpdate: true,
      updateAvailable: true,
      missingCandidateData: false,
      regionSetupCompletedAt: '2026-05-01T00:00:00Z',
      lastSyncedAt: '2026-05-01T00:00:00Z',
      candidateVersionHash: 'candidate-hash',
      syncedVersionHash: 'synced-hash',
      message: 'ADM1 boundary data has changed. Update regions to apply the latest boundaries.',
    };
    setupState$ = new BehaviorSubject<TerritoryRegionSetupState>({
      setupStatus,
      setupCheckInProgress: false,
      setupCheckedForCurrentSession: true,
      modalOpen: false,
      syncInProgress: false,
      setupCompleted: true,
      setupError: '',
    });

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
    syncFromAdm1$ = vi.fn(() => of({
      territoryId: 10,
      territoryName: 'Croatia',
      countryIso3: 'HRV',
      totalCandidates: 21,
      existingLinkedCount: 1,
      linkedExistingCount: 0,
      createdCount: 20,
      updatedCount: 0,
      skippedCount: 0,
      ambiguousCount: 0,
      failedCount: 0,
      dryRun: false,
      items: [],
    }));
    refreshSetupStatus = vi.fn(() => {
      setupState$.next({
        setupStatus,
        setupCheckInProgress: false,
        setupCheckedForCurrentSession: true,
        modalOpen: false,
        syncInProgress: false,
        setupCompleted: setupStatus.onboardingCompleted,
        setupError: '',
      });
      return of(setupStatus);
    });

    const regionsService = {
      getListState: vi.fn(() => null),
      clearListState: vi.fn(),
      saveListState: vi.fn(),
      setActiveTerritoryId: vi.fn(),
      getActiveTerritoryId: vi.fn(() => 10),
      getPage$,
      syncFromAdm1$,
    };

    await TestBed.configureTestingModule({
      imports: [AllRegionsComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: authService },
        { provide: TerritoryDashboardService, useValue: territoryService },
        {
          provide: TerritoryRegionSetupService,
          useValue: {
            state$: setupState$.asObservable(),
            refreshSetupStatus,
          },
        },
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

  it('renders sync and assignment actions for Territory Managers', () => {
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate');
    const text = fixture.nativeElement.textContent;

    expect(text).toContain('Update Regions');
    expect(text).toContain('Assign Manager');
    expect(text).not.toContain('Add Region');

    component.goToUserAdministration();

    expect(navigate).toHaveBeenCalledWith(['/territory/users'], {
      state: { territoryId: 10 },
    });
  });

  it('syncs ADM1 regions and reloads the list', () => {
    component.syncRegions();
    fixture.detectChanges();

    expect(syncFromAdm1$).toHaveBeenCalledWith(10, { dryRun: false });
    expect(refreshSetupStatus).toHaveBeenCalledWith(10);
    expect(component.feedbackMessage).toContain('21 ADM1 candidates checked');
    expect(getPage$).toHaveBeenCalledTimes(2);
  });

  it('disables Update Regions when backend reports regions are up to date', () => {
    setupStatus = {
      ...setupStatus,
      canUpdate: false,
      updateAvailable: false,
      message: 'Regions are up to date.',
    };
    setupState$.next({
      setupStatus,
      setupCheckInProgress: false,
      setupCheckedForCurrentSession: true,
      modalOpen: false,
      syncInProgress: false,
      setupCompleted: true,
      setupError: '',
    });
    fixture.detectChanges();

    const buttons = Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[];
    const updateButton = buttons.find(button => button.textContent?.includes('Update Regions'));

    expect(updateButton).toBeTruthy();
    expect(updateButton!.disabled).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('Regions are up to date.');
  });

  it('enables Update Regions when backend reports candidate changes', () => {
    fixture.detectChanges();

    const buttons = Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[];
    const updateButton = buttons.find(button => button.textContent?.includes('Update Regions'));

    expect(updateButton).toBeTruthy();
    expect(updateButton!.disabled).toBe(false);
    expect(fixture.nativeElement.textContent).toContain('ADM1 boundary data has changed.');
  });

  it('renders read-only rows without edit or delete actions', () => {
    const text = fixture.nativeElement.textContent;

    expect(text).toContain('Regional Management');
    expect(text).toContain('Region 11');
    expect(text).not.toContain('Delete Region');
    expect(text).not.toContain('Edit Region');
  });

  it('displays a representative image when one is available', () => {
    component.items = [
      makeRegion(101, {
        representativeImage: {
          url: '/media/test/region-thumb.webp',
          altText: 'Representative place thumbnail',
          sourceType: 'Place',
          sourceId: 501,
          sourceName: 'Representative Place',
        },
        imageUrl: '/media/test/region-thumb.webp',
      }),
    ];
    component.totalItems = 1;
    fixture.detectChanges();

    const image = fixture.nativeElement.querySelector('img[src="/media/test/region-thumb.webp"]') as HTMLImageElement | null;

    expect(image).toBeTruthy();
    expect(image!.alt).toBe('Representative place thumbnail');
  });

  it('keeps the placeholder when no representative image is available', () => {
    component.items = [makeRegion(102)];
    component.totalItems = 1;
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('img[alt="Region 102"]')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Region 102');
  });

  it('falls back to the placeholder when a representative image fails to load', () => {
    const item = makeRegion(103, {
      representativeImage: {
        url: '/media/test/broken-region.webp',
        altText: 'Broken representative thumbnail',
        sourceType: 'Destination',
        sourceId: 503,
        sourceName: 'Broken Destination',
      },
      imageUrl: '/media/test/broken-region.webp',
    });
    component.items = [item];
    component.totalItems = 1;
    fixture.detectChanges();

    expect(component.hasImage(item)).toBe(true);
    component.onImageError(item);
    fixture.detectChanges();

    expect(component.hasImage(item)).toBe(false);
    expect(fixture.nativeElement.querySelector('img[src="/media/test/broken-region.webp"]')).toBeNull();
  });

  it('renders representative images for lazy-loaded rows', () => {
    component.loadMore();
    fixture.detectChanges();

    const image = fixture.nativeElement.querySelector('img[src="/media/test/region-36.webp"]') as HTMLImageElement | null;

    expect(component.items.some(item => item.regionId === 36)).toBe(true);
    expect(image).toBeTruthy();
  });
});
