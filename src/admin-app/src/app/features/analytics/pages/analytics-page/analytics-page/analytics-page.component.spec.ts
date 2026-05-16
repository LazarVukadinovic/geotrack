import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { AnalyticsPageComponent } from './analytics-page.component';
import { AuthService } from '../../../../../core/services/auth.service';
import { DashboardService } from '../../../../../core/services/dashboard.service';
import { RegionalAnalyticsService } from '../../../../../core/services/regional-analytics';
import {
  CategoryFilter,
  MetricCard,
  RegionalAnalyticsEntityMetric,
  RegionalAnalyticsOverview,
  RegionalMonthlyVisits,
  RegionalMonthlyVisitYearsBack,
} from '../../../../../core/models/regional-analytics.model';

describe('AnalyticsPageComponent', () => {
  let fixture: ComponentFixture<AnalyticsPageComponent>;
  let component: AnalyticsPageComponent;
  let analyticsService: RegionalAnalyticsService;

  const region = { id: 21, name: 'Assigned', displayName: 'Assigned Region', territoryName: 'Testland' };

  beforeEach(async () => {
    sessionStorage.clear();

    analyticsService = {
      getOverview: vi.fn(() => of(overviewResponse())),
      getMonthlyVisits: vi.fn((params: { selectedYearsBack: RegionalMonthlyVisitYearsBack }) =>
        of(monthlyVisitsResponse(params.selectedYearsBack))),
      exportCsv: vi.fn(() => of(new Blob(['csv'], { type: 'text/csv' }))),
      downloadCsv: vi.fn(),
      buildMetricCards: vi.fn(() => metricCards()),
      mapEntityRows: vi.fn((rows: RegionalAnalyticsEntityMetric[]) => rows.map(row => ({
        entityType: row.entityType,
        entityId: row.entityId,
        name: row.name,
        type: row.entityType === 'location' ? 'LOCATION' : 'PLACE',
        location: row.regionDisplayName,
        views: row.pageViews,
        visitors: row.uniqueVisitors,
        saves: row.saves,
        actions: row.externalClicks,
        reviewAvg: row.averageRating,
        reviewCount: row.reviewCount,
        trend: 'up',
        imageBg: '#fff',
        imageUrl: row.imageUrl,
        status: row.status,
        detailRoute: row.detailRoute,
      }))),
      mapDistribution: vi.fn(() => [{ label: 'Places', count: 1, color: '#1252A0' }]),
      mapTrend: vi.fn(() => [12]),
      buildActionableInsights: vi.fn(() => [
        { icon: 'pin', label: 'TOP VIEWED LOCATION', title: 'Real Location', subtitle: '12 Views' },
      ]),
      buildAutoInsights: vi.fn(() => []),
      asBucket: vi.fn((filter: CategoryFilter, value: unknown, empty: unknown) => ({
        all: filter === 'all' ? value : empty,
        locations: filter === 'locations' ? value : empty,
        destinations: filter === 'destinations' ? value : empty,
        events: filter === 'events' ? value : empty,
        places: filter === 'places' ? value : empty,
      })),
    } as unknown as RegionalAnalyticsService;

    await TestBed.configureTestingModule({
      imports: [AnalyticsPageComponent],
      providers: [
        provideRouter([]),
        {
          provide: DashboardService,
          useValue: {
            getAssignedRegions: () => of([region]),
            getSidebarSummary: () => of({
              assignedRegions: [region],
              activeRegion: region,
              pendingRequestsCount: 6,
            }),
          },
        },
        {
          provide: AuthService,
          useValue: {
            fullName: 'Regional User',
            getUser: () => ({ role: 'Regional Manager' }),
            isNationalManager: () => false,
            isRegionalManager: () => true,
          },
        },
        { provide: RegionalAnalyticsService, useValue: analyticsService },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AnalyticsPageComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => {
    sessionStorage.clear();
  });

  it('loads active region analytics from the backend service', () => {
    fixture.detectChanges();

    expect(component.activeRegionId).toBe(21);
    expect(component.regionDisplayName).toBe('Assigned Region');
    expect(component.pendingRequestsCount).toBe(6);
    expect(analyticsService.getOverview).toHaveBeenCalledWith(expect.objectContaining({
      regionId: 21,
      period: '30d',
      entityType: 'all',
    }));
    expect(analyticsService.getMonthlyVisits).toHaveBeenCalledWith(expect.objectContaining({
      regionId: 21,
      selectedYearsBack: 1,
      entityType: 'all',
    }));
  });

  it('reloads analytics when entity type changes', () => {
    fixture.detectChanges();
    vi.mocked(analyticsService.getOverview).mockClear();

    component.selectedType = 'Location';

    expect(analyticsService.getOverview).toHaveBeenCalledWith(expect.objectContaining({
      entityType: 'location',
      take: 25,
    }));
    expect(analyticsService.getMonthlyVisits).toHaveBeenCalledWith(expect.objectContaining({
      entityType: 'location',
      selectedYearsBack: 1,
    }));
  });

  it('renders monthly visits and reloads when the chart range changes', () => {
    fixture.detectChanges();
    vi.mocked(analyticsService.getMonthlyVisits).mockClear();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Monthly Visits Over Time');
    expect(component.selectedMonthlyVisitYears).toBe(1);

    component.onMonthlyVisitYearsChange(2);

    expect(component.selectedMonthlyVisitYears).toBe(2);
    expect(analyticsService.getMonthlyVisits).toHaveBeenCalledWith(expect.objectContaining({
      selectedYearsBack: 2,
      regionId: 21,
    }));
  });

  it('exports current filtered analytics data', () => {
    fixture.detectChanges();

    component.exportData();

    expect(analyticsService.exportCsv).toHaveBeenCalledWith(expect.objectContaining({
      regionId: 21,
      period: '30d',
    }));
    expect(analyticsService.downloadCsv).toHaveBeenCalled();
  });

  it('renders visitor behavior metrics from backend data', () => {
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('Visitor Behavior');
    expect(text).toContain('Logged-in users');
    expect(text).toContain('Anonymous visitors');
    expect(text).toContain('Real Place');
    expect(text).toContain('2m 14s');
  });

  function metricCards(): MetricCard[] {
    return [
      { label: 'Page Views', value: '12', delta: 0, deltaLabel: 'vs previous period', icon: 'trend' },
    ];
  }

  function overviewResponse(): RegionalAnalyticsOverview {
    return {
      activeRegion: region,
      period: {
        key: '30d',
        startDate: '2026-04-15T00:00:00Z',
        endDate: '2026-05-15T00:00:00Z',
      },
      totals: {
        pageViews: 12,
        uniqueVisitors: 3,
        saves: 2,
        averageRating: 4.4,
        externalClicks: 1,
        totalReviews: 1,
      },
      comparison: {
        pageViewsChangePercent: 0,
        uniqueVisitorsChangePercent: 0,
        savesChangePercent: 0,
        averageRatingChangePercent: 0,
        externalClicksChangePercent: 0,
        totalReviewsChangePercent: 0,
      },
      pageViewsTrend: [{ date: '2026-05-15T00:00:00Z', views: 12, uniqueVisitors: 3 }],
      topPerformingEntities: [
        {
          entityType: 'location',
          entityId: 4,
          name: 'Real Location',
          regionDisplayName: 'Assigned Region',
          imageUrl: null,
          pageViews: 12,
          uniqueVisitors: 3,
          saves: 0,
          externalClicks: 0,
          averageRating: 0,
          reviewCount: 0,
          status: 'Published',
        },
      ],
      entityTypeDistribution: [{ entityType: 'place', label: 'Places', count: 1 }],
      engagementFunnel: {
        pageViews: 12,
        uniqueVisitors: 3,
        saves: 2,
        externalClicks: 1,
        reviews: 1,
      },
      actionableInsights: {
        topViewedLocation: null,
        mostSaved: null,
        highestPerformingEvent: null,
        messages: [],
      },
      visitorBehavior: {
        loggedInVisitors: 2,
        anonymousVisitors: 1,
        returningVisitors: 1,
        returningVisitorRatePercent: 33.3,
        topTimeSpentEntity: {
          pageOrEntityName: 'Real Place',
          entityType: 'place',
          entityId: 8,
          routeOrPage: '/places/:id',
          averageTimeSpentSeconds: 134,
          totalTimeSpentSeconds: 268,
          visitCount: 2,
        },
        topTimeSpentPages: [],
        hasTimeSpentData: true,
        notes: [],
      },
      entityRows: [
        {
          entityType: 'place',
          entityId: 8,
          name: 'Real Place',
          regionDisplayName: 'Assigned Region',
          imageUrl: null,
          pageViews: 12,
          uniqueVisitors: 3,
          saves: 2,
          externalClicks: 1,
          averageRating: 4.4,
          reviewCount: 1,
          status: 'Published',
        },
      ],
      pagination: {
        totalCount: 1,
        skip: 0,
        take: 25,
        hasMore: false,
      },
      pendingRequestsCount: 6,
    };
  }

  function monthlyVisitsResponse(selectedYearsBack: RegionalMonthlyVisitYearsBack = 1): RegionalMonthlyVisits {
    return {
      activeRegion: region,
      selectedYearsBack,
      monthLabels: ['Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec', 'Jan', 'Feb', 'Mar', 'Apr', 'May'],
      series: Array.from({ length: selectedYearsBack }, (_, index) => ({
        yearLabel: index === 0 ? '2025-2026' : `Series ${index + 1}`,
        year: 2026 - (selectedYearsBack - 1 - index),
        startDate: '2025-06-01T00:00:00Z',
        endDate: '2026-05-31T23:59:59Z',
        monthlyVisitCounts: [0, 1, 0, 2, 0, 3, 0, 4, 0, 5, 0, 6],
        totalVisits: 21,
      })),
      totalVisits: 21 * selectedYearsBack,
      visitMetric: 'entity_view',
      visitEventTypes: ['detail_view', 'page_view'],
      timeZone: 'UTC',
    };
  }
});
