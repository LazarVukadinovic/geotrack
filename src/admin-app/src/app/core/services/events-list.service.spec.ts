import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { EventsListService } from './events-list.service';

describe('EventsListService', () => {
  let service: EventsListService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(EventsListService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('requests regional events with backend paging, search, sort, and filters', () => {
    service.list({
      regionId: 7,
      search: 'festival',
      sort: 'eventDateAsc',
      status: 'published',
      eventType: 'Festival',
      skip: 25,
      take: 25,
    }).subscribe(response => {
      expect(response.totalCount).toBe(40);
      expect(response.items[0].name).toBe('Backend Event');
    });

    const request = http.expectOne(req => req.url === '/api/admin/regional/events');
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('regionId')).toBe('7');
    expect(request.request.params.get('search')).toBe('festival');
    expect(request.request.params.get('sort')).toBe('eventDateAsc');
    expect(request.request.params.get('status')).toBe('published');
    expect(request.request.params.get('eventType')).toBe('Festival');
    expect(request.request.params.get('skip')).toBe('25');
    expect(request.request.params.get('take')).toBe('25');
    request.flush({
      items: [eventDto()],
      totalCount: 40,
      skip: 25,
      take: 25,
    });
  });

  it('deletes with region scope and never calls a mock collection', () => {
    service.deleteEvent(7, 15).subscribe();

    const request = http.expectOne(
      req =>
        req.url === '/api/admin/regional/events/15' &&
        req.params.get('regionId') === '7',
    );
    expect(request.request.method).toBe('DELETE');
    request.flush(null);
  });

  it('maps backend DTOs into the legacy list item shape without mock ids', () => {
    const item = service.toListItem(eventDto());

    expect(item.id).toBe('15');
    expect(item.name).toBe('Backend Event');
    expect(item.region).toBe('Backend Region');
    expect(item.category).toBe('Festival');
    expect(item.locationName).toBe('Backend Location');
    expect(item.venueName).toBe('Backend Place');
    expect(item.dateRange).toContain('May');
    expect(service.findById(15)?.name).toBe('Backend Event');
  });

  function eventDto() {
    return {
      id: 15,
      name: 'Backend Event',
      summary: 'Backend event summary',
      description: 'Backend event description',
      eventType: 'Festival',
      eventTypeId: 2,
      category: 'Festival',
      status: 'Published',
      regionId: 7,
      regionDisplayName: 'Backend Region',
      destinationId: 3,
      destinationName: 'Backend Destination',
      locationId: 4,
      locationName: 'Backend Location',
      placeId: 5,
      placeName: 'Backend Place',
      address: 'Backend address',
      startsAt: '2026-05-20T12:00:00Z',
      endsAt: '2026-05-20T14:00:00Z',
      createdAt: '2026-05-10T10:00:00Z',
      lastUpdatedAt: '2026-05-12T10:00:00Z',
      imageUrl: '/media/seed/events/backend.webp',
      detailRoute: '/events/detail/15',
      editRoute: '/events/new',
      latitude: 42.1,
      longitude: 18.1,
    };
  }
});
