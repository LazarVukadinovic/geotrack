import type { RegionalAnalyticsData } from './regional-analytics.model';

describe('RegionalAnalyticsData', () => {
  it('accepts the regional analytics response shape', () => {
    const data: RegionalAnalyticsData = {
      metrics: {
        all: [],
        locations: [],
        destinations: [],
        events: [],
        places: [],
        stays: [],
      },
      categories: [],
      entityRows: [],
      actionableInsights: [],
      insights: [],
      distribution: {
        all: [],
        locations: [],
        destinations: [],
        events: [],
        places: [],
        stays: [],
      },
      pageViewsTrend: {
        all: [],
        locations: [],
        destinations: [],
        events: [],
        places: [],
        stays: [],
      },
    };

    expect(data.metrics.all).toEqual([]);
    expect(data.pageViewsTrend.places).toEqual([]);
  });
});
