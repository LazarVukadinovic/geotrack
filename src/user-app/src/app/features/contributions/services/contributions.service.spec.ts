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
    expect(payload['amenities']).toEqual(['wifi']);
    expect(payload['bookingPartners']).toEqual([]);
    expect(((req.request.body as FormData).get('ownershipProofDocument') as File).name)
      .toBe('ownership-proof.pdf');

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
    expect(payload['amenities']).toEqual(['free-wifi']);
    expect(payload['bookingPartners']).toEqual([
      { id: 'booking', url: 'https://booking.example/villa' },
    ]);
    expect(((req.request.body as FormData).get('ownershipProofDocument') as File).name)
      .toBe('ownership-proof.pdf');

    req.flush({ contribution: makeApiContribution({ entityType: 'Place', placeType: 'Stay', stayType: 'Villa' }) });
    httpMock.expectOne('/api/contributions').flush({ contributions: [] });
  });

  it('includes ownership proof for Restaurant contributions without stay-only fields', () => {
    const draft = makePlaceDraft();
    draft.basics = {
      ...draft.basics!,
      placeType: 'Restaurant',
      ownershipProofFile: makePdfFile('restaurant-proof.pdf'),
    };

    service.createPlace(draft).subscribe();

    const req = httpMock.expectOne('/api/contributions/places');
    const body = req.request.body as FormData;
    const payload = JSON.parse(body.get('payload') as string) as Record<string, unknown>;

    expect(payload['placeType']).toBe('Restaurant');
    expect(payload['stayType']).toBeUndefined();
    expect((body.get('ownershipProofDocument') as File).name).toBe('restaurant-proof.pdf');

    req.flush({ contribution: makeApiContribution({ entityType: 'Place', placeType: 'Restaurant', stayType: null }) });
    httpMock.expectOne('/api/contributions').flush({ contributions: [] });
  });

  it('does not append stale ownership proof for unrelated place types', () => {
    const draft = makePlaceDraft();
    draft.basics = {
      ...draft.basics!,
      placeType: 'Landmark',
      ownershipProofFile: null,
    };

    service.createPlace(draft).subscribe();

    const req = httpMock.expectOne('/api/contributions/places');
    const body = req.request.body as FormData;
    const payload = JSON.parse(body.get('payload') as string) as Record<string, unknown>;

    expect(payload['placeType']).toBe('Landmark');
    expect(body.has('ownershipProofDocument')).toBe(false);

    req.flush({ contribution: makeApiContribution({ entityType: 'Place', placeType: 'Landmark', stayType: null }) });
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

  it('normalizes backend amenity values to frontend option ids for edit drafts', () => {
    let actual: ContributionDetail | undefined;

    service.getDetail(7).subscribe(detail => {
      actual = detail;
    });

    const req = httpMock.expectOne('/api/contributions/7');
    req.flush(makeApiContribution({
      id: 7,
      entityType: 'Place',
      placeType: 'Stay',
      stayType: 'Villa',
      amenities: [
        'free-wifi',
        'Breakfast Included',
        'On-site Dining',
        'air-conditioning',
        'Fitness Center',
        'Swimming Pool',
        'WIFI',
        'sea-view',
      ],
      stayAmenities: ['free-wifi'],
    }));

    expect(actual?.amenities).toEqual(['wifi', 'breakfast', 'dining', 'ac', 'gym', 'pool', 'sea-view']);
    expect(service.detailToDraft(actual!).features?.amenities)
      .toEqual(['wifi', 'breakfast', 'dining', 'ac', 'gym', 'pool', 'sea-view']);
  });

  it('sends normalized backend amenity values in update payload without dropping unknown amenities', () => {
    const draft = makeStayDraft();
    draft.features = {
      tags: ['Coastal'],
      amenities: ['free-wifi', 'wifi', 'Breakfast Included', 'On-site Dining', 'sea-view', 'A/C'],
    };

    service.updatePlace(42, draft).subscribe();

    const req = httpMock.expectOne('/api/contributions/places/42');
    const payload = JSON.parse((req.request.body as FormData).get('payload') as string) as Record<string, unknown>;

    expect(payload['amenities']).toEqual([
      'free-wifi',
      'breakfast-included',
      'restaurant',
      'sea-view',
      'air-conditioning',
    ]);

    req.flush({ contribution: makeApiContribution({ entityType: 'Place', placeType: 'Stay', stayType: 'Villa' }) });
    httpMock.expectOne('/api/contributions').flush({ contributions: [] });
  });

  it('maps ownership contribution images amenities and characteristics into the edit draft', () => {
    let actual: ContributionDetail | undefined;

    service.getDetail(7).subscribe(detail => {
      actual = detail;
    });

    const req = httpMock.expectOne('/api/contributions/7');
    req.flush(makeApiContribution({
      id: 7,
      type: 'ClaimOwnership',
      placeType: 'Stay',
      stayType: 'Villa',
      cardImageUrl: null,
      coverImageUrl: null,
      galleryImageUrls: [],
      tags: ['Family Friendly', 'Historic'],
      amenities: ['Outdoor Seating'],
      stayAmenities: ['free-wifi', 'spa'],
      images: [
        { url: '/media/test/cover.svg', imageRole: 'cover', sortOrder: 0, imagePosition: '45% 40%', imageSize: 'cover' },
        { url: '/media/test/card.webp', imageRole: 'card', sortOrder: 1, imagePosition: '50% 45%', imageSize: 'cover' },
        { url: '/media/test/gallery.webp', imageRole: 'gallery', sortOrder: 2 },
      ],
    }));

    expect(actual?.isOwnership).toBe(true);
    expect(actual?.coverImageUrl).toBe('/media/test/cover.svg');
    expect(actual?.cardImageUrl).toBe('/media/test/card.webp');
    expect(actual?.galleryImageUrls).toEqual(['/media/test/gallery.webp']);
    expect(actual?.tags).toEqual(['Family Friendly', 'Historic']);
    expect(actual?.amenities).toEqual(['outdoor', 'wifi', 'spa']);

    const draft = service.detailToDraft(actual!);
    expect(draft.basics?.imageUrl).toBe('/media/test/cover.svg');
    expect(draft.basics?.cardImageUrl).toBe('/media/test/card.webp');
    expect(draft.basics?.galleryImageUrls).toEqual(['/media/test/gallery.webp']);
    expect(draft.features?.tags).toEqual(['Family Friendly', 'Historic']);
    expect(draft.features?.amenities).toEqual(['outdoor', 'wifi', 'spa']);
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
      placeTypeId: null,
      imageUrl: '',
      cardImageUrl: '',
      galleryImageUrls: [],
      coverImageFile: null,
      ownershipProofFile: makePdfFile(),
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

function makePdfFile(name = 'ownership-proof.pdf'): File {
  return new File(['%PDF-1.4\n% test proof\n'], name, { type: 'application/pdf' });
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
