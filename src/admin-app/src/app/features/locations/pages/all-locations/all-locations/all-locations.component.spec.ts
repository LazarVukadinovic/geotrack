import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { vi } from 'vitest';

import { AllLocationsComponent } from './all-locations.component';
import { RegionalLocationListResponse } from '../../../../../core/models/location.model';

describe('AllLocationsComponent', () => {
  let component: AllLocationsComponent;
  let fixture: ComponentFixture<AllLocationsComponent>;
  let http: HttpTestingController;

  beforeEach(async () => {
    sessionStorage.clear();

    await TestBed.configureTestingModule({
      imports: [AllLocationsComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(AllLocationsComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify({ ignoreCancelled: true });
    sessionStorage.clear();
    vi.useRealTimers();
  });

  it('loads first 25 locations for the active assigned region', () => {
    fixture.detectChanges();
    flushRegions();
    flushLocations(
      7,
      locationPage(makeLocations(25), 60),
      (req) => req.params.get('skip') === '0' && req.params.get('take') === '25',
    );

    expect(component.activeRegionId).toBe(7);
    expect(component.items.length).toBe(25);
    expect(component.totalItems).toBe(60);
    expect(component.hasMore).toBe(true);
    expect(component.isLoading).toBe(false);
    expect(component.items.map((item) => item.name)).not.toContain('Oplenac Royal Complex');
  });

  it('renders backend rows and footer without mock location rows', () => {
    fixture.detectChanges();
    flushRegions();
    flushLocations(7, locationPage([locationDto(1, 'Backend Location')], 1));

    fixture.detectChanges();
    const text = normalizedText();

    expect(text).toContain('Backend Location');
    expect(text).toContain('Showing 1 of 1 location');
    expect(text).not.toContain('Kragujevac Memorial Park');
    expect(text).not.toContain('Gledic Mountains Reserve');
  });

  it('show more appends the next backend page and updates the footer count', () => {
    fixture.detectChanges();
    flushRegions();
    flushLocations(7, locationPage(makeLocations(25), 40));

    component.loadMore();
    flushLocations(
      7,
      locationPage(makeLocations(15, 26), 40, 25),
      (req) => req.params.get('skip') === '25' && req.params.get('take') === '25',
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
    flushLocations(7, locationPage(makeLocations(25), 40));

    component.searchQuery = 'harbor';
    component.onSearchChange();
    vi.advanceTimersByTime(300);

    flushLocations(
      7,
      locationPage([locationDto(7, 'Harbor Location')], 1),
      (req) => req.params.get('search') === 'harbor' && req.params.get('skip') === '0',
    );

    expect(component.items.map((item) => item.name)).toEqual(['Harbor Location']);
    expect(component.totalItems).toBe(1);
  });

  it('sort changes reset pagination and preserve the current search value', () => {
    fixture.detectChanges();
    flushRegions();
    flushLocations(7, locationPage(makeLocations(25), 40));

    component.searchQuery = 'river';
    component.applySortOption({ label: 'Name Z-A', value: 'nameDesc' });

    flushLocations(
      7,
      locationPage([locationDto(12, 'Zeta Location')], 1),
      (req) =>
        req.params.get('sort') === 'nameDesc' &&
        req.params.get('search') === 'river' &&
        req.params.get('skip') === '0',
    );

    expect(component.currentSort).toBe('nameDesc');
    expect(component.items[0].name).toBe('Zeta Location');
  });

  it('clears loading and shows an empty state for no locations', () => {
    fixture.detectChanges();
    flushRegions();
    flushLocations(7, locationPage([], 0));

    expect(component.isLoading).toBe(false);
    expect(component.items).toEqual([]);
    expect(component.emptyMessage).toBe('No locations found in this region.');
  });

  it('clears loading and shows a safe error state when the API fails', () => {
    fixture.detectChanges();
    flushRegions();

    const request = http.expectOne((req) => req.url === '/api/admin/regional/locations');
    request.flush({ message: 'Server error' }, { status: 500, statusText: 'Server Error' });

    expect(component.isLoading).toBe(false);
    expect(component.items).toEqual([]);
    expect(component.errorMessage).toBe('Unable to load locations.');
  });

  it('opens delete confirmation and removes a row after successful delete', () => {
    fixture.detectChanges();
    flushRegions();
    flushLocations(7, locationPage([locationDto(15, 'Delete Me'), locationDto(16, 'Keep Me')], 2));

    component.promptDelete(component.items[0]);
    expect(component.deleteTarget?.name).toBe('Delete Me');

    component.confirmDelete();

    const request = http.expectOne(
      (req) => req.url === '/api/admin/regional/locations/15' && req.params.get('regionId') === '7',
    );
    expect(request.request.method).toBe('DELETE');
    request.flush(null);

    expect(component.deleteTarget).toBeNull();
    expect(component.items.map((item) => item.name)).toEqual(['Keep Me']);
    expect(component.totalItems).toBe(1);
  });

  it('shows the backend delete error when related content blocks deletion', () => {
    fixture.detectChanges();
    flushRegions();
    flushLocations(7, locationPage([locationDto(15, 'Protected Location')], 1));

    component.promptDelete(component.items[0]);
    component.confirmDelete();

    const request = http.expectOne('/api/admin/regional/locations/15?regionId=7');
    request.flush(
      { message: 'This location cannot be deleted while it has related content.' },
      { status: 409, statusText: 'Conflict' },
    );

    expect(component.isDeleting).toBe(false);
    expect(component.errorMessage).toBe(
      'This location cannot be deleted while it has related content.',
    );
  });

  it('changing active region reloads the first page for the new region', () => {
    fixture.detectChanges();
    flushRegions([
      { id: 7, name: 'Backend Region', displayName: 'Backend Region' },
      { id: 8, name: 'Second Region', displayName: 'Second Region' },
    ]);
    flushLocations(7, locationPage([locationDto(1, 'Region 7 Location')], 1));

    component.onRegionSelected(8);
    flushFilterOptions(8);
    flushLocations(8, locationPage([locationDto(2, 'Region 8 Location', 8)], 1));

    expect(component.activeRegionId).toBe(8);
    expect(component.items.map((item) => item.name)).toEqual(['Region 8 Location']);
  });

  it('navigates edit through the canonical location edit route with active region context', () => {
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    fixture.detectChanges();
    flushRegions();
    flushLocations(7, locationPage([locationDto(21, 'Editable Location')], 1));

    component.editLocation(component.items[0]);

    expect(navigateSpy).toHaveBeenCalledWith(['/locations/edit/21'], {
      queryParams: { regionId: 7 },
      state: {
        location: expect.objectContaining({ id: '21', name: 'Editable Location' }),
        editMode: true,
        returnTo: '/locations',
        regionId: 7,
      },
    });
  });

  function flushRegions(
    regions = [{ id: 7, name: 'Backend Region', displayName: 'Backend Region' }],
  ): void {
    const request = http.expectOne('/api/admin/regional/regions');
    expect(request.request.method).toBe('GET');
    request.flush(regions);
    flushFilterOptions(regions[0]?.id ?? 7);
  }

  function flushFilterOptions(regionId: number): void {
    http
      .expectOne(
        (req) =>
          req.url === '/api/admin/regional/location-parent-destinations' &&
          req.params.get('regionId') === String(regionId),
      )
      .flush([
        {
          id: 3,
          name: 'Backend Destination',
          displayName: 'Backend Destination',
          status: 'Published',
        },
      ]);
    http
      .expectOne('/api/admin/regional/location-types')
      .flush([{ id: 2, name: 'District', description: 'District type' }]);
    http.expectOne('/api/admin/regional/location-tags').flush([]);
  }

  function flushLocations(
    regionId: number,
    response: RegionalLocationListResponse,
    extra?: (req: import('@angular/common/http').HttpRequest<unknown>) => boolean,
  ): void {
    const request = http.expectOne(
      (req) =>
        req.url === '/api/admin/regional/locations' &&
        req.params.get('regionId') === String(regionId) &&
        (!extra || extra(req)),
    );
    expect(request.request.method).toBe('GET');
    request.flush(response);
  }

  function normalizedText(): string {
    return (fixture.nativeElement.textContent as string).replace(/\s+/g, ' ').trim();
  }

  function locationPage(
    items: ReturnType<typeof locationDto>[],
    totalCount = items.length,
    skip = 0,
  ): RegionalLocationListResponse {
    return {
      items,
      totalCount,
      skip,
      take: 25,
    };
  }

  function makeLocations(count: number, start = 1): ReturnType<typeof locationDto>[] {
    return Array.from({ length: count }, (_, index) => {
      const id = start + index;
      return locationDto(id, `Backend Location ${id}`);
    });
  }

  function locationDto(id: number, name: string, regionId = 7) {
    return {
      id,
      name,
      summary: `${name} summary`,
      description: null,
      locationType: 'District',
      locationTypeId: 2,
      status: 'Published',
      regionId,
      regionDisplayName: regionId === 7 ? 'Backend Region' : 'Second Region',
      destinationId: 3,
      destinationName: regionId === 7 ? 'Backend Destination' : 'Second Destination',
      createdAt: '2026-05-10T10:00:00Z',
      lastUpdatedAt: '2026-05-12T10:00:00Z',
      imageUrl: null,
      detailRoute: `/locations/detail/${id}`,
      editRoute: `/locations/edit/${id}`,
    };
  }
});
