import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { FavoriteStateService } from '../../../core/services/favorite-state.service';
import { LocationService } from '../../../core/services/location.service';
import { AnalyticsService } from '../../../core/services/analytics.service';
import { SaveToCollectionService } from '../../../core/services/save-to-collection.service';
import { DestinationService } from '../../../core/services/destination.service';
import { PlaceDetailService } from './place-detail.service';

describe('PlaceDetailService image mapping', () => {
  let service: PlaceDetailService;
  let httpMock: HttpTestingController;
  let analytics: { track: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    analytics = { track: vi.fn() };

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: FavoriteStateService,
          useValue: { resolve: (_entityType: string, _id: string, fallback: boolean) => fallback },
        },
        {
          provide: LocationService,
          useValue: { location: () => null },
        },
        {
          provide: DestinationService,
          useValue: { mode: () => 'destination', id: () => 123 },
        },
        {
          provide: AnalyticsService,
          useValue: analytics,
        },
        {
          provide: SaveToCollectionService,
          useValue: { open: vi.fn() },
        },
      ],
    });

    service = TestBed.inject(PlaceDetailService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    TestBed.resetTestingModule();
  });

  it('orders backend images and derives heroImage from the main image', () => {
    service.loadPlaceDetail('42');

    const req = httpMock.expectOne('/api/places/42?distanceOrigin=destination-centre');
    expect(req.request.method).toBe('GET');
    req.flush({
      id: 42,
      title: 'Banje Beach',
      destinationId: 99,
      location: 'Dubrovnik',
      heroImage: '/legacy-main.svg',
      images: [
        { id: 2, url: '/second.svg', sortOrder: 1, isMain: false },
        { id: 1, url: '/main.svg', altText: 'Main photo', caption: 'Hero', sortOrder: 0, isMain: true },
      ],
      tags: [],
      reviews: [],
      similar: [],
      isSaved: false,
      contentType: 'place',
    });

    const detail = service.getPlaceDetailPageData('42');
    expect(detail?.heroImage).toBe('/main.svg');
    expect(detail?.images.map((image) => image.url)).toEqual(['/second.svg']);
    expect(detail?.images[0]).toMatchObject({
      id: '2',
      isMain: false,
      sortOrder: 1,
    });
    expect(analytics.track).toHaveBeenCalledWith('detail_view', { entityType: 'place', entityId: 42, destinationId: 99 });
  });

  it('keeps heroImage fallback and empty image arrays when images are missing', () => {
    service.loadPlaceDetail('7');

    const req = httpMock.expectOne('/api/places/7?distanceOrigin=destination-centre');
    req.flush({
      id: 7,
      title: 'Fallback Place',
      heroImage: '/legacy-main.svg',
      images: null,
      tags: [],
      reviews: [],
      similar: [],
      isSaved: false,
      contentType: 'place',
    });

    const detail = service.getPlaceDetailPageData('7');
    expect(detail?.heroImage).toBe('/legacy-main.svg');
    expect(detail?.images).toEqual([]);
  });

  it('normalizes missing similar places to an empty array', () => {
    service.loadPlaceDetail('9');

    const req = httpMock.expectOne('/api/places/9?distanceOrigin=destination-centre');
    req.flush({
      id: 9,
      title: 'Quiet Place',
      tags: [],
      reviews: [],
      isSaved: false,
      contentType: 'place',
    });

    const detail = service.getPlaceDetailPageData('9');
    expect(detail?.similar).toEqual([]);
  });
});
