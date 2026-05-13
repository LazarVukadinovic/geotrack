import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { DashboardService } from './dashboard.service';

describe('DashboardService', () => {
  let service: DashboardService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(DashboardService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('loads assigned regions from the regional admin API', () => {
    service.getAssignedRegions().subscribe((regions) => {
      expect(regions[0].displayName).toBe('Dalmatia, Croatia');
    });

    const request = http.expectOne('/api/admin/regional/regions');
    expect(request.request.method).toBe('GET');
    request.flush([
      { id: 1, name: 'Dalmatia', displayName: 'Dalmatia, Croatia', territoryName: 'Croatia' },
    ]);
  });

  it('loads dashboard data for the selected region without wrapping or remapping the response', () => {
    service.getDashboard(12).subscribe((dashboard) => {
      expect(dashboard.activeRegion.displayName).toBe('Backend Region, Croatia');
      expect(dashboard.stats.pendingRequests).toBe(2);
      expect(dashboard.requestQueuePreview[0].title).toBe('Backend Request');
      expect(dashboard.recentActivity[0].description).toBe('Place added: Backend Place');
    });

    const request = http.expectOne(
      (req) => req.url === '/api/admin/regional/dashboard' && req.params.get('regionId') === '12',
    );
    expect(request.request.method).toBe('GET');
    request.flush({
      activeRegion: {
        id: 12,
        name: 'Backend Region',
        displayName: 'Backend Region, Croatia',
        territoryName: 'Croatia',
      },
      stats: {
        pendingRequests: 2,
        totalDestinations: 1,
        destinationsAddedLastMonth: 1,
        totalPlaces: 8,
        placesAddedLastMonth: 8,
        totalEvents: 3,
        eventsAddedLastMonth: 3,
      },
      requestQueuePreview: [
        {
          requestId: 20,
          title: 'Backend Request',
          submittedByDisplayName: 'Reviewer',
          entityType: 'Place',
          contributionType: 'CreatePlace',
          submittedAt: '2026-05-03T02:22:54.16123Z',
          status: 'PendingReview',
          thumbnailImageUrl: null,
          detailRoute: '/request/detail/20',
        },
      ],
      recentActivity: [
        {
          id: 'place-8',
          entityType: 'place',
          entityId: 8,
          action: 'Created place',
          description: 'Place added: Backend Place',
          occurredAt: '2026-05-03T02:22:54.16123Z',
          status: 'Created',
          detailRoute: '/places/detail/8',
        },
      ],
    });
  });

  it('builds stat cards from backend counts', () => {
    const stats = service.buildStats({
      pendingRequests: 3,
      totalDestinations: 12,
      destinationsAddedLastMonth: 2,
      totalPlaces: 1234,
      placesAddedLastMonth: 9,
      totalEvents: 5,
      eventsAddedLastMonth: 1,
    });

    expect(stats[0].value).toBe('3');
    expect(stats[1].value).toBe('12');
    expect(stats[2].value).toBe('1,234');
    expect(stats[2].sub).toBe('9 in last 30 days');
  });
});
