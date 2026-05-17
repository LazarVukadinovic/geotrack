import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { DestinationsListService } from './destinations-list.service';

describe('DestinationsListService', () => {
  let service: DestinationsListService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(DestinationsListService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('loads regional destinations with backend paging, search, and sort parameters', () => {
    service
      .list({
        regionId: 7,
        search: 'orchards',
        sort: 'nameAsc',
        skip: 25,
        take: 25,
      })
      .subscribe(response => {
        expect(response.totalCount).toBe(40);
        expect(response.items[0].name).toBe('Backend Destination');
      });

    const request = http.expectOne(req => req.url === '/api/admin/regional/destinations');
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('regionId')).toBe('7');
    expect(request.request.params.get('search')).toBe('orchards');
    expect(request.request.params.get('sort')).toBe('nameAsc');
    expect(request.request.params.get('skip')).toBe('25');
    expect(request.request.params.get('take')).toBe('25');
    request.flush({
      items: [destinationDto()],
      totalCount: 40,
      skip: 25,
      take: 25,
    });
  });

  it('deletes with region scope and never calls a mock collection', () => {
    service.deleteDestination(7, 15).subscribe();

    const request = http.expectOne(
      req =>
        req.url === '/api/admin/regional/destinations/15' &&
        req.params.get('regionId') === '7',
    );
    expect(request.request.method).toBe('DELETE');
    request.flush(null);
  });

  it('maps backend DTOs into the legacy list item shape without mock ids', () => {
    const item = service.toListItem(destinationDto());

    expect(item.id).toBe('15');
    expect(item.name).toBe('Backend Destination');
    expect(item.region).toBe('Backend Region');
    expect(item.category).toBe('Mountain');
    expect(item.summary).toBe('Backend summary');
    expect(item.lastUpdated).toContain('May');
    expect(service.findById(15)?.name).toBe('Backend Destination');
  });

  function destinationDto() {
    return {
      id: 15,
      name: 'Backend Destination',
      summary: 'Backend summary',
      description: 'Backend description',
      status: 'Published',
      regionId: 7,
      regionDisplayName: 'Backend Region',
      destinationTypeId: 3,
      destinationTypeName: 'Mountain',
      createdAt: '2026-05-10T10:00:00Z',
      lastUpdatedAt: '2026-05-12T10:00:00Z',
      imageUrl: '/media/seed/destinations/backend.webp',
      detailRoute: '/destinations/detail/15',
      editRoute: '/destinations/edit/15',
    };
  }
});
