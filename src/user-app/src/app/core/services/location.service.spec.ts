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

  it('removes invalid session location payloads without restoring them', () => {
    sessionStorage.setItem(LOCATION_KEY, JSON.stringify({
      schemaVersion: 2,
      location: {
        latitude: 0,
        longitude: 0,
        city: 'Null Island',
        region: '',
        country: '',
        displayLabel: 'Null Island',
        source: 'gps',
        cachedAt: Date.now(),
      },
    }));

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

  it('requests precise GPS during background refresh when permission is already granted', async () => {
    mockGeolocationPermission('granted');
    const geolocation = mockGeolocationSuccess(44.0128, 20.9114);
    setup();

    service.tryBackgroundGpsRefresh();
    await vi.waitFor(() => expect(geolocation.getCurrentPosition).toHaveBeenCalled());

    expect(service.location()).toMatchObject({
      latitude: 44.0128,
      longitude: 20.9114,
      source: 'gps',
    });
    expect(localStorage.getItem(LOCATION_KEY)).toBeNull();

    const reverseReq = httpMock.expectOne(req => req.url === '/api/map/reverse');
    reverseReq.flush({
      city: 'Kragujevac',
      region: 'Sumadija',
      country: 'Serbia',
      displayLabel: 'Kragujevac, Serbia',
    });
  });

  it('refreshes a session precise location on forced startup when permission is already granted', async () => {
    sessionStorage.setItem(LOCATION_KEY, JSON.stringify({
      schemaVersion: 2,
      location: {
        latitude: 43.985,
        longitude: 20.89,
        city: 'Old Session City',
        region: '',
        country: 'Serbia',
        displayLabel: 'Old Session City, Serbia',
        source: 'gps',
        cachedAt: Date.now(),
      },
    }));
    mockGeolocationPermission('granted');
    const geolocation = mockGeolocationSuccess(44.0128, 20.9114);
    setup();

    expect(service.location()).toMatchObject({
      latitude: 43.985,
      longitude: 20.89,
      source: 'gps',
    });

    service.tryBackgroundGpsRefresh({ force: true });
    await vi.waitFor(() => expect(geolocation.getCurrentPosition).toHaveBeenCalled());

    expect(service.location()).toMatchObject({
      latitude: 44.0128,
      longitude: 20.9114,
      source: 'gps',
    });
    expect(readStoredLocation()).toMatchObject({
      latitude: 44.0128,
      longitude: 20.9114,
      source: 'gps',
    });
    httpMock.expectNone(req => req.url === '/api/map/reverse');
  });

  it('keeps fallback temporary when it resolves before precise GPS', async () => {
    const geolocation = mockGeolocationDeferred();
    setup();

    const fallback = service.ensureIpFallback();
    const precise = service.requestGps();

    const fallbackReq = httpMock.expectOne('/api/locations/approximate');
    fallbackReq.flush(approximateResponse());

    await expect(fallback).resolves.toMatchObject({ source: 'approximate' });
    expect(service.location()).toMatchObject({
      latitude: 44.8706,
      longitude: 20.6403,
      source: 'approximate',
    });

    geolocation.succeed(44.0128, 20.9114);
    const reverseReq = httpMock.expectOne(req => req.url === '/api/map/reverse');
    reverseReq.flush({
      city: 'Kragujevac',
      region: 'Sumadija',
      country: 'Serbia',
      displayLabel: 'Kragujevac, Serbia',
    });

    await precise;
    expect(service.location()).toMatchObject({
      latitude: 44.0128,
      longitude: 20.9114,
      city: 'Kragujevac',
      source: 'gps',
    });
    expect(readStoredLocation()?.source).toBe('gps');
  });

  it('ignores fallback when precise GPS resolves before the fallback response', async () => {
    const geolocation = mockGeolocationDeferred();
    setup();

    const fallback = service.ensureIpFallback();
    const precise = service.requestGps();

    geolocation.succeed(44.0128, 20.9114);
    const reverseReq = httpMock.expectOne(req => req.url === '/api/map/reverse');
    reverseReq.flush({
      city: 'Kragujevac',
      region: 'Sumadija',
      country: 'Serbia',
      displayLabel: 'Kragujevac, Serbia',
    });
    await precise;

    const fallbackReq = httpMock.expectOne('/api/locations/approximate');
    fallbackReq.flush(approximateResponse());
    await expect(fallback).resolves.toMatchObject({ source: 'approximate' });

    expect(service.location()).toMatchObject({
      latitude: 44.0128,
      longitude: 20.9114,
      source: 'gps',
    });
    expect(readStoredLocation()).toMatchObject({
      latitude: 44.0128,
      longitude: 20.9114,
      source: 'gps',
    });
  });

  it('replaces a stale session approximate location when new precise GPS arrives', async () => {
    sessionStorage.setItem(LOCATION_KEY, JSON.stringify({
      schemaVersion: 2,
      location: {
        ...approximateResponse(),
        source: 'approximate',
        cachedAt: Date.now(),
      },
    }));
    mockGeolocationPermission('granted');
    mockGeolocationSuccess(44.0128, 20.9114);
    setup();

    expect(service.location()).toMatchObject({ source: 'approximate' });

    const loc = await service.resolveLocationForProductFlow();

    expect(loc).toMatchObject({
      latitude: 44.0128,
      longitude: 20.9114,
      source: 'gps',
    });
    expect(service.location()).toMatchObject({ source: 'gps' });
    expect(readStoredLocation()?.source).toBe('gps');
    httpMock.expectNone(req => req.url === '/api/map/reverse');
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

function mockGeolocationSuccess(latitude: number, longitude: number): { getCurrentPosition: ReturnType<typeof vi.fn> } {
  const getCurrentPosition = vi.fn((success: PositionCallback) => {
    success({ coords: { latitude, longitude } } as GeolocationPosition);
  });
  Object.defineProperty(navigator, 'geolocation', {
    configurable: true,
    value: {
      getCurrentPosition,
      watchPosition: vi.fn(),
      clearWatch: vi.fn(),
    },
  });

  return { getCurrentPosition };
}

function mockGeolocationDeferred(): {
  succeed(latitude: number, longitude: number): void;
  fail(code: number): void;
} {
  let successCallback: PositionCallback | null = null;
  let errorCallback: PositionErrorCallback | undefined;

  Object.defineProperty(navigator, 'geolocation', {
    configurable: true,
    value: {
      getCurrentPosition: vi.fn((success: PositionCallback, error?: PositionErrorCallback) => {
        successCallback = success;
        errorCallback = error;
      }),
      watchPosition: vi.fn(),
      clearWatch: vi.fn(),
    },
  });

  return {
    succeed(latitude: number, longitude: number): void {
      successCallback?.({ coords: { latitude, longitude } } as GeolocationPosition);
    },
    fail(code: number): void {
      errorCallback?.({ code } as GeolocationPositionError);
    },
  };
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

function approximateResponse(): {
  latitude: number;
  longitude: number;
  city: string;
  region: string;
  country: string;
  displayLabel: string;
  source: string;
} {
  return {
    latitude: 44.8706,
    longitude: 20.6403,
    city: 'Pancevo',
    region: 'Vojvodina',
    country: 'Serbia',
    displayLabel: 'Pancevo, Serbia',
    source: 'ip',
  };
}

function readStoredLocation(): {
  latitude?: number;
  longitude?: number;
  source?: string;
} | null {
  const stored = JSON.parse(sessionStorage.getItem(LOCATION_KEY) ?? '{}') as {
    location?: { latitude?: number; longitude?: number; source?: string };
  };
  return stored.location ?? null;
}
