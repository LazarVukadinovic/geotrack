import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { LocationService } from './location.service';

const LOCATION_KEY = 'geotrack_location';
const DISCLOSURE_KEY = 'geotrack_approximate_location_disclosure_v1';

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

  it('requires disclosure acceptance before calling approximate location', async () => {
    setup();

    const fallback = service.ensureIpFallback();

    expect(service.approximateDisclosureVisible()).toBe(true);
    httpMock.expectNone('/api/locations/approximate');

    service.acceptApproximateFallback();
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
    expect(localStorage.getItem(LOCATION_KEY)).toBeNull();
    expect(localStorage.getItem(DISCLOSURE_KEY)).toBe('accepted');

    const stored = JSON.parse(sessionStorage.getItem(LOCATION_KEY) ?? '{}') as {
      location?: { source?: string; latitude?: number };
    };
    expect(stored.location?.source).toBe('approximate');
    expect(stored.location?.latitude).toBe(44.8125);
  });

  it('declining approximate fallback prevents repeated backend calls', async () => {
    setup();

    const firstFallback = service.ensureIpFallback();

    expect(service.approximateDisclosureVisible()).toBe(true);
    service.declineApproximateFallback();

    await expect(firstFallback).resolves.toBeNull();
    expect(localStorage.getItem(DISCLOSURE_KEY)).toBe('declined');
    httpMock.expectNone('/api/locations/approximate');

    await expect(service.ensureIpFallback()).resolves.toBeNull();
    expect(service.approximateDisclosureVisible()).toBe(false);
    httpMock.expectNone('/api/locations/approximate');
  });

  it('removes invalid session storage without throwing', () => {
    sessionStorage.setItem(LOCATION_KEY, '{bad json');

    setup();

    expect(service.location()).toBeNull();
    expect(sessionStorage.getItem(LOCATION_KEY)).toBeNull();
    httpMock.expectNone('/api/locations/approximate');
  });
});
