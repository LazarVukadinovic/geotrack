import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormsModule } from '@angular/forms';
import { Observable, of } from 'rxjs';
import { vi } from 'vitest';

import { UserManagementComponent } from './user-management.component';
import { AppUsersService } from '../../../../core/services/administrator/app-users.service';
import { AdminDashboardService } from '../../../../core/services/administrator/admin-dashboard.service';
import { TerritoryService } from '../../../../core/services/administrator/territory.service';
import { AdminUserPreview, AppUser, AppUsersPage, AppUsersPageRequest } from '../../../../core/models/administrator/app-user.model';
import { Territory, TerritoryPage } from '../../../../core/models/administrator/territory.model';

@Component({
  selector: 'app-sidebar',
  standalone: true,
  template: '<aside>{{ supportReportsCount }}</aside>',
})
class SidebarStubComponent {
  @Input() supportReportsCount: number | null = null;
}

@Component({
  selector: 'app-topbar',
  standalone: true,
  template: '<header>{{ title }}</header>',
})
class TopbarStubComponent {
  @Input() title = '';
  @Input() showBack = false;
  @Input() backRoute = '';
}

describe('UserManagementComponent', () => {
  let fixture: ComponentFixture<UserManagementComponent>;
  let component: UserManagementComponent;
  let usersService: {
    getPage$: ReturnType<typeof vi.fn>;
    getPreview$: ReturnType<typeof vi.fn>;
    suspend$: ReturnType<typeof vi.fn>;
    unsuspend$: ReturnType<typeof vi.fn>;
    delete$: ReturnType<typeof vi.fn>;
    assignTerritoryManager$: ReturnType<typeof vi.fn>;
    formatDate: ReturnType<typeof vi.fn>;
  };
  let territoryService: {
    getPage$: ReturnType<typeof vi.fn>;
    invalidateMapCache: ReturnType<typeof vi.fn>;
    getCountryByCode: ReturnType<typeof vi.fn>;
  };

  beforeEach(async () => {
    usersService = {
      getPage$: vi.fn((req: AppUsersPageRequest) => of(pageForRequest(req))),
      getPreview$: vi.fn((userId: number) => of(previewForUser(userId))),
      suspend$: vi.fn(() => of({ ...userOne(), status: 'Suspended', canSuspend: false, canUnsuspend: true })),
      unsuspend$: vi.fn(() => of(userOne())),
      delete$: vi.fn(() => of(undefined)),
      assignTerritoryManager$: vi.fn(() => of({ ...userOne(), roles: ['RegisteredUser', 'TerritoryManager'] })),
      formatDate: vi.fn((value: string | null | undefined) => value ?? 'Never'),
    };
    territoryService = {
      getPage$: vi.fn(() => of(territoryPage())),
      invalidateMapCache: vi.fn(),
      getCountryByCode: vi.fn((code: string) => ({ code, name: code === 'RS' ? 'Serbia' : code, continent: 'Europe', flag: '' })),
    };

    await TestBed.configureTestingModule({
      imports: [UserManagementComponent],
      providers: [
        { provide: AppUsersService, useValue: usersService },
        { provide: TerritoryService, useValue: territoryService },
        {
          provide: AdminDashboardService,
          useValue: { loadDashboard: vi.fn(() => of({ badges: { pendingReportsCount: 4, supportReportsCount: 3 } })) },
        },
      ],
    })
      .overrideComponent(UserManagementComponent, {
        set: { imports: [CommonModule, FormsModule, SidebarStubComponent, TopbarStubComponent] },
      })
      .compileComponents();

    fixture = TestBed.createComponent(UserManagementComponent);
    component = fixture.componentInstance;
  });

  it('requests the first backend page with take 25', () => {
    fixture.detectChanges();

    expect(usersService.getPage$).toHaveBeenCalledWith(expect.objectContaining({
      tab: 'app-users',
      skip: 0,
      take: 25,
      status: 'all',
      sort: 'newest',
    }));
    expect(normalizedText()).toContain('Showing 1 of 2 users');
  });

  it('appends users when Show More loads the next page', () => {
    usersService.getPage$.mockImplementation((req: AppUsersPageRequest) => of(pageForRequest(req)));
    fixture.detectChanges();

    component.loadMore();
    fixture.detectChanges();

    expect(usersService.getPage$).toHaveBeenLastCalledWith(expect.objectContaining({ skip: 25, take: 25 }));
    expect(component.users.map(user => user.email)).toEqual(['ada@example.com', 'grace@example.com']);
  });

  it('resets pagination when search or tab changes', async () => {
    fixture.detectChanges();

    component.searchQuery = 'grace';
    component.onSearchChange();
    await new Promise(resolve => setTimeout(resolve, 350));
    fixture.detectChanges();

    expect(usersService.getPage$).toHaveBeenLastCalledWith(expect.objectContaining({
      search: 'grace',
      skip: 0,
      take: 25,
    }));

    component.setTab('territory-managers');
    fixture.detectChanges();

    expect(usersService.getPage$).toHaveBeenLastCalledWith(expect.objectContaining({
      tab: 'territory-managers',
      skip: 0,
      take: 25,
    }));
  });

  it('renders row actions from backend permission flags and calls suspend', () => {
    fixture.detectChanges();

    component.toggleDropdown('42', {
      stopPropagation: vi.fn(),
      currentTarget: {
        getBoundingClientRect: () => ({ top: 10, bottom: 30, right: 40 }),
      },
    } as unknown as MouseEvent);
    fixture.detectChanges();

    const text = normalizedText();
    expect(text).toContain('Assign Territory Manager');
    expect(text).toContain('Suspend Account');
    expect(text).toContain('Delete Account');
    expect(text).not.toContain('Unsuspend Account');

    component.openConfirmation('suspend', userOne());
    component.confirmAccountAction();

    expect(usersService.suspend$).toHaveBeenCalledWith(42);
  });

  it('opens a real user preview from a row click', () => {
    fixture.detectChanges();

    const firstRow = fixture.nativeElement.querySelector('tbody tr') as HTMLElement;
    firstRow.click();
    fixture.detectChanges();

    expect(usersService.getPreview$).toHaveBeenCalledWith(42);
    const text = normalizedText();
    expect(text).toContain('Account Overview');
    expect(text).toContain('Registered User');
    expect(text).toContain('Reports Made');
    expect(text).toContain('Reports Against');
  });

  it('keeps the action menu click from opening the preview', () => {
    fixture.detectChanges();

    const actionButton = fixture.nativeElement.querySelector('tbody tr td:last-child button') as HTMLElement;
    actionButton.click();
    fixture.detectChanges();

    expect(usersService.getPreview$).not.toHaveBeenCalled();
    expect(normalizedText()).toContain('Assign Territory Manager');
  });

  it('renders report, review, and moderation sections in the preview', () => {
    fixture.detectChanges();
    component.openUserPreview(userOne());
    fixture.detectChanges();

    component.setPreviewTab('reports');
    fixture.detectChanges();
    expect(normalizedText()).toContain('Reports Made By This User');
    expect(normalizedText()).toContain('Offensive snapshot');

    component.setPreviewTab('reviews');
    fixture.detectChanges();
    expect(normalizedText()).toContain('Reviews & Content');
    expect(normalizedText()).toContain('Ada Place');

    component.setPreviewTab('history');
    fixture.detectChanges();
    expect(normalizedText()).toContain('Moderation History');
    expect(normalizedText()).toContain('Report accepted');
  });

  it('opens assign modal and saves territory assignment', () => {
    fixture.detectChanges();

    component.openAssignTerritoryManager(userOne());
    fixture.detectChanges();

    expect(component.assignTarget?.email).toBe('ada@example.com');
    expect(component.assignTerritoryId).toBe(101);

    component.confirmAssignTerritoryManager();

    expect(usersService.assignTerritoryManager$).toHaveBeenCalledWith(42, { territoryId: 101 });
    expect(territoryService.invalidateMapCache).toHaveBeenCalled();
  });

  function normalizedText(): string {
    return (fixture.nativeElement.textContent as string).replace(/\s+/g, ' ').trim();
  }

  function pageForRequest(req: AppUsersPageRequest): AppUsersPage {
    if (req.skip === 0) {
      return {
        items: [userOne()],
        total: 2,
        totalCount: 2,
        skip: 0,
        take: 25,
        nextSkip: 25,
        hasMore: true,
        counts: counts(),
      };
    }

    return {
      items: [userTwo()],
      total: 2,
      totalCount: 2,
      skip: 25,
      take: 25,
      nextSkip: null,
      hasMore: false,
      counts: counts(),
    };
  }

  function userOne(): AppUser {
    return {
      userId: 42,
      id: '42',
      displayName: 'Ada Lovelace',
      firstName: 'Ada',
      lastName: 'Lovelace',
      email: 'ada@example.com',
      avatarUrl: null,
      initials: 'AL',
      avatarInitials: 'AL',
      avatarColor: '#2563eb',
      status: 'Active',
      roles: ['RegisteredUser'],
      isAdmin: false,
      isCurrentUser: false,
      reviewsCount: 3,
      reportedCount: 1,
      reviewCount: 3,
      reportedReviewCount: 1,
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

  function userTwo(): AppUser {
    return {
      ...userOne(),
      userId: 43,
      id: '43',
      displayName: 'Grace Hopper',
      firstName: 'Grace',
      lastName: 'Hopper',
      email: 'grace@example.com',
      initials: 'GH',
      avatarInitials: 'GH',
      canAssignTerritoryManager: false,
    };
  }

  function previewForUser(userId: number): AdminUserPreview {
    const user = userId === 43 ? userTwo() : userOne();

    return {
      user: {
        userId: user.userId,
        id: user.id,
        displayName: user.displayName,
        firstName: user.firstName,
        lastName: user.lastName,
        email: user.email,
        avatarUrl: user.avatarUrl,
        initials: user.initials,
        avatarInitials: user.avatarInitials,
        avatarColor: user.avatarColor,
        status: user.status,
        roles: user.roles,
        isCurrentUser: user.isCurrentUser,
        isAdmin: user.isAdmin,
        hasElevatedAccess: false,
        accountType: 'App User',
        joinedAt: user.joinedAt,
        lastActiveAt: user.lastActiveAt,
        lastLoginAt: null,
        assignedTerritories: [],
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
          reporterUserId: user.userId,
          reporterName: user.displayName,
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
          reviewAuthorUserId: user.userId,
          reviewAuthorName: user.displayName,
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

  function counts() {
    return {
      appUsersCount: 2,
      territoryManagersCount: 1,
      activeCount: 2,
      suspendedCount: 0,
      deletedCount: 0,
    };
  }

  function territoryPage(): TerritoryPage {
    return {
      total: 1,
      items: [territory()],
    };
  }

  function territory(): Territory {
    return {
      id: 'TERR-101',
      territoryId: 101,
      countryId: 101,
      name: 'Serbia',
      displayName: 'Serbia',
      internalReference: 'TERR-101',
      territoryKind: 'Country',
      countryCode: 'RS',
      iso2: 'RS',
      iso3: 'SRB',
      countries: ['RS'],
      assignedManagerId: null,
      assignedManagerName: null,
      assignedManagerEmail: null,
      status: 'Unassigned',
      hasBoundary: true,
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
    };
  }
});
