import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { NewPlaceComponent } from './add-place.component';
import { RegionalAssignedRegion } from '../../../../core/models/dashboard.model';

type RouteStub = {
  snapshot: {
    paramMap: ReturnType<typeof convertToParamMap>;
    queryParamMap: ReturnType<typeof convertToParamMap>;
  };
};

describe('NewPlaceComponent', () => {
  let component: NewPlaceComponent;
  let fixture: ComponentFixture<NewPlaceComponent>;
  let http: HttpTestingController;
  let route: RouteStub;
  let router: Router;

  beforeEach(async () => {
    sessionStorage.clear();
    route = {
      snapshot: {
        paramMap: convertToParamMap({}),
        queryParamMap: convertToParamMap({}),
      },
    };

    await TestBed.configureTestingModule({
      imports: [NewPlaceComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: route },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(NewPlaceComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => {
    http.verify();
    sessionStorage.clear();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('builds a seven-day midnight schedule when open 24 hours is enabled', () => {
    component.opsEnabled = true;
    component.open24Hours = true;

    const hours = component['buildOperatingHoursPayload']();

    expect(hours).toHaveLength(7);
    expect(hours?.every(hour =>
      hour.isOpen && hour.openTime === '00:00' && hour.closeTime === '00:00')).toBe(true);
  });

  it('reads map coordinates from query params and requests the active region viewport', () => {
    route.snapshot.queryParamMap = convertToParamMap({
      regionId: '17',
      latitude: '45.1234567',
      longitude: '16.7654321',
      source: 'map',
    });

    fixture.detectChanges();

    expect(component.pinConfirmed).toBe(true);
    expect(component.pinLat).toBe('45.123457');
    expect(component.pinLng).toBe('16.765432');
    expect(component.modalPinLat).toBe(45.1234567);
    expect(component.modalPinLng).toBe(16.7654321);

    flushReverseGeocode(45.1234567, 16.7654321);
    flushRegions([{ id: 17, name: 'North', displayName: 'North Region', territoryName: 'Testland' }]);
    flushParentDestinations(17);
    flushRegionViewport(17);
    flushPlaceTypes();
    flushPlaceTags();

    expect(component.activeRegionId).toBe(17);
    expect(component.streetAddress).toBe('Pinned address');
  });

  it('shows a message when create new location is chosen before selecting a destination', () => {
    fixture.detectChanges();
    flushRegions([{ id: 17, name: 'North', displayName: 'North Region', territoryName: 'Testland' }]);
    flushParentDestinations(17);
    flushRegionViewport(17);
    flushPlaceTypes();
    flushPlaceTags();

    component.onParentLocationSelection(component.createLocationOptionValue);

    expect(component.errorMessage).toBe('Please select a destination before creating a new location.');
  });

  it('navigates to add destination with the current place draft and coordinates', () => {
    const navigateSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    route.snapshot.queryParamMap = convertToParamMap({
      regionId: '17',
      latitude: '45.1234567',
      longitude: '16.7654321',
      source: 'map',
    });

    fixture.detectChanges();
    flushReverseGeocode(45.1234567, 16.7654321);
    flushRegions([{ id: 17, name: 'North', displayName: 'North Region', territoryName: 'Testland' }]);
    flushParentDestinations(17);
    flushRegionViewport(17);
    flushPlaceTypes();
    flushPlaceTags();

    component.placeName = 'Draft place';
    component.summary = 'Draft summary';
    component.parentDestinationId = 12;

    component.onParentDestinationSelection(component.createDestinationOptionValue);

    expect(navigateSpy).toHaveBeenCalledWith(['/destinations/new'], {
      queryParams: {
        source: 'add-place',
        returnFlow: 'create-place',
      },
      state: {
        regionId: 17,
        returnUrl: '/places/new',
        placeDraft: expect.objectContaining({
          activeRegionId: 17,
          placeName: 'Draft place',
          summary: 'Draft summary',
          parentDestinationId: 12,
          pinLat: '45.123457',
          pinLng: '16.765432',
        }),
        coordinates: {
          latitude: 45.123457,
          longitude: 16.765432,
        },
      },
    });
  });

  function flushRegions(regions: RegionalAssignedRegion[]): void {
    const request = http.expectOne('/api/admin/regional/regions');
    expect(request.request.method).toBe('GET');
    request.flush(regions);
  }

  function flushParentDestinations(regionId: number): void {
    const request = http.expectOne(req =>
      req.url === '/api/admin/regional/place-parent-destinations' &&
      req.params.get('regionId') === String(regionId),
    );
    expect(request.request.method).toBe('GET');
    request.flush([]);
  }

  function flushRegionViewport(regionId: number): void {
    const request = http.expectOne(req =>
      req.url === '/api/admin/regional/map/viewport' &&
      req.params.get('regionId') === String(regionId),
    );
    expect(request.request.method).toBe('GET');
    request.flush({
      centerLatitude: 45.2,
      centerLongitude: 16.3,
      zoom: 10,
      isEmpty: false,
    });
  }

  function flushPlaceTypes(): void {
    const request = http.expectOne('/api/admin/regional/place-types');
    expect(request.request.method).toBe('GET');
    request.flush([]);
  }

  function flushPlaceTags(): void {
    const request = http.expectOne('/api/admin/regional/place-tags');
    expect(request.request.method).toBe('GET');
    request.flush([]);
  }

  function flushReverseGeocode(latitude: number, longitude: number): void {
    const request = http.expectOne(req =>
      req.url === '/api/map/reverse' &&
      req.params.get('latitude') === String(latitude) &&
      req.params.get('longitude') === String(longitude),
    );
    expect(request.request.method).toBe('GET');
    request.flush({
      address: 'Pinned address',
      city: '',
      region: '',
      country: '',
      displayLabel: 'Pinned address',
    });
  }
});
