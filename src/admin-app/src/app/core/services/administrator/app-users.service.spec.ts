import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { AppUsersService } from './app-users.service';

describe('AppUsersService', () => {
  let service: AppUsersService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AppUsersService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('loads administrator users from the backend with pagination filters', () => {
    let total = 0;
    let firstEmail = '';

    service.getPage$({
      tab: 'app-users',
      search: 'ada',
      status: 'active',
      sort: 'newest',
      skip: 0,
      take: 25,
    }).subscribe(page => {
      total = page.totalCount;
      firstEmail = page.items[0].email;
      expect(page.items[0].reviewCount).toBe(3);
      expect(page.items[0].reportedReviewCount).toBe(1);
      expect(page.items[0].avatarInitials).toBe('AL');
    });

    const request = http.expectOne(req =>
      req.url === '/api/admin/users' &&
      req.params.get('tab') === 'app-users' &&
      req.params.get('search') === 'ada' &&
      req.params.get('status') === 'active' &&
      req.params.get('sort') === 'newest' &&
      req.params.get('skip') === '0' &&
      req.params.get('take') === '25');
    expect(request.request.method).toBe('GET');
    request.flush(pageResponse());

    expect(total).toBe(1);
    expect(firstEmail).toBe('ada@example.com');
  });

  it('calls the real admin action endpoints', () => {
    service.suspend$(42).subscribe(user => expect(user.status).toBe('Suspended'));
    const suspend = http.expectOne('/api/admin/users/42/suspend');
    expect(suspend.request.method).toBe('POST');
    suspend.flush({ ...userResponse(), status: 'Suspended' });

    service.unsuspend$('42').subscribe(user => expect(user.status).toBe('Active'));
    const unsuspend = http.expectOne('/api/admin/users/42/unsuspend');
    expect(unsuspend.request.method).toBe('POST');
    unsuspend.flush(userResponse());

    service.assignTerritoryManager$(42, { territoryId: 7 }).subscribe(user => {
      expect(user.roles).toContain('TerritoryManager');
      expect(user.assignedTerritoryCount).toBe(1);
    });
    const assign = http.expectOne('/api/admin/users/42/assign-territory-manager');
    expect(assign.request.method).toBe('POST');
    expect(assign.request.body).toEqual({ territoryId: 7 });
    assign.flush({
      ...userResponse(),
      roles: ['RegisteredUser', 'TerritoryManager'],
      assignedTerritoryCount: 1,
      assignedTerritories: [{ territoryId: 7, territoryName: 'Serbia', displayName: 'Serbia', countryCode: 'RS' }],
    });

    service.delete$(42).subscribe();
    const remove = http.expectOne('/api/admin/users/42');
    expect(remove.request.method).toBe('DELETE');
    remove.flush(null);
  });

  it('loads and normalizes the administrator user preview endpoint', () => {
    service.getPreview$(42).subscribe(preview => {
      expect(preview.user.email).toBe('ada@example.com');
      expect(preview.user.avatarInitials).toBe('AL');
      expect(preview.user.status).toBe('Active');
      expect(preview.roles[0].displayName).toBe('Registered User');
      expect(preview.accountSummary.reviewsCount).toBe(3);
      expect(preview.reportsMade.items.length).toBe(1);
      expect(preview.reportsAgainstUser.items[0].contentExcerpt).toContain('Offensive snapshot');
      expect(preview.recentReviews.totalCount).toBe(1);
      expect(preview.moderationHistory.items[0].eventType).toBe('Report accepted');
      expect(preview.permissions.canSuspend).toBe(true);
    });

    const request = http.expectOne('/api/admin/users/42/preview');
    expect(request.request.method).toBe('GET');
    request.flush(previewResponse());
  });

  function pageResponse() {
    return {
      items: [userResponse()],
      totalCount: 1,
      skip: 0,
      take: 25,
      nextSkip: null,
      hasMore: false,
      counts: {
        appUsersCount: 1,
        territoryManagersCount: 0,
        activeCount: 1,
        suspendedCount: 0,
        deletedCount: 0,
      },
    };
  }

  function userResponse() {
    return {
      userId: 42,
      id: '42',
      displayName: 'Ada Lovelace',
      firstName: 'Ada',
      lastName: 'Lovelace',
      email: 'ada@example.com',
      avatarUrl: null,
      initials: 'AL',
      status: 'Active',
      roles: ['RegisteredUser'],
      isAdmin: false,
      isCurrentUser: false,
      reviewsCount: 3,
      reportedCount: 1,
      joinedAt: '2026-01-01T00:00:00Z',
      lastActiveAt: null,
      assignedTerritories: [],
      assignedTerritoryCount: 0,
      canAssignTerritoryManager: true,
      canSuspend: true,
      canUnsuspend: false,
      canDelete: true,
    };
  }

  function previewResponse() {
    return {
      user: {
        ...userResponse(),
        accountType: 'App User',
        hasElevatedAccess: false,
        lastLoginAt: null,
        assignedRegions: [],
      },
      roles: [{
        roleName: 'RegisteredUser',
        displayName: 'Registered User',
        assignedScope: 'Default app access',
        assignedAt: null,
        scopes: [],
      }],
      accountSummary: {
        reviewsCount: 3,
        reportsMadeCount: 1,
        reportedContentCount: 1,
        pendingReportsAgainstUserCount: 1,
        acceptedReportsAgainstUserCount: 0,
        rejectedReportsAgainstUserCount: 0,
        contributionsCount: 2,
        pendingContributionsCount: 0,
        favoritesCount: 1,
        collectionsCount: 1,
        savedItemsCount: 2,
        ownedPlacesCount: 0,
        activityEventsCount: 5,
        knownSuspensionCount: 0,
        lastKnownActivityAt: '2026-01-02T00:00:00Z',
        riskLevel: 'Medium',
        riskSummary: '1 pending report needs review.',
      },
      reportsMade: {
        items: [{
          reportId: 11,
          contentType: 'Review',
          targetType: 'Place',
          reviewId: 101,
          entityId: 7,
          entityName: 'Kalemegdan',
          regionName: 'Belgrade',
          reason: 'Spam',
          status: 'Pending',
          decision: null,
          actionTaken: 'Pending review',
          contentExcerpt: 'Reported snapshot',
          reporterUserId: 42,
          reporterName: 'Ada Lovelace',
          reviewAuthorUserId: 43,
          reviewAuthorName: 'Grace Hopper',
          createdAt: '2026-01-02T00:00:00Z',
          moderationDecisionDate: null,
          detailRoute: null,
        }],
        totalCount: 1,
        skip: 0,
        take: 10,
        hasMore: false,
      },
      reportsAgainstUser: {
        items: [{
          reportId: 12,
          contentType: 'Review',
          targetType: 'Place',
          reviewId: 102,
          entityId: 8,
          entityName: 'Ada Place',
          regionName: 'Belgrade',
          reason: 'OffensiveContent',
          status: 'Accepted',
          decision: 'Remove review',
          actionTaken: 'Review removed',
          contentExcerpt: 'Offensive snapshot',
          reporterUserId: 43,
          reporterName: 'Grace Hopper',
          reviewAuthorUserId: 42,
          reviewAuthorName: 'Ada Lovelace',
          createdAt: '2026-01-03T00:00:00Z',
          moderationDecisionDate: '2026-01-04T00:00:00Z',
          detailRoute: null,
        }],
        totalCount: 1,
        skip: 0,
        take: 10,
        hasMore: false,
      },
      recentReviews: {
        items: [{
          reviewId: 102,
          targetType: 'Place',
          entityId: 8,
          entityName: 'Ada Place',
          rating: 4,
          commentExcerpt: 'Helpful review.',
          reportCount: 1,
          status: 'Visible',
          createdAt: '2026-01-02T00:00:00Z',
          updatedAt: null,
          detailRoute: null,
        }],
        totalCount: 1,
        skip: 0,
        take: 10,
        hasMore: false,
      },
      moderationHistory: {
        items: [{
          eventId: 'report:12',
          eventType: 'Report accepted',
          summary: 'Review report was accepted.',
          status: 'Accepted',
          actorUserId: 1,
          actorDisplayName: 'Admin User',
          createdAt: '2026-01-04T00:00:00Z',
          note: 'Removed review.',
          source: 'ReviewReport',
          sourceId: 12,
        }],
        totalCount: 1,
        skip: 0,
        take: 10,
        hasMore: false,
      },
      permissions: {
        canAssignTerritoryManager: true,
        canSuspend: true,
        canUnsuspend: false,
        canDelete: true,
        canViewReports: true,
        canViewModerationHistory: true,
      },
    };
  }
});
