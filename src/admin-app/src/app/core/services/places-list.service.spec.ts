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
});
