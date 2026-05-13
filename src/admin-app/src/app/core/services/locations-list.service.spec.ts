import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { LocationsListService } from './locations-list.service';

describe('LocationsListService', () => {
  let service: LocationsListService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(LocationsListService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('loads regional locations with backend paging, search, and sort parameters', () => {
    service
      .list({
        regionId: 7,
        search: 'harbor',
        sort: 'nameAsc',
        skip: 25,
        take: 25,
      })
      .subscribe(response => {
        expect(response.totalCount).toBe(40);
        expect(response.items[0].name).toBe('Backend Location');
      });

    const request = http.expectOne(req => req.url === '/api/admin/regional/locations');
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('regionId')).toBe('7');
    expect(request.request.params.get('search')).toBe('harbor');
    expect(request.request.params.get('sort')).toBe('nameAsc');
    expect(request.request.params.get('skip')).toBe('25');
    expect(request.request.params.get('take')).toBe('25');
    request.flush({
      items: [locationDto()],
      totalCount: 40,
      skip: 25,
      take: 25,
    });
  });

  it('deletes with region scope and never calls a mock collection', () => {
    service.deleteLocation(7, 15).subscribe();

    const request = http.expectOne(
      req =>
        req.url === '/api/admin/regional/locations/15' &&
        req.params.get('regionId') === '7',
    );
    expect(request.request.method).toBe('DELETE');
    request.flush(null);
  });

  it('maps backend DTOs into the legacy list item shape without mock ids', () => {
    const item = service.toListItem(locationDto());

    expect(item.id).toBe('15');
    expect(item.name).toBe('Backend Location');
    expect(item.region).toBe('Backend Region');
    expect(item.category).toBe('District');
    expect(item.destinationName).toBe('Backend Destination');
    expect(item.summary).toBe('District in Backend Destination');
    expect(item.lastUpdated).toContain('May');
    expect(service.findById(15)?.name).toBe('Backend Location');
  });

  function locationDto() {
    return {
      id: 15,
      name: 'Backend Location',
      summary: 'District in Backend Destination',
      description: null,
      locationType: 'District',
      locationTypeId: 2,
      status: 'Published',
      regionId: 7,
      regionDisplayName: 'Backend Region',
      destinationId: 3,
      destinationName: 'Backend Destination',
      createdAt: '2026-05-10T10:00:00Z',
      lastUpdatedAt: '2026-05-12T10:00:00Z',
      imageUrl: '/media/seed/locations/backend.webp',
      detailRoute: '/locations/detail/15',
      editRoute: '/locations/new',
    };
  }
});
