import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { vi } from 'vitest';

import { AllEventsComponent } from './all-events.component';
import { RegionalEventListResponse } from '../../../../../core/models/event.model';

describe('AllEventsComponent', () => {
  let component: AllEventsComponent;
  let fixture: ComponentFixture<AllEventsComponent>;
  let http: HttpTestingController;

  beforeEach(async () => {
    sessionStorage.clear();

    await TestBed.configureTestingModule({
      imports: [AllEventsComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(AllEventsComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify({ ignoreCancelled: true });
    sessionStorage.clear();
    vi.useRealTimers();
  });

  it('loads first 25 events for the active assigned region', () => {
    fixture.detectChanges();
    flushRegions();
    flushEvents(7, eventPage(makeEvents(25), 60), req =>
      req.params.get('skip') === '0' && req.params.get('take') === '25',
    );

    expect(component.activeRegionId).toBe(7);
    expect(component.items.length).toBe(25);
    expect(component.totalItems).toBe(60);
    expect(component.hasMore).toBe(true);
    expect(component.isLoading).toBe(false);
    expect(component.items.every(item => item.name.startsWith('Backend Event'))).toBe(true);
  });

  it('renders backend rows and footer without mock event rows', () => {
    fixture.detectChanges();
    flushRegions();
    flushEvents(7, eventPage([eventDto(1, 'Backend Event')], 1));

    fixture.detectChanges();
    const text = normalizedText();

    expect(text).toContain('Backend Event');
    expect(text).toContain('Showing 1 of 1 event');
  });

  it('show more appends the next backend page and updates the footer count', () => {
    fixture.detectChanges();
    flushRegions();
    flushEvents(7, eventPage(makeEvents(25), 40));

    component.loadMore();
    flushEvents(7, eventPage(makeEvents(15, 26), 40, 25), req =>
      req.params.get('skip') === '25' && req.params.get('take') === '25',
    );

    expect(component.items.length).toBe(40);
    expect(component.totalItems).toBe(40);
    expect(component.hasMore).toBe(false);
    expect(component.isLoadingMore).toBe(false);
  });

  it('debounces search, resets pagination, and loads backend matches', () => {
    vi.useFakeTimers();
    fixture.detectChanges();
    flushRegions();
    flushEvents(7, eventPage(makeEvents(25), 40));

    component.searchQuery = 'festival';
    component.onSearchChange();
    vi.advanceTimersByTime(300);

    flushEvents(7, eventPage([eventDto(7, 'Festival Event')], 1), req =>
      req.params.get('search') === 'festival' && req.params.get('skip') === '0',
    );

    expect(component.items.map(item => item.name)).toEqual(['Festival Event']);
    expect(component.totalItems).toBe(1);
  });

  it('sort changes reset pagination and preserve the current search value', () => {
    fixture.detectChanges();
    flushRegions();
    flushEvents(7, eventPage(makeEvents(25), 40));

    component.searchQuery = 'river';
    component.applySortOption({ label: 'Event Date Soonest', value: 'eventDateAsc' });

    flushEvents(7, eventPage([eventDto(12, 'Soonest Event')], 1), req =>
      req.params.get('sort') === 'eventDateAsc' &&
      req.params.get('search') === 'river' &&
      req.params.get('skip') === '0',
    );

    expect(component.currentSort).toBe('eventDateAsc');
    expect(component.items[0].name).toBe('Soonest Event');
  });

  it('clears loading and shows an empty state for no events', () => {
    fixture.detectChanges();
    flushRegions();
    flushEvents(7, eventPage([], 0));

    expect(component.isLoading).toBe(false);
    expect(component.items).toEqual([]);
    expect(component.emptyMessage).toBe('No events found in this region.');
  });

  it('clears loading and shows a safe error state when the API fails', () => {
    fixture.detectChanges();
    flushRegions();

    const request = http.expectOne(req => req.url === '/api/admin/regional/events');
    request.flush({ message: 'Server error' }, { status: 500, statusText: 'Server Error' });

    expect(component.isLoading).toBe(false);
    expect(component.items).toEqual([]);
    expect(component.errorMessage).toBe('Unable to load events.');
  });

  it('opens delete confirmation and removes a row after successful delete', () => {
    fixture.detectChanges();
    flushRegions();
    flushEvents(7, eventPage([eventDto(15, 'Delete Me'), eventDto(16, 'Keep Me')], 2));

    component.promptDelete(component.items[0]);
    expect(component.deleteTarget?.name).toBe('Delete Me');

    component.confirmDelete();

    const request = http.expectOne(
      req =>
        req.url === '/api/admin/regional/events/15' &&
        req.params.get('regionId') === '7',
    );
    expect(request.request.method).toBe('DELETE');
    request.flush(null);

    expect(component.deleteTarget).toBeNull();
    expect(component.items.map(item => item.name)).toEqual(['Keep Me']);
    expect(component.totalItems).toBe(1);
  });

  it('shows the backend delete error when related content blocks deletion', () => {
    fixture.detectChanges();
    flushRegions();
    flushEvents(7, eventPage([eventDto(15, 'Protected Event')], 1));

    component.promptDelete(component.items[0]);
    component.confirmDelete();

    const request = http.expectOne('/api/admin/regional/events/15?regionId=7');
    request.flush(
      { message: 'This event cannot be deleted while it has related content.' },
      { status: 409, statusText: 'Conflict' },
    );

    expect(component.isDeleting).toBe(false);
    expect(component.errorMessage).toBe('This event cannot be deleted while it has related content.');
  });

  it('changing active region reloads the first page for the new region', () => {
    fixture.detectChanges();
    flushRegions([
      { id: 7, name: 'Backend Region', displayName: 'Backend Region' },
      { id: 8, name: 'Second Region', displayName: 'Second Region' },
    ]);
    flushEvents(7, eventPage([eventDto(1, 'Region 7 Event')], 1));

    component.onRegionSelected(8);
    flushEvents(8, eventPage([eventDto(2, 'Region 8 Event', 8)], 1));

    expect(component.activeRegionId).toBe(8);
    expect(component.items.map(item => item.name)).toEqual(['Region 8 Event']);
  });

  it('navigates edit through the existing event create/edit flow with active region context', () => {
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    fixture.detectChanges();
    flushRegions();
    flushEvents(7, eventPage([eventDto(21, 'Editable Event')], 1));

    component.editEvent(component.items[0]);

    expect(navigateSpy).toHaveBeenCalledWith(['/events/new'], {
      queryParams: { regionId: 7 },
      state: {
        place: expect.objectContaining({ id: '21', name: 'Editable Event' }),
        editMode: true,
        returnTo: '/events',
        regionId: 7,
        regionDisplayName: 'Backend Region',
      },
    });
  });

  function flushRegions(
    regions = [{ id: 7, name: 'Backend Region', displayName: 'Backend Region' }],
  ): void {
    const request = http.expectOne('/api/admin/regional/regions');
    expect(request.request.method).toBe('GET');
    request.flush(regions);
  }

  function flushEvents(
    regionId: number,
    response: RegionalEventListResponse,
    extra?: (req: import('@angular/common/http').HttpRequest<unknown>) => boolean,
  ): void {
    const request = http.expectOne(
      req =>
        req.url === '/api/admin/regional/events' &&
        req.params.get('regionId') === String(regionId) &&
        (!extra || extra(req)),
    );
    expect(request.request.method).toBe('GET');
    request.flush(response);
  }

  function normalizedText(): string {
    return (fixture.nativeElement.textContent as string).replace(/\s+/g, ' ').trim();
  }

  function eventPage(
    items: ReturnType<typeof eventDto>[],
    totalCount = items.length,
    skip = 0,
  ): RegionalEventListResponse {
    return {
      items,
      totalCount,
      skip,
      take: 25,
    };
  }

  function makeEvents(count: number, start = 1): ReturnType<typeof eventDto>[] {
    return Array.from({ length: count }, (_, index) => {
      const id = start + index;
      return eventDto(id, `Backend Event ${id}`);
    });
  }

  function eventDto(id: number, name: string, regionId = 7) {
    return {
      id,
      name,
      summary: `${name} summary`,
      description: `${name} backend description`,
      eventType: 'Festival',
      eventTypeId: 2,
      category: 'Festival',
      status: 'Published',
      regionId,
      regionDisplayName: regionId === 7 ? 'Backend Region' : 'Second Region',
      destinationId: 3,
      destinationName: regionId === 7 ? 'Backend Destination' : 'Second Destination',
      locationId: 9,
      locationName: regionId === 7 ? 'Backend Location' : 'Second Location',
      placeId: id === 5 ? 11 : null,
      placeName: id === 5 ? 'Backend Place' : null,
      address: null,
      startsAt: '2026-05-20T12:00:00Z',
      endsAt: '2026-05-20T14:00:00Z',
      createdAt: '2026-05-10T10:00:00Z',
      lastUpdatedAt: '2026-05-12T10:00:00Z',
      imageUrl: null,
      detailRoute: `/events/detail/${id}`,
      editRoute: '/events/new',
      latitude: 42.1,
      longitude: 18.1,
    };
  }
});
