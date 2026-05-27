import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { NationalAnalyticsService } from './national-analytics.service';
import { TerritoryAnalyticsOverview } from '../../models/national-analytics.model';

describe('NationalAnalyticsService', () => {
  let service: NationalAnalyticsService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(NationalAnalyticsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('loads territory analytics overview with active filters', () => {
    service.getOverview({
      territoryId: 5,
      period: '6m',
      regionIds: [11, 12],
      status: 'active',
      skip: 0,
      take: 10,
      sort: 'views_desc',
    }).subscribe(response => {
      expect(response.totals.totalPageViews).toBe(42);
    });

    const request = http.expectOne(req =>
      req.url === '/api/admin/territory/analytics/overview' &&
      req.params.get('territoryId') === '5' &&
      req.params.get('period') === '6m' &&
      req.params.get('status') === 'active' &&
      req.params.getAll('regionIds')?.join(',') === '11,12' &&
      req.params.get('take') === '10' &&
      req.params.get('sort') === 'views_desc',
    );
    expect(request.request.method).toBe('GET');
    request.flush(overviewResponse());
  });

  it('exports filtered analytics as CSV', () => {
    service.exportCsv({ territoryId: 5, period: '7d', status: 'all', regionIds: [11] }).subscribe(blob => {
      expect(blob.type).toBe('text/csv');
    });

    const request = http.expectOne(req =>
      req.url === '/api/admin/territory/analytics/export' &&
      req.params.get('territoryId') === '5' &&
      req.params.get('period') === '7d' &&
      req.params.get('status') === 'all' &&
      req.params.getAll('regionIds')?.join(',') === '11' &&
      !req.params.has('take'),
    );
    expect(request.request.method).toBe('GET');
    request.flush(new Blob(['Territory Analytics Export\n'], { type: 'text/csv' }), {
      headers: { 'Content-Type': 'text/csv' },
    });
  });

  it('builds metric cards from backend totals and comparison values', () => {
    const cards = service.buildMetricCards(overviewResponse());

    expect(cards[0].value).toBe('42');
    expect(cards[0].change).toBe('+10%');
    expect(cards[5].value).toBe('4.4/5');
  });

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
        externalLinkClicks: 3,
        totalSaves: 4,
        totalReviews: 5,
        averageRating: 4.4,
      },
      comparison: {
        pageViewsChangePercent: 10,
        uniqueVisitorsChangePercent: 0,
        externalClicksChangePercent: 0,
        savesChangePercent: 0,
        reviewsChangePercent: 0,
        averageRatingChange: 0.2,
      },
      topRegion: null,
      fastestGrowing: null,
      topEntities: [],
      pageViewsOverTime: { monthLabels: ['Jan'], series: [] },
      topDestinations: [],
      topLocations: [],
      topPlaces: [],
      topEvents: [],
      contentQualityByRegion: [],
      regionalPerformance: {
        items: [],
        totalCount: 0,
        skip: 0,
        take: 5,
        hasMore: false,
      },
    };
  }
});
