import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, Subject } from 'rxjs';
import { vi } from 'vitest';
import { AnalyticsPageComponent } from './analytics-page.component';
import { AuthService } from '../../../../../core/services/auth.service';
import { DashboardService } from '../../../../../core/services/dashboard.service';
import { RegionalAnalyticsService } from '../../../../../core/services/regional-analytics';
import {
  CategoryFilter,
  EntityRow,
  MetricCard,
  RegionalAnalyticsEntityDetail,
  RegionalAnalyticsEntityMetric,
  RegionalAnalyticsOverview,
  RegionalAnalyticsTrendPoint,
  RegionalMonthlyVisits,
  RegionalMonthlyVisitYearsBack,
  RegionalTagViewsAnalyticsResponse,
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
      getTagViews: vi.fn(() => of(tagViewsResponse())),
      getEntityDetail: vi.fn((params: { entityType: string; entityId: number }) =>
        of(entityDetailResponse(params.entityId === 18 ? 'stay' : params.entityId === 19 ? 'stay-empty' : params.entityType))),
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
      mapTrend: vi.fn((points: RegionalAnalyticsTrendPoint[]) => points),
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
            isTerritoryManager: () => false,
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
    document.querySelectorAll('[data-testid="analytics-chart-tooltip"]').forEach(element => element.remove());
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
    expect(analyticsService.getTagViews).toHaveBeenCalledWith(expect.objectContaining({
      regionId: 21,
      period: '1m',
      entityType: 'all',
      tagIds: [],
    }));
  });

  it('shows page views trend tooltip on hover and focus, then hides it', () => {
    fixture.detectChanges();

    const point = fixture.nativeElement.querySelector('[data-testid="page-views-trend-point"]') as Element;

    expect(showTooltip(point)).toContain('May 15, 2026');
    expect(tooltipText()).toContain('Page Views');
    expect(tooltipText()).toContain('12');
    expect(tooltipText()).toContain('Unique Visitors');
    expect(tooltipText()).toContain('3');

    point.dispatchEvent(new MouseEvent('mouseleave'));
    fixture.detectChanges();
    expect(document.body.querySelector('[data-testid="analytics-chart-tooltip"]')).toBeNull();

    point.dispatchEvent(new Event('focus'));
    fixture.detectChanges();
    expect(tooltipText()).toContain('May 15, 2026');

    point.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    expect(document.body.querySelector('[data-testid="analytics-chart-tooltip"]')).toBeNull();
  });

  it('does not render always-visible page views trend point circles by default', () => {
    const overview = overviewResponse();
    overview.pageViewsTrend = multiTrendPoints();
    vi.mocked(analyticsService.getOverview).mockReturnValue(of(overview));

    fixture.detectChanges();

    const chart = pageViewsTrendChart();
    expect(chart.querySelectorAll('[data-testid="page-views-trend-point"]').length).toBe(3);
    expect(chart.querySelectorAll('[data-testid="page-views-trend-point"] circle').length).toBe(0);
    expect(activeTrendPoint()).toBeNull();
  });

  it('activates one page views trend point on hover, moves it to the hovered value, and hides it on leave', () => {
    const overview = overviewResponse();
    overview.pageViewsTrend = multiTrendPoints();
    vi.mocked(analyticsService.getOverview).mockReturnValue(of(overview));

    fixture.detectChanges();

    const points = pageViewsTrendPoints();
    expect(points.length).toBe(3);

    expect(showTooltip(points[1])).toContain('May 14, 2026');
    expect(tooltipText()).toContain('Page Views');
    expect(tooltipText()).toContain('18');
    let active = activeTrendPoint();
    expect(active).not.toBeNull();
    expect(active?.getAttribute('cx')).toBe('170');

    points[1].dispatchEvent(new MouseEvent('mouseleave'));
    fixture.detectChanges();
    points[2].dispatchEvent(new MouseEvent('mouseenter', {
      clientX: 280,
      clientY: 130,
      bubbles: true,
    }));
    fixture.detectChanges();

    active = activeTrendPoint();
    expect(pageViewsTrendChart().querySelectorAll('[data-testid="page-views-trend-active-point"]').length).toBe(1);
    expect(active?.getAttribute('cx')).toBe('316');

    points[2].dispatchEvent(new MouseEvent('mouseleave'));
    fixture.detectChanges();
    expect(activeTrendPoint()).toBeNull();
    expect(document.body.querySelector('[data-testid="analytics-chart-tooltip"]')).toBeNull();
  });

  it('renders page views trend safely when trend data is empty', () => {
    const overview = overviewResponse();
    overview.pageViewsTrend = [];
    vi.mocked(analyticsService.getOverview).mockReturnValue(of(overview));

    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Page Views Trend');
    expect(pageViewsTrendPoints().length).toBe(0);
    expect(activeTrendPoint()).toBeNull();
  });

  it('shows top performing entity tooltip with entity name and real views', () => {
    fixture.detectChanges();

    const row = fixture.nativeElement.querySelector('[data-testid="top-performing-row"]') as Element;

    expect(showTooltip(row)).toContain('Real Location');
    expect(tooltipText()).toContain('Views');
    expect(tooltipText()).toContain('12');
    expect(tooltipText()).toContain('Visitors');
    expect(tooltipText()).toContain('3');
  });

  it('shows entity type distribution tooltip with count and percentage', () => {
    fixture.detectChanges();

    const segment = fixture.nativeElement.querySelector('[data-testid="entity-distribution-segment"]') as Element;

    expect(showTooltip(segment)).toContain('Places');
    expect(tooltipText()).toContain('1 entities');
    expect(tooltipText()).toContain('100%');
  });

  it('shows engagement funnel tooltip with metric value and page-view share', () => {
    fixture.detectChanges();

    const rows = fixture.nativeElement.querySelectorAll('[data-testid="engagement-funnel-row"]') as NodeListOf<Element>;

    expect(showTooltip(rows[1])).toContain('Unique Visitors');
    expect(tooltipText()).toContain('Value');
    expect(tooltipText()).toContain('3');
    expect(tooltipText()).toContain('25%');
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
    expect(analyticsService.getTagViews).toHaveBeenCalledWith(expect.objectContaining({
      entityType: 'location',
      period: '1m',
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

  it('shows monthly visits tooltip with month, year, and visits', () => {
    fixture.detectChanges();

    const bars = Array.from(fixture.nativeElement.querySelectorAll('[data-testid="monthly-visits-bar"]')) as Element[];
    const mayBar = bars.find(bar => bar.getAttribute('aria-label')?.includes('May 2026'));

    expect(mayBar).toBeDefined();
    expect(showTooltip(mayBar as Element)).toContain('May 2026');
    expect(tooltipText()).toContain('Visits');
    expect(tooltipText()).toContain('6');
  });

  it('renders tag views card directly below monthly visits', () => {
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    const monthlyIndex = text.indexOf('Monthly Visits Over Time');
    const tagViewsIndex = text.indexOf('Tag Views');
    const detailsIndex = text.indexOf('Entity Performance Details');

    expect(monthlyIndex).toBeGreaterThanOrEqual(0);
    expect(tagViewsIndex).toBeGreaterThan(monthlyIndex);
    expect(detailsIndex).toBeGreaterThan(tagViewsIndex);
    expect(text).toContain('Coastal Walks');
    expect(text).toContain('Museums');
  });

  it('defaults tag views to one month and reloads when the local period changes', () => {
    fixture.detectChanges();
    vi.mocked(analyticsService.getTagViews).mockClear();

    expect(component.selectedTagViewsPeriod).toBe('1m');

    component.onTagViewsPeriodChange('24h');

    expect(component.selectedTagViewsPeriod).toBe('24h');
    expect(analyticsService.getTagViews).toHaveBeenCalledWith(expect.objectContaining({
      regionId: 21,
      period: '24h',
      tagIds: [],
    }));
  });

  it('shows backend tag options and reloads when a tag is selected', () => {
    fixture.detectChanges();
    vi.mocked(analyticsService.getTagViews).mockClear();

    const button = fixture.nativeElement.querySelector('[aria-haspopup="listbox"]') as HTMLButtonElement;
    button.click();
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Coastal Walks');
    expect(text).toContain('Museums');

    const checkbox = fixture.nativeElement.querySelector('[data-testid="tag-selector-menu"] input[type="checkbox"]') as HTMLInputElement;
    checkbox.checked = true;
    checkbox.dispatchEvent(new Event('change'));

    expect(analyticsService.getTagViews).toHaveBeenCalledWith(expect.objectContaining({
      tagIds: [7],
      period: '1m',
    }));
  });

  it('renders tag views empty state safely', () => {
    const emptyTagViews = tagViewsResponse();
    emptyTagViews.totalViews = 0;
    emptyTagViews.items = [];
    vi.mocked(analyticsService.getTagViews).mockReturnValue(of(emptyTagViews));

    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('No tag view data for this period.');
    expect(text).not.toContain('Fake Tag');
    expect(fixture.nativeElement.querySelectorAll('[data-testid="tag-views-bar"]').length).toBe(0);
  });

  it('shows tag views tooltip with tag name, views, and total share', () => {
    fixture.detectChanges();

    const bar = fixture.nativeElement.querySelector('[data-testid="tag-views-bar"]') as Element;

    expect(showTooltip(bar)).toContain('Coastal Walks');
    expect(tooltipText()).toContain('Views');
    expect(tooltipText()).toContain('8');
    expect(tooltipText()).toContain('66.7%');
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

  it('renders destination ranking data above actionable insights', () => {
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    const rankingIndex = text.indexOf('Destination Ranking');
    const insightsIndex = text.indexOf('Actionable Insights');

    expect(rankingIndex).toBeGreaterThanOrEqual(0);
    expect(insightsIndex).toBeGreaterThan(rankingIndex);
    expect(text).toContain('Coastal Capital');
    expect(text).toContain('Harbor Quarter');
    expect(text).not.toContain('Banje Beach');
  });

  it('shows destination ranking tooltip with rank, destination, visits, and top share', () => {
    fixture.detectChanges();

    const row = fixture.nativeElement.querySelector('[data-testid="destination-ranking-row"]') as Element;

    expect(showTooltip(row)).toContain('Coastal Capital');
    expect(tooltipText()).toContain('Rank');
    expect(tooltipText()).toContain('#1');
    expect(tooltipText()).toContain('Visits');
    expect(tooltipText()).toContain('20');
    expect(tooltipText()).toContain('100%');
  });

  it('renders destination ranking empty state when no destination visits exist', () => {
    const emptyOverview = overviewResponse();
    emptyOverview.destinationRanking = [];
    vi.mocked(analyticsService.getOverview).mockReturnValue(of(emptyOverview));

    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('No destination visit data yet.');
  });

  it('scales destination ranking bars against the top visit count', () => {
    fixture.detectChanges();

    const bars = fixture.nativeElement.querySelectorAll('[data-testid="destination-ranking-bar-fill"]') as NodeListOf<HTMLElement>;

    expect(bars.length).toBe(2);
    expect(bars[0].style.width).toBe('100%');
    expect(bars[1].style.width).toBe('50%');
  });

  it('does not render monthly tooltip targets for empty monthly chart data', () => {
    const emptyMonthlyVisits = monthlyVisitsResponse(1);
    emptyMonthlyVisits.totalVisits = 0;
    emptyMonthlyVisits.series = emptyMonthlyVisits.series.map(series => ({
      ...series,
      monthlyVisitCounts: series.monthlyVisitCounts.map(() => 0),
      totalVisits: 0,
    }));
    vi.mocked(analyticsService.getMonthlyVisits).mockReturnValue(of(emptyMonthlyVisits));

    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('No visit data yet for this selection.');
    expect(fixture.nativeElement.querySelectorAll('[data-testid="monthly-visits-bar"]').length).toBe(0);
  });

  it('reloads overview analytics when the period changes', () => {
    fixture.detectChanges();
    vi.mocked(analyticsService.getOverview).mockClear();

    component.onPeriodChange('Last 7 Days');

    expect(analyticsService.getOverview).toHaveBeenCalledWith(expect.objectContaining({
      regionId: 21,
      period: '7d',
    }));
  });

  it('opens entity detail overlay from a table row and calls the detail endpoint', () => {
    fixture.detectChanges();

    const row = fixture.nativeElement.querySelector('[data-testid="entity-performance-row"]') as HTMLTableRowElement;
    row.click();
    fixture.detectChanges();

    expect(analyticsService.getEntityDetail).toHaveBeenCalledWith({
      regionId: 21,
      entityType: 'place',
      entityId: 8,
      period: '30d',
    });
    expect(fixture.nativeElement.textContent).toContain('Real backend place detail');
    expect(fixture.nativeElement.textContent).toContain('Website');
  });

  it('shows loading state while entity detail is loading', () => {
    const pending = new Subject<RegionalAnalyticsEntityDetail>();
    vi.mocked(analyticsService.getEntityDetail).mockReturnValueOnce(pending.asObservable());
    fixture.detectChanges();

    const row = fixture.nativeElement.querySelector('[data-testid="entity-performance-row"]') as HTMLTableRowElement;
    row.click();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Loading entity details...');

    pending.next(entityDetailResponse('place'));
    pending.complete();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Real backend place detail');
  });

  it('renders destination, location, place, stay, and event details safely', () => {
    fixture.detectChanges();

    const cases = [
      { row: entityRow('destination'), expected: ['Destination Fields', 'Locations', 'Boundary note'] },
      { row: entityRow('location'), expected: ['Location Fields', 'Parent destination', 'Places'] },
      { row: entityRow('place'), expected: ['Place Fields', 'Website', 'Opening hours'] },
      { row: entityRow('place', 18, 'Real Stay'), expected: ['Stay type', 'Hotel', 'WiFi'] },
      { row: entityRow('event'), expected: ['Event Fields', 'Starts', 'Venue'] },
    ];

    for (const item of cases) {
      component.openEntityDetails(item.row);
      fixture.detectChanges();
      const text = fixture.nativeElement.textContent as string;
      for (const expected of item.expected) {
        expect(text).toContain(expected);
      }
      component.closeEntityDetails();
      fixture.detectChanges();
    }
  });

  it('renders stay external partner click distribution with labels and shares', () => {
    fixture.detectChanges();

    component.openEntityDetails(entityRow('place', 18, 'Real Stay'));
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(fixture.nativeElement.querySelector('[data-testid="external-partner-click-card"]')).not.toBeNull();
    expect(text).toContain('External Partner Click Distribution');
    expect(text).toContain('Booking.com');
    expect(text).toContain('Airbnb');
    expect(text).toContain('Expedia');
    expect(text).toContain('33.3%');
    expect(text).toContain('16.7%');
    expect(text).toContain('50%');
  });

  it('renders stay external partner click empty state when no partner clicks exist', () => {
    fixture.detectChanges();

    component.openEntityDetails(entityRow('place', 19, 'Quiet Stay'));
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(fixture.nativeElement.querySelector('[data-testid="external-partner-click-card"]')).not.toBeNull();
    expect(text).toContain('No external partner click data available yet.');
    expect(text).toContain('Booking.com');
    expect(text).toContain('Airbnb');
  });

  it('does not render external partner click chart for non-stay places', () => {
    fixture.detectChanges();

    component.openEntityDetails(entityRow('place', 8, 'Real Place'));
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(fixture.nativeElement.querySelector('[data-testid="external-partner-click-card"]')).toBeNull();
    expect(text).not.toContain('External Partner Click Distribution');
  });

  it('handles missing detail fields without rendering broken values', () => {
    vi.mocked(analyticsService.getEntityDetail).mockReturnValueOnce(of(missingFieldsDetail()));
    fixture.detectChanges();

    component.openEntityDetails(entityRow('place', 44, 'Sparse Place'));
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('No additional description is available.');
    expect(text).not.toContain('undefined');
    expect(text).not.toContain('null');
    expect(text).not.toContain('Fake');
  });

  it('closes entity detail overlay with the close button and Escape key', () => {
    fixture.detectChanges();

    component.openEntityDetails(entityRow('place'));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="entity-detail-overlay"]')).not.toBeNull();

    const closeButton = fixture.nativeElement.querySelector('[data-testid="entity-detail-close"]') as HTMLButtonElement;
    closeButton.click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="entity-detail-overlay"]')).toBeNull();

    component.openEntityDetails(entityRow('event'));
    fixture.detectChanges();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="entity-detail-overlay"]')).toBeNull();
  });

  function showTooltip(element: Element): string {
    element.dispatchEvent(new MouseEvent('mouseenter', {
      clientX: 160,
      clientY: 160,
      bubbles: true,
    }));
    fixture.detectChanges();
    return tooltipText();
  }

  function tooltipText(): string {
    return (document.body.querySelector('[data-testid="analytics-chart-tooltip"]')?.textContent ?? '')
      .replace(/\s+/g, ' ')
      .trim();
  }

  function pageViewsTrendChart(): SVGElement {
    return fixture.nativeElement.querySelector('[data-testid="page-views-trend-chart"]') as SVGElement;
  }

  function pageViewsTrendPoints(): Element[] {
    return Array.from(fixture.nativeElement.querySelectorAll('[data-testid="page-views-trend-point"]')) as Element[];
  }

  function activeTrendPoint(): Element | null {
    return fixture.nativeElement.querySelector('[data-testid="page-views-trend-active-point"]') as Element | null;
  }

  function entityRow(
    entityType: RegionalAnalyticsEntityDetail['entityType'] = 'place',
    entityId = entityType === 'destination' ? 2 : entityType === 'location' ? 4 : entityType === 'event' ? 12 : 8,
    name = `Real ${entityType}`,
  ): EntityRow {
    const typeMap: Record<RegionalAnalyticsEntityDetail['entityType'], EntityRow['type']> = {
      destination: 'DESTINATION',
      location: 'LOCATION',
      place: 'PLACE',
      event: 'EVENT',
    };

    return {
      entityType,
      entityId,
      name,
      type: typeMap[entityType],
      location: 'Assigned Region',
      views: 12,
      visitors: 3,
      saves: 2,
      actions: 1,
      reviewAvg: 4.4,
      reviewCount: 1,
      trend: 'up',
      imageBg: '#fff',
      imageUrl: null,
      status: 'Published',
      detailRoute: null,
    };
  }

  function entityDetailResponse(entityType: string): RegionalAnalyticsEntityDetail {
    const base: RegionalAnalyticsEntityDetail = {
      entityType: entityType === 'destination' || entityType === 'location' || entityType === 'event' ? entityType : 'place',
      entityId: entityType === 'destination' ? 2 : entityType === 'location' ? 4 : entityType === 'event' ? 12 : 8,
      name: `Real ${entityType}`,
      title: `Real ${entityType}`,
      subtitle: entityType === 'place' ? 'A useful place' : null,
      status: 'Published',
      regionId: 21,
      regionDisplayName: 'Assigned Region',
      destinationId: 2,
      destinationName: 'Real Destination',
      locationId: entityType === 'destination' ? null : 4,
      locationName: entityType === 'destination' ? null : 'Real Location',
      placeId: entityType === 'place' ? 8 : null,
      placeName: entityType === 'place' ? 'Real place' : null,
      description: `Real backend ${entityType} detail`,
      summary: `Real backend ${entityType} detail`,
      imageUrl: null,
      galleryImages: [],
      tags: [{ id: 7, name: 'Coastal Walks', slug: 'coastal-walks' }],
      address: entityType === 'place' ? 'Harbor 1' : null,
      latitude: 42.1,
      longitude: 18.1,
      createdAt: '2026-05-01T00:00:00Z',
      updatedAt: '2026-05-02T00:00:00Z',
      publishedAt: '2026-05-03T00:00:00Z',
      analytics: {
        views: 12,
        uniqueVisitors: 3,
        saves: 2,
        externalClicks: 1,
        reviews: 1,
        averageRating: 4.4,
        ratingCount: 1,
        trendChangePercent: 10,
        lastViewedAt: '2026-05-15T00:00:00Z',
      },
      typeSpecific: {
        operatingHours: [],
        amenities: [],
        bookingPartners: [],
        externalPartnerClicks: [],
      },
    };

    if (entityType === 'destination') {
      return {
        ...base,
        entityType: 'destination',
        typeSpecific: {
          ...base.typeSpecific,
          destinationType: 'Coastal',
          locationsCount: 2,
          placesCount: 5,
          eventsCount: 1,
          boundaryNote: 'Boundary note',
        },
      };
    }

    if (entityType === 'location') {
      return {
        ...base,
        entityType: 'location',
        typeSpecific: {
          ...base.typeSpecific,
          locationType: 'Harbor',
          parentDestination: 'Real Destination',
          placesCount: 3,
          eventsCount: 2,
        },
      };
    }

    if (entityType === 'event') {
      return {
        ...base,
        entityType: 'event',
        typeSpecific: {
          ...base.typeSpecific,
          eventType: 'Festival',
          parentDestination: 'Real Destination',
          parentLocation: 'Real Location',
          startsAt: '2026-06-01T18:00:00Z',
          endsAt: '2026-06-01T20:00:00Z',
          allDay: false,
          venueName: 'Main Square',
          ticketPrice: '$12',
        },
      };
    }

    const isStay = entityType === 'stay' || entityType === 'stay-empty';
    const hasStayClickData = entityType === 'stay';
    return {
      ...base,
      entityType: 'place',
      entityId: isStay ? (hasStayClickData ? 18 : 19) : 8,
      name: isStay ? (hasStayClickData ? 'Real Stay' : 'Quiet Stay') : 'Real place',
      title: isStay ? (hasStayClickData ? 'Real Stay' : 'Quiet Stay') : 'Real place',
      placeId: isStay ? (hasStayClickData ? 18 : 19) : 8,
      placeName: isStay ? (hasStayClickData ? 'Real Stay' : 'Quiet Stay') : 'Real place',
      typeSpecific: {
        ...base.typeSpecific,
        placeType: isStay ? 'Stay' : 'Cafe',
        stayType: isStay ? 'Hotel' : null,
        parentLocation: 'Real Location',
        parentDestination: 'Real Destination',
        website: 'https://example.com',
        contactPhone: '+111',
        openingHours: '09:00 - 17:00',
        amenities: isStay ? ['WiFi'] : [],
        bookingPartners: isStay ? [
          { label: 'Booking.com', url: 'https://www.booking.com/hotel/hr/real-stay.html' },
          { label: 'Airbnb', url: 'https://www.airbnb.com/rooms/real-stay' },
          ...(hasStayClickData ? [{ label: 'Expedia', url: 'https://www.expedia.com/real-stay' }] : []),
        ] : [],
        externalPartnerClicks: isStay ? [
          {
            partnerKey: 'booking',
            partnerLabel: 'Booking.com',
            url: 'https://www.booking.com/hotel/hr/real-stay.html',
            clickCount: hasStayClickData ? 2 : 0,
            sharePercent: hasStayClickData ? 33.3 : 0,
          },
          {
            partnerKey: 'airbnb',
            partnerLabel: 'Airbnb',
            url: 'https://www.airbnb.com/rooms/real-stay',
            clickCount: hasStayClickData ? 1 : 0,
            sharePercent: hasStayClickData ? 16.7 : 0,
          },
          ...(hasStayClickData ? [{
            partnerKey: 'external',
            partnerLabel: 'Expedia',
            url: 'https://www.expedia.com/real-stay',
            clickCount: 3,
            sharePercent: 50,
          }] : []),
        ] : [],
      },
    };
  }

  function missingFieldsDetail(): RegionalAnalyticsEntityDetail {
    return {
      ...entityDetailResponse('place'),
      entityId: 44,
      name: 'Sparse Place',
      title: 'Sparse Place',
      description: null,
      summary: null,
      tags: [],
      address: null,
      latitude: null,
      longitude: null,
      typeSpecific: {
        operatingHours: [],
        amenities: [],
        bookingPartners: [],
        externalPartnerClicks: [],
      },
    };
  }

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
      destinationRanking: [
        {
          destinationId: 2,
          destinationName: 'Coastal Capital',
          regionId: 21,
          regionDisplayName: 'Assigned Region',
          visits: 20,
          rank: 1,
          percentageOfTopValue: 100,
          imageUrl: null,
          detailRoute: '/destinations/detail/2',
        },
        {
          destinationId: 3,
          destinationName: 'Harbor Quarter',
          regionId: 21,
          regionDisplayName: 'Assigned Region',
          visits: 10,
          rank: 2,
          percentageOfTopValue: 50,
          imageUrl: null,
          detailRoute: '/destinations/detail/3',
        },
      ],
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

  function multiTrendPoints(): RegionalAnalyticsTrendPoint[] {
    return [
      { date: '2026-05-13T00:00:00Z', views: 12, uniqueVisitors: 3 },
      { date: '2026-05-14T00:00:00Z', views: 18, uniqueVisitors: 5 },
      { date: '2026-05-15T00:00:00Z', views: 9, uniqueVisitors: 2 },
    ];
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

  function tagViewsResponse(): RegionalTagViewsAnalyticsResponse {
    return {
      period: '1m',
      regionId: 21,
      regionDisplayName: 'Assigned Region',
      totalViews: 12,
      availableTags: [
        { tagId: 7, name: 'Coastal Walks', entityScopes: ['place'] },
        { tagId: 9, name: 'Museums', entityScopes: ['destination', 'place'] },
      ],
      items: [
        { tagId: 7, tagName: 'Coastal Walks', views: 8, percentageOfTotal: 66.7, entityScopes: ['place'] },
        { tagId: 9, tagName: 'Museums', views: 4, percentageOfTotal: 33.3, entityScopes: ['destination', 'place'] },
      ],
    };
  }
});
