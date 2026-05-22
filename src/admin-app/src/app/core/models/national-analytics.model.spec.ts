import type { TerritoryAnalyticsOverview } from './national-analytics.model';

describe('TerritoryAnalyticsOverview', () => {
  it('accepts the territory analytics response shape', () => {
    const data: TerritoryAnalyticsOverview = {
      activeTerritory: { id: 1, name: 'Assigned Territory', displayName: 'Assigned Territory' },
      filters: {
        period: '6m',
        startDate: '2026-01-01T00:00:00Z',
        endDate: '2026-05-01T00:00:00Z',
        selectedRegionIds: [10],
        status: 'active',
      },
      totals: {
        totalPageViews: 12,
        uniqueVisitors: 4,
        externalLinkClicks: 2,
        totalSaves: 3,
        totalReviews: 5,
        averageRating: 4.2,
      },
      comparison: {
        pageViewsChangePercent: 10,
        uniqueVisitorsChangePercent: 0,
        externalClicksChangePercent: 0,
        savesChangePercent: 0,
        reviewsChangePercent: 0,
        averageRatingChange: 0.1,
      },
      topRegion: { regionId: 10, regionName: 'Assigned Region', score: 88, healthScore: 88, description: 'Real score', visits: 12, uniqueVisitors: 4 },
      fastestGrowing: null,
      topEntities: [],
      pageViewsOverTime: { monthLabels: ['May'], series: [] },
      topDestinations: [],
      topLocations: [],
      topPlaces: [],
      topEvents: [],
      contentQualityByRegion: [],
      regionalPerformance: { items: [], totalCount: 0, skip: 0, take: 5, hasMore: false },
    };

    expect(data.totals.totalPageViews).toBe(12);
    expect(data.topRegion?.score).toBe(88);
  });
});
