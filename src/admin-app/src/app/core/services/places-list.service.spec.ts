import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { PlacesListService } from './places-list.service';

describe('PlacesListService', () => {
  let service: PlacesListService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(PlacesListService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('requests regional places with backend paging, search, sort, and filters', () => {
    service.list({
      regionId: 7,
      search: 'museum',
      sort: 'nameAsc',
      status: 'published',
      placeType: 'Museum',
      skip: 25,
      take: 25,
    }).subscribe();

    const request = http.expectOne(req => req.url === '/api/admin/regional/places');
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('regionId')).toBe('7');
    expect(request.request.params.get('search')).toBe('museum');
    expect(request.request.params.get('sort')).toBe('nameAsc');
    expect(request.request.params.get('status')).toBe('published');
    expect(request.request.params.get('placeType')).toBe('Museum');
    expect(request.request.params.get('skip')).toBe('25');
    expect(request.request.params.get('take')).toBe('25');
    request.flush({ items: [], totalCount: 0, skip: 25, take: 25 });
  });

  it('creates a regional place without sending an internal reference id', () => {
    service.createPlace({
      regionId: 7,
      locationId: 11,
      placeTypeId: 3,
      name: 'New Place',
      summary: 'Short summary.',
      description: 'Longer description.',
      status: 'Published',
      latitude: 44.1,
      longitude: 20.2,
      streetAddress: 'Main street 1',
      amenities: ['wifi', 'parking'],
      operatingHoursEnabled: false,
      operatingHours: [],
      tagIds: [1, 2],
    }).subscribe();

    const request = http.expectOne('/api/admin/regional/places');
    expect(request.request.method).toBe('POST');
    expect(request.request.body.regionId).toBe(7);
    expect(request.request.body.locationId).toBe(11);
    expect(request.request.body.placeTypeId).toBe(3);
    expect(request.request.body.tagIds).toEqual([1, 2]);
    expect(request.request.body.amenities).toEqual(['wifi', 'parking']);
    expect(request.request.body.internalReferenceId).toBeUndefined();
    request.flush({
      id: 41,
      name: 'New Place',
      status: 'Published',
      regionId: 7,
      regionDisplayName: 'Region',
      latitude: 44.1,
      longitude: 20.2,
      operatingHoursEnabled: false,
      operatingHours: [],
    });
  });

  it('loads backend-scoped parent locations, place tags, and place types', () => {
    service.getParentLocations(7, 'old', 10).subscribe();
    const parents = http.expectOne(req => req.url === '/api/admin/regional/place-parent-locations');
    expect(parents.request.params.get('regionId')).toBe('7');
    expect(parents.request.params.get('search')).toBe('old');
    expect(parents.request.params.get('take')).toBe('10');
    parents.flush([]);

    service.getPlaceTags().subscribe();
    const tags = http.expectOne('/api/admin/regional/place-tags');
    expect(tags.request.method).toBe('GET');
    tags.flush([]);

    service.searchPlaceTags('museum').subscribe();
    const searchTags = http.expectOne(req => req.url === '/api/admin/regional/place-tags');
    expect(searchTags.request.params.get('search')).toBe('museum');
    searchTags.flush([]);

    service.getPlaceTypes().subscribe();
    const types = http.expectOne('/api/admin/regional/place-types');
    expect(types.request.method).toBe('GET');
    types.flush([]);
  });
});
