import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { AdminDashboardService } from './admin-dashboard.service';

describe('AdminDashboardService', () => {
  let service: AdminDashboardService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AdminDashboardService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('loads the administrator dashboard from the backend endpoint', () => {
    let pendingReportsCount = -1;

    service.loadDashboard().subscribe(dashboard => {
      pendingReportsCount = dashboard.badges.pendingReportsCount;
    });

    const request = http.expectOne('/api/admin/dashboard');
    expect(request.request.method).toBe('GET');
    request.flush({
      currentUser: {
        id: 46,
        displayName: 'Zika Admin',
        email: 'zika@gmail.com',
        avatarUrl: null,
        roleDisplayName: 'Administrator',
      },
      stats: {
        territoriesCount: 12,
        assignedTerritoriesCount: 5,
        appUsersCount: 34,
        suspendedUsersCount: 2,
        reviewsCount: 56,
        reportedReviewsCount: 9,
        pendingReportsCount: 7,
      },
      quickLinks: {
        territoryManagementRoute: '/admin-territories',
        userManagementRoute: '/admin-users',
        reviewManagementRoute: '/admin-reviews',
        pendingReportsRoute: '/admin-reviews?status=reported',
      },
      badges: {
        pendingReportsCount: 7,
      },
    });

    expect(pendingReportsCount).toBe(7);
    expect(service.getPendingReportsCount()).toBe(7);
  });

  it('normalizes missing dashboard values to safe zero/default values', () => {
    service.loadDashboard().subscribe(dashboard => {
      expect(dashboard.currentUser.displayName).toBe('Administrator');
      expect(dashboard.stats.territoriesCount).toBe(0);
      expect(dashboard.stats.pendingReportsCount).toBe(0);
      expect(dashboard.badges.pendingReportsCount).toBe(0);
      expect(dashboard.quickLinks.pendingReportsRoute).toBe('/admin-reviews?status=reported');
    });

    http.expectOne('/api/admin/dashboard').flush({});
  });
});
