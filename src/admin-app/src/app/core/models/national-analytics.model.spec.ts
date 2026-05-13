import type { NationalAnalyticsData } from './national-analytics.model';

describe('NationalAnalyticsData', () => {
  it('accepts the national analytics response shape', () => {
    const data: NationalAnalyticsData = {
      kpis: [],
      topRegion: { name: 'Sumadija', score: 94, note: 'Leading engagement' },
      fastestGrowing: { name: 'Banat', change: '+12%' },
      topEntities: [],
      pageViewsData: [],
      topDestinations: [],
      topEvents: [],
      topPlaces: [],
      topLocations: [],
      contentQuality: [],
      regionRows: [],
      alerts: [],
    };

    expect(data.topRegion.score).toBe(94);
    expect(data.fastestGrowing.change).toBe('+12%');
  });
});
