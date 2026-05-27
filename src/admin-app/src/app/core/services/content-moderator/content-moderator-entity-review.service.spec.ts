import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { ContentModeratorEntityReviewService } from './content-moderator-entity-review.service';

describe('ContentModeratorEntityReviewService', () => {
  let service: ContentModeratorEntityReviewService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(ContentModeratorEntityReviewService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('loads paginated entity review data with filters', () => {
    service.getPage$({
      type: 'destination',
      search: 'alps',
      status: 'active',
      sort: 'newest',
      skip: 25,
      take: 25,
    }).subscribe(page => {
      expect(page.items.length).toBe(1);
      expect(page.items[0].name).toBe('Alps');
      expect(page.items[0].publicDetailRoute).toBe('/destinations/7');
      expect(page.items[0].canOpenInUserApp).toBe(true);
      expect(page.nextSkip).toBe(50);
      expect(page.hasMore).toBe(true);
      expect(page.counts.activeCount).toBe(31);
    });

    const request = http.expectOne(req =>
      req.url === '/api/admin/content-moderator/entities' &&
      req.params.get('type') === 'destination' &&
      req.params.get('search') === 'alps' &&
      req.params.get('status') === 'active' &&
      req.params.get('sort') === 'newest' &&
      req.params.get('skip') === '25' &&
      req.params.get('take') === '25');

    expect(request.request.method).toBe('GET');
    request.flush({
      items: [{
        entityId: 7,
        entityType: 'destination',
        name: 'Alps',
        summary: null,
        descriptionPreview: 'Mountain destination',
        thumbnailUrl: null,
        status: 'Active',
        rawStatus: 'Published',
        isPublished: true,
        isSuspended: false,
        isDeleted: false,
        createdAt: '2026-05-27T09:00:00Z',
        updatedAt: null,
        publishedAt: '2026-05-27T09:00:00Z',
        regionId: 4,
        regionName: 'Central',
        territoryName: 'Serbia',
        destinationId: null,
        destinationName: null,
        locationId: null,
        locationName: null,
        placeId: null,
        placeName: null,
        typeName: 'Mountain',
        createdByUserId: null,
        startsAt: null,
        endsAt: null,
        publicDetailRoute: '/destinations/7',
        canOpenInUserApp: true,
        permissions: {
          canView: true,
          canSuspend: true,
          canUnsuspend: false,
          canDelete: true,
          canRestore: false,
        },
      }],
      totalCount: 52,
      skip: 25,
      take: 25,
      nextSkip: 50,
      hasMore: true,
      counts: {
        allCount: 52,
        activeCount: 31,
        suspendedCount: 20,
        deletedCount: 1,
      },
    });
  });

  it('calls moderation action endpoints and normalizes detail data', () => {
    service.suspend$('place', 42, { reason: 'Needs review' }).subscribe(detail => {
      expect(detail.entityId).toBe(42);
      expect(detail.status).toBe('Suspended');
      expect(detail.publicDetailRoute).toBeNull();
      expect(detail.canOpenInUserApp).toBe(false);
      expect(detail.permissions.canUnsuspend).toBe(true);
      expect(detail.metadata.contentCounts['events']).toBe(3);
    });

    const request = http.expectOne('/api/admin/content-moderator/entities/place/42/suspend');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ reason: 'Needs review' });
    request.flush({
      entityId: 42,
      entityType: 'place',
      name: 'Museum',
      summary: null,
      descriptionPreview: 'Preview',
      thumbnailUrl: null,
      status: 'Suspended',
      rawStatus: 'Draft',
      isPublished: false,
      isSuspended: true,
      isDeleted: false,
      createdAt: '2026-05-27T09:00:00Z',
      updatedAt: '2026-05-27T09:10:00Z',
      publishedAt: null,
      regionId: 4,
      regionName: 'Central',
      territoryName: 'Serbia',
      destinationId: 3,
      destinationName: 'City',
      locationId: 5,
      locationName: 'Old Town',
      placeId: null,
      placeName: null,
      typeName: 'Museum',
      createdByUserId: null,
      startsAt: null,
      endsAt: null,
      publicDetailRoute: null,
      canOpenInUserApp: false,
      permissions: {
        canView: true,
        canSuspend: false,
        canUnsuspend: true,
        canDelete: true,
        canRestore: false,
      },
      description: 'Full detail',
      images: [],
      tags: [],
      metadata: {
        latitude: 44.1,
        longitude: 20.2,
        contentCounts: { events: 3 },
      },
    });
  });
});
