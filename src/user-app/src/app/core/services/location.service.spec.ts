import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { LocationService } from './location.service';

const LOCATION_KEY = 'geotrack_location';
const OBSOLETE_DISCLOSURE_KEY = 'geotrack_approximate_location_disclosure_v1';
const ORIGINAL_PERMISSIONS_DESCRIPTOR = Object.getOwnPropertyDescriptor(navigator, 'permissions');
const ORIGINAL_GEOLOCATION_DESCRIPTOR = Object.getOwnPropertyDescriptor(navigator, 'geolocation');

describe('LocationService', () => {
  let service: LocationService;
  let httpMock: HttpTestingController;

  const setup = () => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });

    service = TestBed.inject(LocationService);
    httpMock = TestBed.inject(HttpTestingController);
  };

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
  });

  afterEach(() => {
    try {
      httpMock?.verify();
    } finally {
      restoreNavigatorProperty('permissions', ORIGINAL_PERMISSIONS_DESCRIPTOR);
      restoreNavigatorProperty('geolocation', ORIGINAL_GEOLOCATION_DESCRIPTOR);
      vi.restoreAllMocks();
      localStorage.clear();
      sessionStorage.clear();
      TestBed.resetTestingModule();
    }
  });

  it('does not call approximate location during service construction', () => {
    setup();

    httpMock.expectNone('/api/locations/approximate');
    expect(service.location()).toBeNull();
    expect(service.loading()).toBe(false);
  });

  it('clears legacy localStorage location data during migration', () => {
    localStorage.setItem(LOCATION_KEY, JSON.stringify({
      latitude: 44.8125,
      longitude: 20.4612,
      city: 'Belgrade',
      region: 'Central Serbia',
      country: 'Serbia',
      displayLabel: 'Belgrade, Serbia',
      source: 'gps',
      cachedAt: Date.now(),
    }));

    setup();

    expect(localStorage.getItem(LOCATION_KEY)).toBeNull();
    expect(service.location()).toBeNull();
    httpMock.expectNone('/api/locations/approximate');
  });

  it('removes obsolete approximate disclosure state during migration', () => {
    localStorage.setItem(OBSOLETE_DISCLOSURE_KEY, 'declined');

    setup();

    expect(localStorage.getItem(OBSOLETE_DISCLOSURE_KEY)).toBeNull();
    httpMock.expectNone('/api/locations/approximate');
  });

  it('stores precise GPS coordinates in sessionStorage only', () => {
    setup();

    service.setGpsLocation(44.8125, 20.4612);

    expect(localStorage.getItem(LOCATION_KEY)).toBeNull();
    const stored = JSON.parse(sessionStorage.getItem(LOCATION_KEY) ?? '{}') as {
      schemaVersion?: number;
      location?: { latitude?: number; longitude?: number; source?: string };
    };
    expect(stored.schemaVersion).toBe(2);
    expect(stored.location?.latitude).toBe(44.8125);
    expect(stored.location?.longitude).toBe(20.4612);
    expect(stored.location?.source).toBe('gps');

    const reverseReq = httpMock.expectOne(req => req.url === '/api/map/reverse');
    reverseReq.flush({
      address: 'Belgrade',
      city: 'Belgrade',
      region: 'Central Serbia',
      country: 'Serbia',
      displayLabel: 'Belgrade, Serbia',
    });
  });

  it('calls approximate fallback automatically without disclosure storage', async () => {
    setup();

    const fallback = service.ensureIpFallback();

    const req = httpMock.expectOne('/api/locations/approximate');
    req.flush({
      latitude: 44.8125,
      longitude: 20.4612,
      city: 'Belgrade',
      region: 'Central Serbia',
      country: 'Serbia',
      displayLabel: 'Belgrade, Serbia',
      source: 'ip',
    });

    const loc = await fallback;
    expect(loc?.source).toBe('approximate');
    expect(localStorage.getItem(LOCATION_KEY)).toBeNull();
    expect(localStorage.getItem(OBSOLETE_DISCLOSURE_KEY)).toBeNull();

    const stored = JSON.parse(sessionStorage.getItem(LOCATION_KEY) ?? '{}') as {
      location?: { source?: string; latitude?: number };
    };
    expect(stored.location?.source).toBe('approximate');
    expect(stored.location?.latitude).toBe(44.8125);
  });

  it('ignores old declined disclosure state and still calls approximate fallback', async () => {
    localStorage.setItem(OBSOLETE_DISCLOSURE_KEY, 'declined');
    setup();

    const fallback = service.ensureIpFallback();

    expect(localStorage.getItem(OBSOLETE_DISCLOSURE_KEY)).toBeNull();
    const req = httpMock.expectOne('/api/locations/approximate');
    req.flush({
      latitude: 44.8125,
      longitude: 20.4612,
      city: 'Belgrade',
      region: 'Central Serbia',
      country: 'Serbia',
      displayLabel: 'Belgrade, Serbia',
      source: 'ip',
    });

    await expect(fallback).resolves.toMatchObject({ source: 'approximate' });
  });

  it('shares one approximate fallback request and resolves safely on failure', async () => {
    setup();

    const firstFallback = service.ensureIpFallback();
    const secondFallback = service.ensureIpFallback();

    const req = httpMock.expectOne('/api/locations/approximate');
    req.flush({}, { status: 503, statusText: 'Unavailable' });

    await expect(firstFallback).resolves.toBeNull();
    await expect(secondFallback).resolves.toBeNull();
    expect(service.loading()).toBe(false);
    expect(service.error()).toBe('Unable to determine location');
  });

  it('removes invalid session storage without throwing', () => {
    sessionStorage.setItem(LOCATION_KEY, '{bad json');

    setup();

    expect(service.location()).toBeNull();
    expect(sessionStorage.getItem(LOCATION_KEY)).toBeNull();
    httpMock.expectNone('/api/locations/approximate');
  });

  it('uses precise GPS when browser permission is granted', async () => {
    mockGeolocationPermission('granted');
    mockGeolocationSuccess(44.8125, 20.4612);
    setup();

    const loc = await service.resolveLocationForProductFlow();

    expect(loc?.source).toBe('gps');
    expect(loc?.latitude).toBe(44.8125);
    expect(localStorage.getItem(LOCATION_KEY)).toBeNull();

    const reverseReq = httpMock.expectOne(req => req.url === '/api/map/reverse');
    reverseReq.flush({
      address: 'Belgrade',
      city: 'Belgrade',
      region: 'Central Serbia',
      country: 'Serbia',
      displayLabel: 'Belgrade, Serbia',
    });
  });

  it('falls back to approximate location automatically when GPS is denied', async () => {
    mockGeolocationPermission('prompt');
    mockGeolocationFailure(1);
    setup();

    const fallback = service.resolveLocationForProductFlow();
    await new Promise(resolve => setTimeout(resolve, 0));

    const req = httpMock.expectOne('/api/locations/approximate');
    req.flush({
      latitude: 44.8125,
      longitude: 20.4612,
      city: 'Belgrade',
      region: 'Central Serbia',
      country: 'Serbia',
      displayLabel: 'Belgrade, Serbia',
      source: 'ip',
    });

    const loc = await fallback;
    expect(loc?.source).toBe('approximate');
    expect(localStorage.getItem(OBSOLETE_DISCLOSURE_KEY)).toBeNull();
  });

  it('falls back to approximate location automatically when geolocation is unavailable', async () => {
    mockGeolocationPermission('granted');
    mockGeolocationUnavailable();
    setup();

    const fallback = service.resolveLocationForProductFlow();
    await new Promise(resolve => setTimeout(resolve, 0));

    const req = httpMock.expectOne('/api/locations/approximate');
    req.flush({
      latitude: 44.8125,
      longitude: 20.4612,
      city: 'Belgrade',
      region: 'Central Serbia',
      country: 'Serbia',
      displayLabel: 'Belgrade, Serbia',
      source: 'ip',
    });

    await expect(fallback).resolves.toMatchObject({ source: 'approximate' });
  });
});

