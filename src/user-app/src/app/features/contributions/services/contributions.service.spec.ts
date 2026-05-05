import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AnalyticsService } from '../../../core/services/analytics.service';
import { ContributionsService } from './contributions.service';
import type { ContributionDraftData, ContributionDetail } from './contributions.service';

describe('ContributionsService', () => {
  let service: ContributionsService;
  let httpMock: HttpTestingController;
  let analytics: { track: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    analytics = { track: vi.fn() };

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AnalyticsService, useValue: analytics },
      ],
    });

    service = TestBed.inject(ContributionsService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    TestBed.resetTestingModule();
  });

  it('creates place contributions through the canonical places endpoint without stale stay-only fields', () => {
    let actual: ContributionDetail | undefined;

    service.createPlace(makePlaceDraft()).subscribe(detail => {
      actual = detail;
    });

    const req = httpMock.expectOne('/api/contributions/places');
    expect(req.request.method).toBe('POST');
    expect(req.request.body instanceof FormData).toBe(true);

    const payload = JSON.parse((req.request.body as FormData).get('payload') as string) as Record<string, unknown>;
    expect(payload['type']).toBe('CreatePlace');
    expect(payload['placeType']).toBe('Cafe');
    expect(payload['entityType']).toBeUndefined();
    expect(payload['stayType']).toBeUndefined();
    expect(payload['bookingPartners']).toEqual([]);

    req.flush({ contribution: makeApiContribution({ entityType: 'Place', placeType: 'Cafe', stayType: null }) });

    const reloadReq = httpMock.expectOne('/api/contributions');
    reloadReq.flush({ contributions: [] });

    expect(actual?.entityType).toBe('Place');
    expect(actual?.placeType).toBe('Cafe');
    expect(actual?.stayType).toBe('');
    expect(analytics.track).toHaveBeenCalledWith('contribution_created', { entityId: 42 });
  });

  it('keeps accommodation fields only when the selected place type is Stay', () => {
    service.updatePlace(42, makeStayDraft()).subscribe();

    const req = httpMock.expectOne('/api/contributions/places/42');
    expect(req.request.method).toBe('PUT');

    const payload = JSON.parse((req.request.body as FormData).get('payload') as string) as Record<string, unknown>;
    expect(payload['placeType']).toBe('Stay');
    expect(payload['stayType']).toBe('Villa');
    expect(payload['bookingPartners']).toEqual([
      { id: 'booking', url: 'https://booking.example/villa' },
    ]);

    req.flush({ contribution: makeApiContribution({ entityType: 'Place', placeType: 'Stay', stayType: 'Villa' }) });
    httpMock.expectOne('/api/contributions').flush({ contributions: [] });
  });

  it('maps legacy Stay contribution entity values to the current Place contribution entity type', () => {
    let actual: ContributionDetail | undefined;

    service.getDetail(7).subscribe(detail => {
      actual = detail;
    });

    const req = httpMock.expectOne('/api/contributions/7');
    req.flush(makeApiContribution({ id: 7, entityType: 'Stay', placeType: 'Stay', stayType: 'Villa' }));

    expect(actual?.entityType).toBe('Place');
    expect(actual?.placeType).toBe('Stay');
    expect(actual?.stayType).toBe('Villa');
  });
});

function makePlaceDraft(): ContributionDraftData {
  return {
    basics: {
      name: 'Canonical Cafe',
      placeType: 'Cafe',
      stayType: 'Hotel',
      address: '10 Market Lane',
      city: 'Dubrovnik',
      region: 'Dalmatia',
      country: 'Croatia',
      imageUrl: '',
      coverImageFile: null,
      lat: 42.6413,
      lng: 18.1089,
      imgBgPosition: '50% 50%',
      imgBgSize: 'cover',
    },
    features: { tags: ['Coffee'], amenities: ['wifi'] },
    details: {
      description: 'A place contribution.',
      phone: '+385111222',
      contactEmail: 'hello@example.com',
      website: 'https://example.com',
      bookingPartners: [{ id: 'booking', url: 'https://booking.example/ignored' }],
    },
  };
}

function makeStayDraft(): ContributionDraftData {
  return {
    ...makePlaceDraft(),
    basics: {
      ...makePlaceDraft().basics!,
      placeType: 'Stay',
      stayType: 'Villa',
    },
    details: {
      ...makePlaceDraft().details!,
      bookingPartners: [{ id: 'booking', url: 'https://booking.example/villa' }],
    },
  };
}

function makeApiContribution(overrides: Partial<Record<string, unknown>> = {}): Record<string, unknown> {
  return {
    id: 42,
    entityType: 'Place',
    name: 'Canonical Cafe',
    placeType: 'Cafe',
    stayType: null,
    location: 'Dubrovnik',
    coverImageUrl: null,
    status: 'PendingReview',
    submittedAt: '2026-05-03T10:00:00Z',
    updatedAt: null,
    publishedPlaceId: null,
    address: '10 Market Lane',
    city: 'Dubrovnik',
    region: 'Dalmatia',
    country: 'Croatia',
    latitude: 42.6413,
    longitude: 18.1089,
    imagePosition: '50% 50%',
    imageSize: 'cover',
    description: 'A place contribution.',
    phone: '+385111222',
    contactEmail: 'hello@example.com',
    website: 'https://example.com',
    tags: ['Coffee'],
    amenities: ['wifi'],
    bookingPartners: [],
    ...overrides,
  };
}
