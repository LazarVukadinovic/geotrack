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
    service.getAssignedRegions().subscribe(regions => {
      expect(regions[0].displayName).toBe('Dalmatia, Croatia');
    });

    const request = http.expectOne('/api/admin/regional/regions');
    expect(request.request.method).toBe('GET');
    request.flush([{ id: 1, name: 'Dalmatia', displayName: 'Dalmatia, Croatia', territoryName: 'Croatia' }]);
  });

  it('loads dashboard data for the selected region', () => {
    service.getDashboard(12).subscribe();

    const request = http.expectOne(req =>
      req.url === '/api/admin/regional/dashboard' &&
      req.params.get('regionId') === '12');
    expect(request.request.method).toBe('GET');
    request.flush({ activeRegion: { id: 12, name: 'Region', displayName: 'Region' }, stats: {}, requestQueuePreview: [], recentActivity: [] });
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