function mockGeolocationPermission(state: PermissionState): void {
  Object.defineProperty(navigator, 'permissions', {
    configurable: true,
    value: {
      query: vi.fn().mockResolvedValue({ state }),
    },
  });
}

function mockGeolocationSuccess(latitude: number, longitude: number): void {
  Object.defineProperty(navigator, 'geolocation', {
    configurable: true,
    value: {
      getCurrentPosition: vi.fn((success: PositionCallback) => {
        success({ coords: { latitude, longitude } } as GeolocationPosition);
      }),
      watchPosition: vi.fn(),
      clearWatch: vi.fn(),
    },
  });
}

function mockGeolocationFailure(code: number): void {
  Object.defineProperty(navigator, 'geolocation', {
    configurable: true,
    value: {
      getCurrentPosition: vi.fn((_success: PositionCallback, error?: PositionErrorCallback) => {
        error?.({ code } as GeolocationPositionError);
      }),
      watchPosition: vi.fn(),
      clearWatch: vi.fn(),
    },
  });
}

function mockGeolocationUnavailable(): void {
  Object.defineProperty(navigator, 'geolocation', {
    configurable: true,
    value: undefined,
  });
}

function restoreNavigatorProperty(
  key: 'permissions' | 'geolocation',
  descriptor: PropertyDescriptor | undefined,
): void {
  if (descriptor) {
    Object.defineProperty(navigator, key, descriptor);
    return;
  }

  delete (navigator as unknown as Record<string, unknown>)[key];
}
