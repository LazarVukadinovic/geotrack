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

  it('creates regional events without a client-provided internal reference id', () => {
    service.createEvent({
      regionId: 7,
      parentEntityType: 'Location',
      destinationId: 3,
      locationId: 4,
      eventTypeId: 2,
      name: 'Backend Event',
      summary: 'Backend event summary',
      description: 'Backend event description',
      status: 'Published',
      isAllDay: false,
      startsAt: '2026-05-20T12:00:00.000Z',
      endsAt: '2026-05-20T14:00:00.000Z',
      isRecurring: false,
      isFree: true,
      tagIds: [11],
    }).subscribe(response => {
      expect(response.id).toBe(15);
    });

    const request = http.expectOne('/api/admin/regional/events');
    expect(request.request.method).toBe('POST');
    expect(request.request.body.internalReferenceId).toBeUndefined();
    expect(request.request.body.regionId).toBe(7);
    expect(request.request.body.locationId).toBe(4);
    expect(request.request.body.tagIds).toEqual([11]);
    request.flush(eventDto());
  });

  it('loads event parents, tags, and types from regional backend endpoints', () => {
    service.getParentLocations(7).subscribe(response => expect(response[0].destinationId).toBe(3));
    service.getParentPlaces(7).subscribe(response => expect(response[0].locationId).toBe(4));
    service.getParentDestinations(7).subscribe(response => expect(response[0].id).toBe(3));
    service.getEventTags('music').subscribe(response => expect(response[0].slug).toBe('music'));
    service.getEventTypes().subscribe(response => expect(response[0].name).toBe('Festival'));

    const locationRequest = http.expectOne(req => req.url === '/api/admin/regional/event-parent-locations');
    expect(locationRequest.request.params.get('regionId')).toBe('7');
    locationRequest.flush([{ id: 4, name: 'Backend Location', displayName: 'Backend Location', destinationId: 3, destinationName: 'Backend Destination', status: 'Published' }]);

    const placeRequest = http.expectOne(req => req.url === '/api/admin/regional/event-parent-places');
    expect(placeRequest.request.params.get('regionId')).toBe('7');
    placeRequest.flush([{ id: 5, name: 'Backend Place', displayName: 'Backend Place', locationId: 4, locationName: 'Backend Location', destinationId: 3, destinationName: 'Backend Destination', status: 'Published' }]);

    const destinationRequest = http.expectOne(req => req.url === '/api/admin/regional/event-parent-destinations');
    expect(destinationRequest.request.params.get('regionId')).toBe('7');
    destinationRequest.flush([{ id: 3, name: 'Backend Destination', displayName: 'Backend Destination', status: 'Published' }]);

    const tagsRequest = http.expectOne(req => req.url === '/api/admin/regional/event-tags');
    expect(tagsRequest.request.params.get('search')).toBe('music');
    tagsRequest.flush([{ id: 11, name: 'Music', slug: 'music' }]);

    const typesRequest = http.expectOne('/api/admin/regional/event-types');
    typesRequest.flush([{ id: 2, name: 'Festival' }]);
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
