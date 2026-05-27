import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { vi } from 'vitest';

import { RegionDetailComponent } from './region-detail.component';
import { AuthService } from '../../../../../core/services/auth.service';
import { RegionsService } from '../../../../../core/services/national/regions.service';
import { TerritoryDashboardService } from '../../../../../core/services/territory-dashboard.service';
import { USER_APP_BASE_URL } from '../../../../../core/services/user-app-link.service';
import {
  TerritoryRegionContentItem,
  TerritoryRegionDetail,
} from '../../../../../core/models/dashboard.model';

describe('RegionDetailComponent', () => {
  let component: RegionDetailComponent;
  let fixture: ComponentFixture<RegionDetailComponent>;
  let getContent$: ReturnType<typeof vi.fn>;

  const authService = {
    fullName: 'Mika Mikic',
    getUser: vi.fn(() => ({ role: 'Territory Manager', region: '' })),
    isAdministrator: vi.fn(() => false),
    isTerritoryManager: vi.fn(() => true),
    canAccessTerritoryAdmin: vi.fn(() => true),
    logout: vi.fn(),
  };

  const territoryService = {
    getAssignedTerritories: vi.fn(() => of([{ id: 10, name: 'Croatia', displayName: 'Croatia' }])),
    getActiveTerritoryId: vi.fn(() => 10),
    setActiveTerritoryId: vi.fn(),
  };

  const destinationItem: TerritoryRegionContentItem = {
    id: 21,
    entityType: 'destination',
    publicDetailRoute: '/explore?destinationId=21',
    name: 'Real Destination',
    summary: 'Destination summary',
    description: 'Destination description',
    status: 'Published',
    regionId: 11,
    regionName: 'Dalmatia',
    destinationId: 21,
    destinationName: 'Real Destination',
    parentName: 'Dalmatia',
    typeName: 'Destination',
    createdAt: '2026-05-01T00:00:00Z',
    locationsCount: 2,
    placesCount: 3,
    staysCount: 1,
    eventsCount: 4,
    views: 15,
    saves: 4,
    externalClicks: 0,
    reviewsCount: 2,
    averageRating: 4.5,
    health: {
      score: 48,
      statusLabel: 'Needs attention',
      drivers: [{ label: 'Missing media', detail: 'No hero image is attached.', severity: 'warning' }],
      issues: [],
      recommendations: [{ label: 'Add image', detail: 'Add a hero image to improve visibility.', severity: 'action' }],
    },
  };

  const locationItem: TerritoryRegionContentItem = {
    id: 31,
    entityType: 'location',
    publicDetailRoute: null,
    name: 'Real Location',
    status: 'Published',
    regionId: 11,
    regionName: 'Dalmatia',
    destinationId: 21,
    destinationName: 'Real Destination',
    parentName: 'Real Destination',
    typeName: 'Location',
    createdAt: '2026-05-01T00:00:00Z',
    locationsCount: 0,
    placesCount: 3,
    staysCount: 1,
    eventsCount: 2,
    views: 7,
    saves: 1,
    externalClicks: 0,
    reviewsCount: 0,
  };

  const placeItem: TerritoryRegionContentItem = {
    id: 41,
    entityType: 'place',
    publicDetailRoute: '/places/41',
    name: 'Real Place',
    status: 'Published',
    regionId: 11,
    regionName: 'Dalmatia',
    destinationId: 21,
    destinationName: 'Real Destination',
    locationId: 31,
    locationName: 'Real Location',
    placeId: 41,
    placeName: 'Real Place',
    parentName: 'Real Location',
    typeName: 'Landmark',
    createdAt: '2026-05-01T00:00:00Z',
    locationsCount: 0,
    placesCount: 0,
    staysCount: 0,
    eventsCount: 0,
    views: 7,
    saves: 1,
    externalClicks: 0,
    reviewsCount: 0,
  };

  const stayItem: TerritoryRegionContentItem = {
    ...placeItem,
    id: 51,
    entityType: 'stay',
    publicDetailRoute: '/stays/51',
    name: 'Real Stay',
    placeId: 51,
    placeName: 'Real Stay',
    typeName: 'Hotel',
  };

  const eventItem: TerritoryRegionContentItem = {
    id: 61,
    entityType: 'event',
    publicDetailRoute: '/events/61',
    name: 'Real Event',
    status: 'Published',
    regionId: 11,
    regionName: 'Dalmatia',
    destinationId: 21,
    destinationName: 'Real Destination',
    locationId: 31,
    locationName: 'Real Location',
    parentName: 'Real Location',
    typeName: 'Festival',
    createdAt: '2026-05-01T00:00:00Z',
    startsAt: '2026-06-01T00:00:00Z',
    locationsCount: 0,
    placesCount: 0,
    staysCount: 0,
    eventsCount: 0,
    views: 7,
    saves: 1,
    externalClicks: 0,
    reviewsCount: 0,
  };

  const unavailableItem: TerritoryRegionContentItem = {
    ...placeItem,
    id: 71,
    publicDetailRoute: null,
    name: 'Draft Place',
    status: 'Draft',
  };

  const detail: TerritoryRegionDetail = {
    region: {
      id: 11,
      name: 'Dalmatia',
      displayName: 'Dalmatia',
      description: null,
      status: 'Active',
      territoryId: 10,
      territoryDisplayName: 'Croatia',
    },
    managers: [
      {
        userId: 3,
        displayName: 'Lead Manager',
        email: 'lead@example.com',
        initials: 'LM',
        avatarUrl: '/media/uploads/avatars/lead.png',
        role: 'Regional Manager',
        assignedAt: '2026-05-01T00:00:00Z',
      },
      {
        userId: 4,
        displayName: 'No Avatar Manager',
        email: 'fallback@example.com',
        initials: 'NA',
        avatarUrl: null,
        role: 'Regional Manager',
        assignedAt: '2026-05-02T00:00:00Z',
      },
    ],
    summary: {
      destinationsCount: 1,
      locationsCount: 1,
      placesCount: 2,
      staysCount: 1,
      eventsCount: 3,
      pendingRequestsCount: 1,
      activeUsers: 5,
      visits: 20,
      saves: 6,
      reviews: 2,
      averageRating: 4.5,
    },
    health: {
      score: 68,
      statusLabel: 'Moderate',
      drivers: [{ label: 'Coverage', detail: 'Content coverage is growing.', severity: 'info' }],
      issues: [],
      recommendations: [{ label: 'Review queue', detail: 'Review pending contributions.', severity: 'action' }],
    },
    content: {
      destinations: [destinationItem],
      locations: [locationItem],
      places: [placeItem],
      stays: [stayItem],
      events: [eventItem],
    },
    analytics: {
      topEntities: [{ entityType: 'destination', entityId: 21, name: 'Real Destination', views: 15 }],
      recentActivity: [],
    },
  };

  beforeEach(async () => {
    vi.clearAllMocks();

    getContent$ = vi.fn((request) => {
      const itemsByType: Record<string, TerritoryRegionContentItem[]> = {
        destinations: [destinationItem],
        locations: [locationItem],
        places: [placeItem],
        stays: [stayItem],
        events: [eventItem],
        all: [destinationItem, locationItem, placeItem, stayItem, eventItem],
      };

      return of({
        entityType: request.entityType,
        items: itemsByType[request.entityType as string] ?? [destinationItem],
        totalCount: 1,
        skip: request.skip,
        take: request.take,
        hasMore: false,
      });
    });

    const regionsService = {
      getActiveTerritoryId: vi.fn(() => 10),
      setActiveTerritoryId: vi.fn(),
      getDetail$: vi.fn(() => of(detail)),
      getContent$,
    };

    await TestBed.configureTestingModule({
      imports: [RegionDetailComponent],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: '11' }) } } },
        { provide: AuthService, useValue: authService },
        { provide: RegionsService, useValue: regionsService },
        { provide: TerritoryDashboardService, useValue: territoryService },
        { provide: USER_APP_BASE_URL, useValue: 'https://user.example' },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(RegionDetailComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  });

  it('renders region summary and content tabs', () => {
    const text = fixture.nativeElement.textContent;

    expect(component).toBeTruthy();
    expect(text).toContain('Dalmatia');
    expect(text).toContain('Destinations');
    expect(text).toContain('Locations');
    expect(text).toContain('Stays');
    expect(text).toContain('Events');
    expect(text).toContain('Read-only');
  });

  it('loads the selected tab through the territory content endpoint', () => {
    component.setActiveTab('locations');
    fixture.detectChanges();

    expect(getContent$).toHaveBeenLastCalledWith({
      regionId: 11,
      territoryId: 10,
      entityType: 'locations',
      search: '',
      sort: 'name',
      skip: 0,
      take: 25,
    });
    expect(component.contentItems[0].name).toBe('Real Location');
  });

  it('renders destination preview links with safe new-tab attributes', () => {
    const link = fixture.nativeElement.querySelector('a[href="https://user.example/explore?destinationId=21"]') as HTMLAnchorElement | null;

    expect(link?.textContent?.trim()).toBe('Real Destination');
    expect(link?.getAttribute('target')).toBe('_blank');
    expect(link?.getAttribute('rel')).toContain('noopener');
    expect(link?.getAttribute('rel')).toContain('noreferrer');
  });

  it('renders place, stay, and event preview links from backend public routes', () => {
    component.setActiveTab('places');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('a[href="https://user.example/places/41"]')).not.toBeNull();

    component.setActiveTab('stays');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('a[href="https://user.example/stays/51"]')).not.toBeNull();

    component.setActiveTab('events');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('a[href="https://user.example/events/61"]')).not.toBeNull();
  });

  it('renders missing public routes as non-links', () => {
    getContent$.mockImplementationOnce((request) => of({
      entityType: request.entityType,
      items: [unavailableItem],
      totalCount: 1,
      skip: request.skip,
      take: request.take,
      hasMore: false,
    }));
    component.setActiveTab('places');
    fixture.detectChanges();
    const disabledName = fixture.nativeElement.querySelector('span[aria-disabled="true"]') as HTMLSpanElement | null;

    expect(fixture.nativeElement.querySelector('a[href*="Draft%20Place"]')).toBeNull();
    expect(disabledName?.textContent).toContain('Draft Place');
  });

  it('renders locations without a user-app public route as disabled text', () => {
    component.setActiveTab('locations');
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('a[href*="locations"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('span[aria-disabled="true"]')?.textContent).toContain('Real Location');
  });

  it('does not use admin edit routes as public preview links', () => {
    getContent$.mockImplementationOnce((request) => of({
      entityType: request.entityType,
      items: [{ ...placeItem, publicDetailRoute: '/admin/places/detail/41' }],
      totalCount: 1,
      skip: request.skip,
      take: request.take,
      hasMore: false,
    }));
    component.setActiveTab('places');
    fixture.detectChanges();
    const disabledName = fixture.nativeElement.querySelector('span[aria-disabled="true"]') as HTMLSpanElement | null;

    expect(fixture.nativeElement.querySelector('a[href*="/admin/places/detail/41"]')).toBeNull();
    expect(disabledName?.textContent).toContain('Real Place');
  });

  it('expands destination health details on demand', () => {
    component.toggleDestinationHealth(component.contentItems[0], new MouseEvent('click'));
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent;
    expect(text).toContain('No hero image is attached.');
    expect(text).toContain('Add a hero image to improve visibility.');
  });

  it('does not render edit or delete controls for Territory Manager detail', () => {
    const text = fixture.nativeElement.textContent;

    expect(text).not.toContain('Edit Region');
    expect(text).not.toContain('Delete');
    expect(text).not.toContain('Assign User');
  });

  it('renders manager avatar images and initials fallback', () => {
    const avatar = fixture.nativeElement.querySelector('img[alt="Lead Manager profile image"]') as HTMLImageElement | null;

    expect(avatar?.getAttribute('src')).toBe('/media/uploads/avatars/lead.png');
    expect(fixture.nativeElement.textContent).toContain('NA');
  });

  it('falls back to initials when a manager image fails', () => {
    component.onManagerAvatarError(detail.managers[0]);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('img[alt="Lead Manager profile image"]')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('LM');
  });
});
