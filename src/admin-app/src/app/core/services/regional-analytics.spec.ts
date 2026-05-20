import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { RegionalAnalyticsService } from './regional-analytics';
import {
  RegionalAnalyticsOverview,
  RegionalMonthlyVisits,
  RegionalTagViewsAnalyticsResponse,
} from '../models/regional-analytics.model';

describe('RegionalAnalyticsService', () => {
  let service: RegionalAnalyticsService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(RegionalAnalyticsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('loads overview data from the regional analytics API with filters', () => {
    service
      .getOverview({
        regionId: 12,
        period: '30d',
        entityType: 'place',
        search: 'museum',
        skip: 0,
        take: 25,
        sort: 'views_desc',
      })
      .subscribe((overview) => {
        expect(overview.activeRegion.displayName).toBe('Assigned Region');
        expect(overview.totals.pageViews).toBe(42);
      });

    const request = http.expectOne((req) =>
      req.url === '/api/admin/regional/analytics/overview' &&
      req.params.get('regionId') === '12' &&
      req.params.get('period') === '30d' &&
      req.params.get('entityType') === 'place' &&
      req.params.get('search') === 'museum' &&
      req.params.get('take') === '25' &&
      req.params.get('sort') === 'views_desc',
    );

    expect(request.request.method).toBe('GET');
    request.flush(overviewResponse());
  });

  it('maps backend overview rows into metric cards and table rows', () => {
    const overview = overviewResponse();
    const metrics = service.buildMetricCards(overview);
    const rows = service.mapEntityRows(overview.entityRows);
    const insights = service.buildActionableInsights(overview.actionableInsights);

    expect(metrics[0].value).toBe('42');
    expect(rows[0].entityId).toBe(8);
    expect(rows[0].imageBg).toContain('/images/place.webp');
    expect(insights[0].title).toBe('Real Location');
  });

  it('exports filtered analytics as CSV', () => {
    service.exportCsv({ regionId: 12, period: '7d', entityType: 'all', sort: 'views_desc' }).subscribe((blob) => {
      expect(blob.type).toBe('text/csv');
    });

    const request = http.expectOne((req) =>
      req.url === '/api/admin/regional/analytics/export' &&
      req.params.get('regionId') === '12' &&
      req.params.get('period') === '7d' &&
      req.params.get('entityType') === 'all' &&
      !req.params.has('take'),
    );

    expect(request.request.method).toBe('GET');
    request.flush(new Blob(['Region,Name\nAssigned Region,Real Place\n'], { type: 'text/csv' }), {
      headers: { 'Content-Type': 'text/csv' },
    });
  });

  it('loads monthly visits with independent year range and shared entity filters', () => {
    service
      .getMonthlyVisits({
        regionId: 12,
        selectedYearsBack: 2,
        entityType: 'event',
        search: 'festival',
      })
      .subscribe((monthlyVisits) => {
        expect(monthlyVisits.selectedYearsBack).toBe(2);
        expect(monthlyVisits.series.length).toBe(2);
      });

    const request = http.expectOne((req) =>
      req.url === '/api/admin/regional/analytics/monthly-visits' &&
      req.params.get('regionId') === '12' &&
      req.params.get('selectedYearsBack') === '2' &&
      req.params.get('entityType') === 'event' &&
      req.params.get('search') === 'festival' &&
      !req.params.has('period'),
    );

    expect(request.request.method).toBe('GET');
    request.flush(monthlyVisitsResponse());
  });

  it('loads tag views with local period and repeated selected tag ids', () => {
    service
      .getTagViews({
        regionId: 12,
        period: '1m',
        tagIds: [7, 9],
        take: 15,
        entityType: 'place',
      })
      .subscribe((tagViews) => {
        expect(tagViews.availableTags.length).toBe(2);
        expect(tagViews.items[0].tagName).toBe('Coastal Walks');
      });

    const request = http.expectOne((req) =>
      req.url === '/api/admin/regional/analytics/tag-views' &&
      req.params.get('regionId') === '12' &&
      req.params.get('period') === '1m' &&
      req.params.get('take') === '15' &&
      req.params.get('entityType') === 'place' &&
      req.params.getAll('tagIds')?.join(',') === '7,9',
    );

    expect(request.request.method).toBe('GET');
    request.flush(tagViewsResponse());
  });

  it('loads entity detail with entity type, id, region, and period', () => {
    service
      .getEntityDetail({
        regionId: 12,
        entityType: 'place',
        entityId: 8,
        period: '30d',
      })
      .subscribe((detail) => {
        expect(detail.entityId).toBe(8);
        expect(detail.analytics.views).toBe(42);
      });

    const request = http.expectOne((req) =>
      req.url === '/api/admin/regional/analytics/entities/place/8/details' &&
      req.params.get('regionId') === '12' &&
      req.params.get('period') === '30d',
    );

    expect(request.request.method).toBe('GET');
    request.flush(entityDetailResponse());
  });

  function overviewResponse(): RegionalAnalyticsOverview {
    return {
      activeRegion: {
        id: 12,
        name: 'Assigned',
        displayName: 'Assigned Region',
        territoryName: 'Testland',
      },
      period: {
        key: '30d',
        startDate: '2026-04-15T00:00:00Z',
        endDate: '2026-05-15T00:00:00Z',
      },
      totals: {
        pageViews: 42,
        uniqueVisitors: 7,
        saves: 3,
        averageRating: 4.5,
        externalClicks: 2,
        totalReviews: 4,
      },
      comparison: {
        pageViewsChangePercent: 10,
        uniqueVisitorsChangePercent: 0,
        savesChangePercent: 0,
        averageRatingChangePercent: 0,
        externalClicksChangePercent: 0,
        totalReviewsChangePercent: 0,
      },
      pageViewsTrend: [{ date: '2026-05-15T00:00:00Z', views: 42, uniqueVisitors: 7 }],
      topPerformingEntities: [
        {
          entityType: 'location',
          entityId: 4,
          name: 'Real Location',
          regionDisplayName: 'Assigned Region',
          imageUrl: null,
          pageViews: 42,
          uniqueVisitors: 7,
          saves: 0,
          externalClicks: 0,
          averageRating: 0,
          reviewCount: 0,
          status: 'Published',
        },
      ],
      entityTypeDistribution: [{ entityType: 'place', label: 'Places', count: 1 }],
      engagementFunnel: {
        pageViews: 42,
        uniqueVisitors: 7,
        saves: 3,
        externalClicks: 2,
        reviews: 4,
      },
      actionableInsights: {
        topViewedLocation: {
          entityType: 'location',
          entityId: 4,
          name: 'Real Location',
          regionDisplayName: 'Assigned Region',
          imageUrl: null,
          pageViews: 42,
          uniqueVisitors: 7,
          saves: 0,
          externalClicks: 0,
          averageRating: 0,
          reviewCount: 0,
          status: 'Published',
        },
        mostSaved: null,
        highestPerformingEvent: null,
        messages: [],
      },
      destinationRanking: [
        {
          destinationId: 2,
          destinationName: 'Real Destination',
          regionId: 12,
          regionDisplayName: 'Assigned Region',
          visits: 42,
          rank: 1,
          percentageOfTopValue: 100,
          imageUrl: null,
          detailRoute: '/destinations/detail/2',
        },
      ],
      visitorBehavior: {
        loggedInVisitors: 4,
        anonymousVisitors: 3,
        returningVisitors: 2,
        returningVisitorRatePercent: 28.6,
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
          imageUrl: '/images/place.webp',
          pageViews: 42,
          uniqueVisitors: 7,
          saves: 3,
          externalClicks: 2,
          averageRating: 4.5,
          reviewCount: 4,
          status: 'Published',
        },
      ],
      pagination: {
        totalCount: 1,
        skip: 0,
        take: 25,
        hasMore: false,
      },
      pendingRequestsCount: 5,
    };
  }

  function monthlyVisitsResponse(): RegionalMonthlyVisits {
    return {
      activeRegion: {
        id: 12,
        name: 'Assigned',
        displayName: 'Assigned Region',
        territoryName: 'Testland',
      },
      selectedYearsBack: 2,
      monthLabels: ['Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec', 'Jan', 'Feb', 'Mar', 'Apr', 'May'],
      series: [
        {
          yearLabel: '2024-2025',
          year: 2025,
          startDate: '2024-06-01T00:00:00Z',
          endDate: '2025-05-31T23:59:59Z',
          monthlyVisitCounts: [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2],
          totalVisits: 2,
        },
        {
          yearLabel: '2025-2026',
          year: 2026,
          startDate: '2025-06-01T00:00:00Z',
          endDate: '2026-05-31T23:59:59Z',
          monthlyVisitCounts: [1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4],
          totalVisits: 5,
        },
      ],
      totalVisits: 7,
      visitMetric: 'entity_view',
      visitEventTypes: ['detail_view', 'page_view'],
      timeZone: 'UTC',
    };
  }

  function tagViewsResponse(): RegionalTagViewsAnalyticsResponse {
    return {
      period: '1m',
      regionId: 12,
      regionDisplayName: 'Assigned Region',
      totalViews: 12,
      availableTags: [
        { tagId: 7, name: 'Coastal Walks', entityScopes: ['place'] },
        { tagId: 9, name: 'Museums', entityScopes: ['place'] },
      ],
      items: [
        { tagId: 7, tagName: 'Coastal Walks', views: 8, percentageOfTotal: 66.7, entityScopes: ['place'] },
        { tagId: 9, tagName: 'Museums', views: 4, percentageOfTotal: 33.3, entityScopes: ['place'] },
      ],
    };
  }

  function entityDetailResponse() {
    return {
      entityType: 'place',
      entityId: 8,
      name: 'Real Place',
      title: 'Real Place',
      subtitle: 'A real place',
      status: 'Published',
      regionId: 12,
      regionDisplayName: 'Assigned Region',
      destinationId: 2,
      destinationName: 'Real Destination',
      locationId: 4,
      locationName: 'Real Location',
      placeId: 8,
      placeName: 'Real Place',
      description: 'Real backend detail.',
      summary: 'A real place',
      imageUrl: '/images/place.webp',
      galleryImages: ['/images/place.webp'],
      tags: [{ id: 7, name: 'Coastal Walks', slug: 'coastal-walks' }],
      address: 'Harbor 1',
      latitude: 42.1,
      longitude: 18.1,
      createdAt: '2026-05-01T00:00:00Z',
      updatedAt: '2026-05-02T00:00:00Z',
      publishedAt: '2026-05-03T00:00:00Z',
      analytics: {
        views: 42,
        uniqueVisitors: 7,
        saves: 3,
        externalClicks: 2,
        reviews: 4,
        averageRating: 4.5,
        ratingCount: 4,
        trendChangePercent: 10,
        lastViewedAt: '2026-05-15T00:00:00Z',
      },
      typeSpecific: {
        placeType: 'Cafe',
        stayType: null,
        parentLocation: 'Real Location',
        parentDestination: 'Real Destination',
        website: 'https://example.com',
        bookingExternalLink: null,
        contactPhone: '+111',
        contactEmail: 'hello@example.com',
        priceRange: '$$',
        openingHours: '09:00 - 17:00',
        operatingHours: [],
        amenities: ['WiFi'],
        bookingPartners: [],
        externalPartnerClicks: [],
      },
    };
  }
});
