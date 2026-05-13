import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AdminMapService } from './admin-map.service';

describe('AdminMapService', () => {
  let service: AdminMapService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(AdminMapService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('requests regional map entities with active region and marker types', () => {
    service.getMap(17, ['destination', 'location', 'place']).subscribe(response => {
      expect(response.markers.length).toBe(0);
      expect(response.zones.length).toBe(0);
    });

    const request = http.expectOne(req =>
      req.url === '/api/admin/regional/map/entities' &&
      req.params.get('regionId') === '17' &&
      req.params.get('types') === 'destination,location,place',
    );

    expect(request.request.method).toBe('GET');
    request.flush({
      viewport: { isEmpty: true },
      markers: [],
      zones: [],
      emptyStateMessage: 'No mapped entities found in this assigned region.',
    });
  });

  it('requests region-scoped map search results', () => {
    service.search(17, 'museum').subscribe(response => {
      expect(response.items[0].title).toBe('Museum');
    });

    const request = http.expectOne(req =>
      req.url === '/api/admin/regional/map/search' &&
      req.params.get('regionId') === '17' &&
      req.params.get('query') === 'museum',
    );

    expect(request.request.method).toBe('GET');
    request.flush({
      items: [
        {
          id: 5,
          entityType: 'Place',
          title: 'Museum',
          latitude: 45.1,
          longitude: 16.2,
        },
      ],
    });
  });
});
