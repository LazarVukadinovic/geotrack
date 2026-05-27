import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { ContentModeratorDashboardService } from './content-moderator-dashboard.service';

describe('ContentModeratorDashboardService', () => {
  let service: ContentModeratorDashboardService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(ContentModeratorDashboardService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('loads the content moderator dashboard from the backend endpoint', () => {
    let pendingReviewReportsCount = -1;

    service.loadDashboard().subscribe(dashboard => {
      pendingReviewReportsCount = dashboard.badges.pendingReviewReportsCount;
      expect(dashboard.currentUser.roleDisplayName).toBe('Content Moderator');
      expect(dashboard.stats.destinationsCount).toBe(12);
      expect(dashboard.quickLinks.destinationsReviewRoute).toBe('/content-moderator/destinations');
      expect(dashboard.quickLinks.pendingReportsRoute).toBe('/content-moderator/review-management?status=reported');
    });

    const request = http.expectOne('/api/admin/content-moderator/dashboard');
    expect(request.request.method).toBe('GET');
    request.flush({
      currentUser: {
        id: 48,
        displayName: 'Content Moderator',
        email: 'moderator@gmail.com',
        avatarUrl: null,
        roleDisplayName: 'Content Moderator',
      },
      stats: {
        destinationsCount: 12,
        locationsCount: 24,
        placesCount: 36,
        eventsCount: 48,
        suspendedDestinationsCount: 1,
        suspendedLocationsCount: 2,
        suspendedPlacesCount: 3,
        suspendedEventsCount: 4,
        deletedDestinationsCount: 0,
        deletedLocationsCount: 1,
        deletedPlacesCount: 2,
        deletedEventsCount: 3,
        pendingReviewReportsCount: 3,
        reportedReviewsCount: 2,
        activeReviewsCount: 20,
        resolvedReportsCount: 5,
        removedReviewsCount: 4,
      },
      quickLinks: {
        destinationsReviewRoute: '/content-moderator/destinations',
        locationsReviewRoute: '/content-moderator/locations',
        placesReviewRoute: '/content-moderator/places',
        eventsReviewRoute: '/content-moderator/events',
        reviewManagementRoute: '/content-moderator/review-management',
        pendingReportsRoute: '/content-moderator/review-management?status=reported',
      },
      badges: {
        pendingReviewReportsCount: 3,
      },
    });

    expect(pendingReviewReportsCount).toBe(3);
    expect(service.getSnapshot()?.stats.reportedReviewsCount).toBe(2);
  });

  it('normalizes missing dashboard values to safe moderator defaults', () => {
    service.loadDashboard().subscribe(dashboard => {
      expect(dashboard.currentUser.displayName).toBe('Content Moderator');
      expect(dashboard.stats.pendingReviewReportsCount).toBe(0);
      expect(dashboard.stats.destinationsCount).toBe(0);
      expect(dashboard.badges.pendingReviewReportsCount).toBe(0);
      expect(dashboard.quickLinks.destinationsReviewRoute).toBe('/content-moderator/destinations');
      expect(dashboard.quickLinks.reviewManagementRoute).toBe('/content-moderator/review-management');
    });

    http.expectOne('/api/admin/content-moderator/dashboard').flush({});
  });
});
