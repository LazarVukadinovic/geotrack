import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { CollectionsService } from './collections';
import { AuthService } from '../../../core/services/auth.service';
import { AnalyticsService } from '../../../core/services/analytics.service';
import { FavoriteStateService } from '../../../core/services/favorite-state.service';
import { vi } from 'vitest';

describe('Collections', () => {
  let service: CollectionsService;
  let http: HttpTestingController;
  const isLoggedIn = signal(false);
  const analytics = {
    track: vi.fn(),
  };
  const favoriteState = {
    set: vi.fn(),
    setMany: vi.fn(),
    reconcileKnown: vi.fn(),
  };

  beforeEach(() => {
    isLoggedIn.set(false);
    vi.clearAllMocks();

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: AuthService,
          useValue: {
            isLoggedIn,
          },
        },
        { provide: AnalyticsService, useValue: analytics },
        { provide: FavoriteStateService, useValue: favoriteState },
      ],
    });

    service = TestBed.inject(CollectionsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('should keep loaded membership when a forced list refresh returns the same collection', () => {
    isLoggedIn.set(true);

    service.ensureLoaded(true);
    http.expectOne('/api/user/me/collections').flush({
      collections: [
        {
          id: 1,
          name: 'Weekend Trip',
          description: '',
          userId: 1,
          createdAt: '2026-04-29T00:00:00Z',
          updatedAt: null,
          itemsCount: 1,
          coverImages: ['/media/place.jpg'],
        },
      ],
    });

    service.loadDetails(1);
    http.expectOne('/api/user/me/collections/1').flush({
      id: 1,
      name: 'Weekend Trip',
      description: '',
      userId: 1,
      createdAt: '2026-04-29T00:00:00Z',
      updatedAt: null,
      items: [
        {
          id: 11,
          collectionId: 1,
          entityType: 'place',
          placeId: 42,
          name: 'Dubrovnik Old Town Walls',
          description: '',
          imageUrl: '/media/place.jpg',
          detailRoute: '/places/42',
        },
      ],
    });

    expect(service.collections()[0].places?.[0]?.entityId).toBe(42);

    service.ensureLoaded(true);
    http.expectOne('/api/user/me/collections').flush({
      collections: [
        {
          id: 1,
          name: 'Weekend Trip',
          description: '',
          userId: 1,
          createdAt: '2026-04-29T00:00:00Z',
          updatedAt: null,
          itemsCount: 1,
          coverImages: ['/media/place.jpg'],
        },
      ],
    });

    expect(service.collections()[0].places?.[0]?.entityId).toBe(42);
    expect(service.collections()[0].places?.[0]?.detailRoute).toBe('/places/42');
  });
});
