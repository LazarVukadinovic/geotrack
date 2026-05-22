import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of } from 'rxjs';
import { vi } from 'vitest';

import { NationalDashboardComponent } from './national-dashboard.component';
import { AuthService } from '../../../../core/services/auth.service';
import { TerritoryDashboardService } from '../../../../core/services/territory-dashboard.service';
import { TerritoryDashboardResponse } from '../../../../core/models/dashboard.model';

describe('NationalDashboardComponent', () => {
  let component: NationalDashboardComponent;
  let fixture: ComponentFixture<NationalDashboardComponent>;

  const dashboard: TerritoryDashboardResponse = {
    activeTerritory: { id: 10, name: 'Croatia', displayName: 'Croatia' },
    stats: {
      totalRegions: 2,
      regionsAddedLastMonth: 0,
      activeUsers: 7,
      activeUsersChangePercent: 40,
      totalDestinations: 3,
      destinationsAddedLastMonth: 1,
      totalLocations: 4,
      locationsAddedLastMonth: 2,
      totalPlaces: 5,
      placesAddedLastMonth: 1,
      totalEvents: 6,
      eventsAddedLastMonth: 1,
    },
    regionsHealth: [
      {
        regionId: 11,
        regionName: 'Dalmatia',
        managerDisplayName: 'Regional Lead',
        destinationsCount: 3,
        locationsCount: 4,
        placesCount: 5,
        staysCount: 0,
        eventsCount: 6,
        pendingRequestsCount: 1,
        healthScore: 82,
        health: {
          score: 82,
          statusLabel: 'Healthy',
          drivers: [],
          issues: [],
          recommendations: [],
        },
        activityScore: 82,
        assignedManagers: [
          {
            userId: 3,
            displayName: 'Regional Lead',
            email: 'lead@example.com',
            initials: 'RL',
            avatarUrl: '/media/uploads/avatars/lead.png',
            role: 'RegionalManager',
            assignedAt: '2026-05-01T00:00:00Z',
          },
          {
            userId: 4,
            displayName: 'No Avatar',
            email: 'no-avatar@example.com',
            initials: 'NA',
            avatarUrl: null,
            role: 'RegionalManager',
            assignedAt: '2026-05-02T00:00:00Z',
          },
          {
            userId: 5,
            displayName: 'Third Manager',
            email: 'third@example.com',
            initials: 'TM',
            avatarUrl: null,
            role: 'RegionalManager',
            assignedAt: '2026-05-03T00:00:00Z',
          },
          {
            userId: 6,
            displayName: 'Fourth Manager',
            email: 'fourth@example.com',
            initials: 'FM',
            avatarUrl: null,
            role: 'RegionalManager',
            assignedAt: '2026-05-04T00:00:00Z',
          },
        ],
        assignedManagersPreview: ['RL'],
      },
    ],
    topPerformingRegions: [
      { regionId: 11, regionName: 'Dalmatia', visitsThisMonth: 42, changePercent: 10, rank: 1 },
    ],
    topContent: {
      topDestinations: [
        { id: 21, name: 'Real Destination', regionName: 'Dalmatia', metricValue: 14, metricLabel: 'views' },
      ],
      topLocations: [
        { id: 31, name: 'Real Location', regionName: 'Dalmatia', metricValue: 9, metricLabel: 'views' },
      ],
      topPlaces: [
        { id: 41, name: 'Real Place', regionName: 'Dalmatia', metricValue: 8, metricLabel: 'views' },
      ],
      upcomingEvents: [
        {
          id: 51,
          name: 'Real Event',
          regionName: 'Dalmatia',
          metricValue: 3,
          metricLabel: 'views',
          startsAt: '2026-06-10T00:00:00Z',
        },
      ],
    },
  };

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
    getDashboard: vi.fn(() => of(dashboard)),
  };

  beforeEach(async () => {
    vi.clearAllMocks();
    await TestBed.configureTestingModule({
      imports: [NationalDashboardComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: authService },
        { provide: TerritoryDashboardService, useValue: territoryService },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(NationalDashboardComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('renders backend territory stats and sections', () => {
    const text = fixture.nativeElement.textContent;

    expect(text).toContain('Croatia');
    expect(text).toContain('Total Regions');
    expect(text).toContain('Active Users');
    expect(text).toContain('2');
    expect(text).toContain('Real Destination');
    expect(text).toContain('Real Event');
    expect(text).toContain('Dalmatia');
  });

  it('navigates content stat cards to the read-only territory directory', () => {
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate');

    component.navigateStat(component.stats.find(stat => stat.label === 'Total Destinations')!);

    expect(navigate).toHaveBeenCalledWith(['/territory/content'], {
      queryParams: { type: 'destinations' },
    });
  });

  it('renders manager avatar images and initials fallback in region cards', () => {
    const avatar = fixture.nativeElement.querySelector('img[alt="Regional Lead profile image"]') as HTMLImageElement | null;
    const moreBadge = fixture.nativeElement.querySelector('[aria-label="Additional regional managers"]') as HTMLElement | null;

    expect(avatar?.getAttribute('src')).toBe('/media/uploads/avatars/lead.png');
    expect(fixture.nativeElement.textContent).toContain('NA');
    expect(moreBadge?.textContent?.trim()).toBe('+1');
  });

  it('falls back to initials when an overview manager avatar fails to load', () => {
    component.onManagerAvatarError(dashboard.regionsHealth[0].assignedManagers[0]);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('img[alt="Regional Lead profile image"]')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('RL');
  });

  it('uses button semantics and opens a region detail route from the card', () => {
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate');
    const card = fixture.nativeElement.querySelector('button[aria-label="Open Dalmatia region detail"]') as HTMLButtonElement;

    expect(card.tagName).toBe('BUTTON');

    card.click();
    expect(navigate).toHaveBeenCalledWith(['/territory/regions', 11], {
      state: { regionId: 11, territoryId: 10 },
    });
  });
});
