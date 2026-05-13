import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { vi } from 'vitest';

import { AllDestinationsComponent } from './all-destinations.component';
import { RegionalDestinationListResponse } from '../../../../../core/models/destination.model';

describe('AllDestinationsComponent', () => {
  let component: AllDestinationsComponent;
  let fixture: ComponentFixture<AllDestinationsComponent>;
  let http: HttpTestingController;

  beforeEach(async () => {
    sessionStorage.clear();

    await TestBed.configureTestingModule({
      imports: [AllDestinationsComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(AllDestinationsComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify({ ignoreCancelled: true });
    sessionStorage.clear();
    vi.useRealTimers();
  });

  it('loads first 25 destinations for the active assigned region', () => {
    fixture.detectChanges();
    flushRegions();
    flushDestinations(7, destinationPage(makeDestinations(25), 60), req =>
      req.params.get('skip') === '0' && req.params.get('take') === '25',
    );

    expect(component.activeRegionId).toBe(7);
    expect(component.items.length).toBe(25);
    expect(component.totalItems).toBe(60);
    expect(component.hasMore).toBe(true);
    expect(component.isLoading).toBe(false);
    expect(component.items.map(item => item.name)).not.toContain('Sumadija Heartland');
  });

  it('renders backend rows and footer without mock destination rows', () => {
    fixture.detectChanges();
    flushRegions();
    flushDestinations(7, destinationPage([destinationDto(1, 'Backend Destination')], 1));

    fixture.detectChanges();
    const text = normalizedText();

    expect(text).toContain('Backend Destination');
    expect(text).toContain('Showing 1 of 1 destination');
    expect(text).not.toContain('Kragujevac Urban Zone');
    expect(text).not.toContain('Topola & Oplenac Region');
  });

  it('show more appends the next backend page and updates the footer count', () => {
    fixture.detectChanges();
    flushRegions();
    flushDestinations(7, destinationPage(makeDestinations(25), 40));

    component.loadMore();
    flushDestinations(7, destinationPage(makeDestinations(15, 26), 40, 25), req =>
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
    flushDestinations(7, destinationPage(makeDestinations(25), 40));

    component.searchQuery = 'orchards';
    component.onSearchChange();
    vi.advanceTimersByTime(300);

    flushDestinations(7, destinationPage([destinationDto(7, 'Orchard Destination')], 1), req =>
      req.params.get('search') === 'orchards' && req.params.get('skip') === '0',
    );

    expect(component.items.map(item => item.name)).toEqual(['Orchard Destination']);
    expect(component.totalItems).toBe(1);
  });

  it('sort changes reset pagination and preserve the current search value', () => {
    fixture.detectChanges();
    flushRegions();
    flushDestinations(7, destinationPage(makeDestinations(25), 40));

    component.searchQuery = 'river';
    component.applySortOption({ label: 'Name Z-A', value: 'nameDesc' });

    flushDestinations(7, destinationPage([destinationDto(12, 'Zeta Destination')], 1), req =>
      req.params.get('sort') === 'nameDesc' &&
      req.params.get('search') === 'river' &&
      req.params.get('skip') === '0',
    );

    expect(component.currentSort).toBe('nameDesc');
    expect(component.items[0].name).toBe('Zeta Destination');
  });

  it('clears loading and shows an empty state for no destinations', () => {
    fixture.detectChanges();
    flushRegions();
    flushDestinations(7, destinationPage([], 0));

    expect(component.isLoading).toBe(false);
    expect(component.items).toEqual([]);
    expect(component.emptyMessage).toBe('No destinations found in this region.');
  });

  it('clears loading and shows a safe error state when the API fails', () => {
    fixture.detectChanges();
    flushRegions();

    const request = http.expectOne(req => req.url === '/api/admin/regional/destinations');
    request.flush({ message: 'Server error' }, { status: 500, statusText: 'Server Error' });

    expect(component.isLoading).toBe(false);
    expect(component.items).toEqual([]);
    expect(component.errorMessage).toBe('Unable to load destinations.');
  });

  it('opens delete confirmation and removes a row after successful delete', () => {
    fixture.detectChanges();
    flushRegions();
    flushDestinations(7, destinationPage([destinationDto(15, 'Delete Me'), destinationDto(16, 'Keep Me')], 2));

    component.promptDelete(component.items[0]);
    expect(component.deleteTarget?.name).toBe('Delete Me');

    component.confirmDelete();

    const request = http.expectOne(
      req =>
        req.url === '/api/admin/regional/destinations/15' &&
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
    flushDestinations(7, destinationPage([destinationDto(15, 'Protected Destination')], 1));

    component.promptDelete(component.items[0]);
    component.confirmDelete();

    const request = http.expectOne('/api/admin/regional/destinations/15?regionId=7');
    request.flush(
      { message: 'This destination cannot be deleted while it has related content.' },
      { status: 409, statusText: 'Conflict' },
    );

    expect(component.isDeleting).toBe(false);
    expect(component.errorMessage).toBe('This destination cannot be deleted while it has related content.');
  });

  it('changing active region reloads the first page for the new region', () => {
    fixture.detectChanges();
    flushRegions([
      { id: 7, name: 'Backend Region', displayName: 'Backend Region' },
      { id: 8, name: 'Second Region', displayName: 'Second Region' },
    ]);
    flushDestinations(7, destinationPage([destinationDto(1, 'Region 7 Destination')], 1));

    component.onRegionSelected(8);
    flushDestinations(8, destinationPage([destinationDto(2, 'Region 8 Destination', 8)], 1));

    expect(component.activeRegionId).toBe(8);
    expect(component.items.map(item => item.name)).toEqual(['Region 8 Destination']);
  });

  it('navigates edit through the existing destination create/edit flow with active region context', () => {
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    fixture.detectChanges();
    flushRegions();
    flushDestinations(7, destinationPage([destinationDto(21, 'Editable Destination')], 1));

    component.editDestination(component.items[0]);

    expect(navigateSpy).toHaveBeenCalledWith(['/destinations/new'], {
      queryParams: { regionId: 7 },
      state: {
        place: expect.objectContaining({ id: '21', name: 'Editable Destination' }),
        editMode: true,
        returnTo: '/destinations',
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
  }

  function flushDestinations(
    regionId: number,
    response: RegionalDestinationListResponse,
    extra?: (req: import('@angular/common/http').HttpRequest<unknown>) => boolean,
  ): void {
    const request = http.expectOne(
      req =>
        req.url === '/api/admin/regional/destinations' &&
        req.params.get('regionId') === String(regionId) &&
        (!extra || extra(req)),
    );
    expect(request.request.method).toBe('GET');
    request.flush(response);
  }

  function normalizedText(): string {
    return (fixture.nativeElement.textContent as string).replace(/\s+/g, ' ').trim();
  }

  function destinationPage(
    items: ReturnType<typeof destinationDto>[],
    totalCount = items.length,
    skip = 0,
  ): RegionalDestinationListResponse {
    return {
      items,
      totalCount,
      skip,
      take: 25,
    };
  }

  function makeDestinations(count: number, start = 1): ReturnType<typeof destinationDto>[] {
    return Array.from({ length: count }, (_, index) => {
      const id = start + index;
      return destinationDto(id, `Backend Destination ${id}`);
    });
  }

  function destinationDto(id: number, name: string, regionId = 7) {
    return {
      id,
      name,
      summary: `${name} summary`,
      description: `${name} backend description`,
      status: 'Published',
      regionId,
      regionDisplayName: regionId === 7 ? 'Backend Region' : 'Second Region',
      destinationTypeId: 3,
      destinationTypeName: 'Destination',
      createdAt: '2026-05-10T10:00:00Z',
      lastUpdatedAt: '2026-05-12T10:00:00Z',
      imageUrl: null,
      detailRoute: `/destinations/detail/${id}`,
      editRoute: '/destinations/new',
    };
  }
});
