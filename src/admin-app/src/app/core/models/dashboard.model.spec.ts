import { RegionalDashboard } from './dashboard.model';

describe('dashboard models', () => {
  it('supports the regional dashboard API shape', () => {
    const dashboard: RegionalDashboard = {
      activeRegion: { id: 1, name: 'Dalmatia', displayName: 'Dalmatia, Croatia' },
      stats: {
        pendingRequests: 1,
        totalDestinations: 2,
        destinationsAddedLastMonth: 1,
        totalPlaces: 3,
        placesAddedLastMonth: 1,
        totalEvents: 4,
        eventsAddedLastMonth: 1,
      },
      requestQueuePreview: [],
      recentActivity: [],
    };

    expect(dashboard.activeRegion.id).toBe(1);
    expect(dashboard.stats.totalPlaces).toBe(3);
  });
});
