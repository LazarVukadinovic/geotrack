import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { AuthService } from '../../../../../core/services/auth.service';
import { TerritoryDashboardService } from '../../../../../core/services/territory-dashboard.service';
import { NationalAnalyticsService } from '../../../../../core/services/national/national-analytics.service';
import { TerritoryAnalyticsOverview } from '../../../../../core/models/national-analytics.model';
import { NationalAnalyticsComponent } from './national-analytics.component';

describe('NationalAnalyticsComponent', () => {
  let fixture: ComponentFixture<NationalAnalyticsComponent>;
  let component: NationalAnalyticsComponent;
  let analytics: {
    getOverview: ReturnType<typeof vi.fn>;
    buildMetricCards: ReturnType<typeof vi.fn>;
    exportCsv: ReturnType<typeof vi.fn>;
    downloadCsv: ReturnType<typeof vi.fn>;
    formatNumber: ReturnType<typeof vi.fn>;
  };
  let territories: {
    getAssignedTerritories: ReturnType<typeof vi.fn>;
    getActiveTerritoryId: ReturnType<typeof vi.fn>;
    setActiveTerritoryId: ReturnType<typeof vi.fn>;
    getRegions: ReturnType<typeof vi.fn>;
  };

  beforeEach(async () => {
    analytics = {
      getOverview: vi.fn(() => of(overviewResponse())),
      buildMetricCards: vi.fn(() => [
        { label: 'Total Page Views', value: '42', change: '+10%', up: true, icon: 'eye' },
        { label: 'Unique Visitors', value: '7', change: '0%', up: true, icon: 'users' },
      ]),
      exportCsv: vi.fn(() => of(new Blob(['csv'], { type: 'text/csv' }))),
      downloadCsv: vi.fn(),
      formatNumber: vi.fn((value: number) => String(value)),
    };
    territories = {
      getAssignedTerritories: vi.fn(() => of([{ id: 5, name: 'Assigned', displayName: 'Assigned' }])),
      getActiveTerritoryId: vi.fn(() => 5),
      setActiveTerritoryId: vi.fn(),
      getRegions: vi.fn(() => of({
        items: [{ regionId: 11, name: 'Region One', regionName: 'Region One', displayName: 'Region One' }],
        totalCount: 1,
        skip: 0,
        take: 100,
        hasMore: false,
      })),
    };

    await TestBed.configureTestingModule({
      imports: [NationalAnalyticsComponent],
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            fullName: 'Territory User',
            getUser: () => ({ email: 'territory@example.com', role: 'Territory Manager' }),
            canAccessTerritoryAdmin: () => true,
            isAdministrator: () => false,
            logout: vi.fn(),
          },
        },
        { provide: TerritoryDashboardService, useValue: territories },
        { provide: NationalAnalyticsService, useValue: analytics },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(NationalAnalyticsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  });

  afterEach(() => {
    document.querySelectorAll('[data-testid="analytics-chart-tooltip"]').forEach(element => element.remove());
  });

  it('loads backend analytics on init and renders metric values', () => {
    expect(component).toBeTruthy();
    expect(analytics.getOverview).toHaveBeenCalledWith(expect.objectContaining({
      territoryId: 5,
      period: '1m',
      status: 'active',
    }));
    expect(fixture.nativeElement.textContent).toContain('42');
    expect(fixture.nativeElement.textContent).toContain('Top Region');
  });

  it('reloads when filters change and sends region ids', () => {
    component.toggleRegion(11);

    expect(analytics.getOverview).toHaveBeenLastCalledWith(expect.objectContaining({
      regionIds: [11],
      take: 5,
    }));
  });

  it('exports with current filters', () => {
    component.selectedRegionIds = [11];
    component.exportReport();

    expect(analytics.exportCsv).toHaveBeenCalledWith(expect.objectContaining({
      territoryId: 5,
      regionIds: [11],
      period: '1m',
      status: 'active',
    }));
    expect(analytics.downloadCsv).toHaveBeenCalled();
  });

  it('supports show more by increasing the backend take value', () => {
    analytics.getOverview.mockReturnValue(of({
      ...overviewResponse(),
      regionalPerformance: {
        ...overviewResponse().regionalPerformance,
        hasMore: true,
        totalCount: 12,
      },
    }));
    component.showMoreTable();

    expect(analytics.getOverview).toHaveBeenLastCalledWith(expect.objectContaining({ take: 10 }));
  });

  it('renders a safe error state', () => {
    analytics.getOverview.mockReturnValue(throwError(() => new Error('failed')));
    component.onStatusChange();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Could not load territory analytics');
  });

  it('skips dense daily x-axis labels while preserving first and last dates', () => {
    const labels = dailyLabels(30);
    setPageViewsData(labels, Array.from({ length: 30 }, (_, index) => index + 1));

    const renderedLabels = pageViewXAxisLabels();

    expect(renderedLabels.length).toBeLessThan(labels.length);
    expect(renderedLabels.length).toBeGreaterThanOrEqual(5);
    expect(renderedLabels[0]).toBe(labels[0]);
    expect(renderedLabels[renderedLabels.length - 1]).toBe(labels[labels.length - 1]);
  });

  it('generates a non-overshooting page views line within the zero baseline', () => {
    setPageViewsData(
      ['Apr 23', 'Apr 24', 'Apr 25', 'Apr 26', 'Apr 27'],
      [0, 200, 0, 180, 0],
    );

    const path = pageViewLinePath();
    const yValues = pathYCoordinates(path);

    expect(path).not.toContain('C');
    expect(path).not.toContain('NaN');
    expect(yValues.length).toBeGreaterThan(0);
    expect(yValues.every(y => y >= 20 && y <= 140)).toBe(true);
  });

  it('keeps tooltips available for data points whose x-axis labels are hidden', () => {
    const labels = dailyLabels(30);
    const values = Array.from({ length: 30 }, (_, index) => index);
    values[1] = 7;
    setPageViewsData(labels, values);

    expect(pageViewXAxisLabels()).not.toContain(labels[1]);
    const target = pageViewHoverTargets().find(element =>
      element.getAttribute('aria-label')?.includes(labels[1]),
    );
    const targetData = component.chartHoverTargets.find(item => item.label === labels[1]);

    expect(target).toBeDefined();
    expect(targetData).toBeDefined();
    expect(component.pageViewsTooltip(targetData!).title).toBe(labels[1]);
    expect(component.pageViewsTooltip(targetData!).rows).toContainEqual({ label: 'Dalmatia', value: '7' });

    component.setActivePageViewsPoint(targetData!.index);
    (component as unknown as { cdr: { markForCheck: () => void } }).cdr.markForCheck();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-testid="territory-page-views-active-point"]')).not.toBeNull();
  });

  it('renders zero-only page view data as a flat baseline', () => {
    setPageViewsData(['Apr 23', 'Apr 24', 'Apr 25', 'Apr 26'], [0, 0, 0, 0]);

    const path = pageViewLinePath();
    const yValues = pathYCoordinates(path);

    expect(path).not.toContain('NaN');
    expect(yValues.length).toBe(4);
    expect(yValues.every(y => y === 140)).toBe(true);
  });

  it('renders sparse and empty page view data safely', () => {
    setPageViewsData(['May 1'], [9]);

    expect(pageViewLinePath()).toMatch(/^M350\.0,/);
    expect(pageViewHoverTargets().length).toBe(1);

    setOverview({
      ...overviewResponse(),
      pageViewsOverTime: { monthLabels: [], series: [] },
    });

    expect(fixture.nativeElement.textContent).toContain('No page view series for the selected filters.');
  });

  function setPageViewsData(labels: string[], values: number[]): void {
    setOverview({
      ...overviewResponse(),
      pageViewsOverTime: {
        monthLabels: labels,
        series: [{ regionId: 11, regionName: 'Dalmatia', values }],
      },
    });
  }

  function setOverview(overview: TerritoryAnalyticsOverview): void {
    analytics.getOverview.mockReturnValue(of(overview));
    component.onPeriodChange();
    fixture.detectChanges();
  }

  function dailyLabels(count: number): string[] {
    const formatter = new Intl.DateTimeFormat('en-US', {
      month: 'short',
      day: 'numeric',
      timeZone: 'UTC',
    });
    const start = Date.UTC(2026, 3, 23);
    return Array.from({ length: count }, (_, index) =>
      formatter.format(new Date(start + index * 86_400_000)),
    );
  }

  function pageViewXAxisLabels(): string[] {
    return Array.from(fixture.nativeElement.querySelectorAll('[data-testid="territory-page-views-x-label"]') as NodeListOf<Element>)
      .map(element => (element.textContent ?? '').trim());
  }

  function pageViewLinePath(): string {
    return fixture.nativeElement
      .querySelector('[data-testid="territory-page-views-line"]')
      ?.getAttribute('d') ?? '';
  }

  function pathYCoordinates(path: string): number[] {
    return Array.from(path.matchAll(/[ML](-?\d+(?:\.\d+)?),(-?\d+(?:\.\d+)?)/g))
      .map(match => Number(match[2]));
  }

  function pageViewHoverTargets(): Element[] {
    return Array.from(fixture.nativeElement.querySelectorAll('[data-testid="territory-page-views-hover-target"]') as NodeListOf<Element>);
  }

  function overviewResponse(): TerritoryAnalyticsOverview {
    return {
      activeTerritory: { id: 5, name: 'Assigned', displayName: 'Assigned' },
      filters: {
        period: '6m',
        startDate: '2026-01-01T00:00:00Z',
        endDate: '2026-05-01T00:00:00Z',
        selectedRegionIds: [11],
        status: 'active',
      },
      totals: {
        totalPageViews: 42,
        uniqueVisitors: 7,
        externalLinkClicks: 2,
        totalSaves: 3,
        totalReviews: 4,
        averageRating: 4.5,
      },
      comparison: {
        pageViewsChangePercent: 10,
        uniqueVisitorsChangePercent: 0,
        externalClicksChangePercent: 0,
        savesChangePercent: 0,
        reviewsChangePercent: 0,
        averageRatingChange: 0,
      },
      topRegion: {
        regionId: 11,
        regionName: 'Region One',
        score: 90,
        healthScore: 90,
        statusLabel: 'Excellent',
        periodLabel: 'Last 30 days',
        description: 'Highest engagement from real events.',
        visits: 42,
        uniqueVisitors: 7,
      },
      fastestGrowing: null,
      topEntities: [
        {
          entityType: 'place',
          entityId: 8,
          name: 'Real Place',
          regionName: 'Region One',
          rank: 1,
          metricValue: 42,
          metricLabel: 'views',
        },
      ],
      pageViewsOverTime: {
        monthLabels: ['May'],
        series: [{ regionId: 11, regionName: 'Region One', values: [42] }],
      },
      topDestinations: [{ id: 1, name: 'Real Destination', regionName: 'Region One', views: 12 }],
      topLocations: [],
      topPlaces: [{ id: 8, name: 'Real Place', regionName: 'Region One', views: 42 }],
      topEvents: [],
      contentQualityByRegion: [{ regionId: 11, regionName: 'Region One', totalReviews: 4, averageRating: 4.5 }],
      regionalPerformance: {
        items: [
          {
            regionId: 11,
            regionName: 'Region One',
            pageViews: 42,
            visitors: 7,
            saves: 3,
            averageRating: 4.5,
            destinationsCount: 1,
            locationsCount: 0,
            placesCount: 1,
            eventsCount: 0,
            statusLabel: 'Active',
            healthScore: 90,
            health: {
              score: 90,
              statusLabel: 'Excellent',
              drivers: [],
              issues: [],
              recommendations: [],
            },
            qualityScore: 90,
            managerStatus: 'Assigned',
            managerNames: ['Manager One'],
          },
        ],
        totalCount: 1,
        skip: 0,
        take: 5,
        hasMore: false,
      },
    };
  }
});
