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

  it('creates a regional location without accepting a client internal reference id', () => {
    const payload = {
      regionId: 7,
      destinationId: 3,
      name: 'New Backend Location',
      summary: 'Short summary',
      description: '<p>Description</p>',
      status: 'Published' as const,
      locationTypeId: 2,
      latitude: 43,
      longitude: 19,
      boundaryNote: 'North edge',
      boundary: {
        shapeType: 'Polygon' as const,
        coordinates: [
          { latitude: 43, longitude: 19 },
          { latitude: 43, longitude: 19.2 },
          { latitude: 43.2, longitude: 19.2 },
        ],
      },
      tagIds: [5, 7],
    };

    service.createLocation(payload).subscribe(response => {
      expect(response.id).toBe(16);
      expect(response.internalReferenceId).toBe('LOC-000016');
    });

    const request = http.expectOne('/api/admin/regional/locations');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(payload);
    expect(request.request.body.internalReferenceId).toBeUndefined();
    request.flush({
      ...locationDto(),
      id: 16,
      name: 'New Backend Location',
      internalReferenceId: 'LOC-000016',
    });
  });

  it('loads parent destinations from the active region selector endpoint', () => {
    service.getParentDestinations(7, 'river', 10).subscribe(response => {
      expect(response[0].displayName).toBe('Backend Destination');
    });

    const request = http.expectOne(req => req.url === '/api/admin/regional/location-parent-destinations');
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('regionId')).toBe('7');
    expect(request.request.params.get('search')).toBe('river');
    expect(request.request.params.get('take')).toBe('10');
    request.flush([
      { id: 3, name: 'Backend Destination', displayName: 'Backend Destination', status: 'Published' },
    ]);
  });

  it('loads and searches only backend location-compatible tag endpoints', () => {
    service.getLocationTags().subscribe(response => {
      expect(response.map(tag => tag.name)).toEqual(['Historic']);
    });

    const listRequest = http.expectOne('/api/admin/regional/location-tags');
    expect(listRequest.request.method).toBe('GET');
    listRequest.flush([{ id: 5, name: 'Historic', slug: 'historic' }]);

    service.searchLocationTags('old town').subscribe(response => {
      expect(response[0].slug).toBe('old-town');
    });

    const searchRequest = http.expectOne(req => req.url === '/api/admin/regional/location-tags');
    expect(searchRequest.request.method).toBe('GET');
    expect(searchRequest.request.params.get('search')).toBe('old town');
    searchRequest.flush([{ id: 7, name: 'Old Town', slug: 'old-town' }]);
  });

  it('loads backend location types for the category dropdown', () => {
    service.getLocationTypes().subscribe(response => {
      expect(response[0].name).toBe('District');
    });

    const request = http.expectOne('/api/admin/regional/location-types');
    expect(request.request.method).toBe('GET');
    request.flush([{ id: 2, name: 'District', description: 'District type' }]);
  });

  it('maps backend DTOs into the legacy list item shape without mock ids', () => {
    const item = service.toListItem(locationDto());

    expect(item.id).toBe('15');
    expect(item.name).toBe('Backend Location');
    expect(item.internalRefId).toBe('LOC-000015');
    expect(item.region).toBe('Backend Region');
    expect(item.category).toBe('District');
    expect(item.destinationName).toBe('Backend Destination');
    expect(item.summary).toBe('District in Backend Destination');
    expect(item.tags).toEqual(['Historic']);
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
      internalReferenceId: 'LOC-000015',
      regionId: 7,
      regionDisplayName: 'Backend Region',
      destinationId: 3,
      destinationName: 'Backend Destination',
      tags: [{ id: 5, name: 'Historic', slug: 'historic' }],
      createdAt: '2026-05-10T10:00:00Z',
      lastUpdatedAt: '2026-05-12T10:00:00Z',
      imageUrl: '/media/seed/locations/backend.webp',
      detailRoute: '/locations/detail/15',
      editRoute: '/locations/new',
    };
  }
});
