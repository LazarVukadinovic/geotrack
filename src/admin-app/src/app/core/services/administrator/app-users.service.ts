import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable, map, tap } from 'rxjs';
import {
  AdminUserListCounts,
  AdminUserListResponse,
  AdminUserPreview,
  AdminUserPreviewPermissions,
  AdminUserPreviewProfile,
  AdminUserPreviewSection,
  AdminUserReportPreview,
  AdminUserRecentReview,
  AdminUserModerationHistoryItem,
  AppUser,
  AppUsersPage,
  AppUsersPageRequest,
  AssignTerritoryManagerRequest,
} from '../../models/administrator/app-user.model';
import { buildAvatarDisplayUrl } from '../../utils/avatar-url';
import { cleanRoleName } from '../../utils/role-utils';

const AVATAR_COLORS = ['#2563eb', '#059669', '#7c3aed', '#d97706', '#dc2626', '#0891b2', '#be185d', '#0f766e'];

@Injectable({ providedIn: 'root' })
export class AppUsersService {
  private readonly endpoint = '/api/admin/users';
  readonly lastPage$ = new BehaviorSubject<AppUsersPage | null>(null);

  constructor(private readonly http: HttpClient) {}

  getPage$(req: AppUsersPageRequest): Observable<AppUsersPage> {
    let params = new HttpParams()
      .set('tab', req.tab)
      .set('status', req.status)
      .set('sort', req.sort)
      .set('skip', String(Math.max(0, req.skip)))
      .set('take', String(Math.max(1, req.take)));

    if (req.search.trim()) {
      params = params.set('search', req.search.trim());
    }

    return this.http.get<AdminUserListResponse>(this.endpoint, { params }).pipe(
      map(response => this.normalizePage(response)),
      tap(page => this.lastPage$.next(page)),
    );
  }

  getUser$(userId: number | string): Observable<AppUser> {
    return this.http.get<AppUser>(`${this.endpoint}/${this.resolveUserId(userId)}`).pipe(
      map(user => this.normalizeUser(user)),
    );
  }

  getPreview$(userId: number | string): Observable<AdminUserPreview> {
    return this.http.get<AdminUserPreview>(`${this.endpoint}/${this.resolveUserId(userId)}/preview`).pipe(
      map(preview => this.normalizePreview(preview)),
    );
  }

  suspend$(userId: number | string): Observable<AppUser> {
    return this.http.post<AppUser>(`${this.endpoint}/${this.resolveUserId(userId)}/suspend`, {}).pipe(
      map(user => this.normalizeUser(user)),
    );
  }

  unsuspend$(userId: number | string): Observable<AppUser> {
    return this.http.post<AppUser>(`${this.endpoint}/${this.resolveUserId(userId)}/unsuspend`, {}).pipe(
      map(user => this.normalizeUser(user)),
    );
  }

  delete$(userId: number | string): Observable<void> {
    return this.http.delete<void>(`${this.endpoint}/${this.resolveUserId(userId)}`);
  }

  assignTerritoryManager$(
    userId: number | string,
    request: AssignTerritoryManagerRequest,
  ): Observable<AppUser> {
    return this.http
      .post<AppUser>(`${this.endpoint}/${this.resolveUserId(userId)}/assign-territory-manager`, request)
      .pipe(map(user => this.normalizeUser(user)));
  }

  formatDate(value: string | null | undefined): string {
    if (!value) {
      return 'Never';
    }

    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return value;
    }

    return date.toLocaleDateString('en-US', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
    });
  }

  private normalizePage(response: AdminUserListResponse | null | undefined): AppUsersPage {
    const safe = response ?? {
      items: [],
      totalCount: 0,
      skip: 0,
      take: 25,
      nextSkip: null,
      hasMore: false,
      counts: this.emptyCounts(),
    };

    return {
      items: (safe.items ?? []).map(user => this.normalizeUser(user)),
      total: this.toNumber(safe.totalCount),
      totalCount: this.toNumber(safe.totalCount),
      skip: this.toNumber(safe.skip),
      take: this.toNumber(safe.take, 25),
      nextSkip: this.toNullableNumber(safe.nextSkip),
      hasMore: !!safe.hasMore,
      counts: this.normalizeCounts(safe.counts),
    };
  }

  private normalizePreview(value: AdminUserPreview | null | undefined): AdminUserPreview {
    const safe = value ?? this.emptyPreview();

    return {
      user: this.normalizePreviewUser(safe.user),
      roles: (safe.roles ?? []).map(role => ({
        ...role,
        roleName: role.roleName ?? '',
        displayName: role.displayName || this.roleDisplayName(role.roleName),
        assignedScope: role.assignedScope || 'Global access',
        assignedAt: role.assignedAt ?? null,
        scopes: role.scopes ?? [],
      })),
      accountSummary: {
        ...this.emptySummary(),
        ...(safe.accountSummary ?? {}),
        reviewsCount: this.toNumber(safe.accountSummary?.reviewsCount),
        reportsMadeCount: this.toNumber(safe.accountSummary?.reportsMadeCount),
        reportedContentCount: this.toNumber(safe.accountSummary?.reportedContentCount),
        pendingReportsAgainstUserCount: this.toNumber(safe.accountSummary?.pendingReportsAgainstUserCount),
        acceptedReportsAgainstUserCount: this.toNumber(safe.accountSummary?.acceptedReportsAgainstUserCount),
        rejectedReportsAgainstUserCount: this.toNumber(safe.accountSummary?.rejectedReportsAgainstUserCount),
        contributionsCount: this.toNumber(safe.accountSummary?.contributionsCount),
        pendingContributionsCount: this.toNumber(safe.accountSummary?.pendingContributionsCount),
        favoritesCount: this.toNumber(safe.accountSummary?.favoritesCount),
        collectionsCount: this.toNumber(safe.accountSummary?.collectionsCount),
        savedItemsCount: this.toNumber(safe.accountSummary?.savedItemsCount),
        ownedPlacesCount: this.toNumber(safe.accountSummary?.ownedPlacesCount),
        activityEventsCount: this.toNumber(safe.accountSummary?.activityEventsCount),
        knownSuspensionCount: this.toNumber(safe.accountSummary?.knownSuspensionCount),
        lastKnownActivityAt: safe.accountSummary?.lastKnownActivityAt ?? null,
        riskLevel: safe.accountSummary?.riskLevel || 'Low',
        riskSummary: safe.accountSummary?.riskSummary || 'No pending moderation risk.',
      },
      reportsMade: this.normalizeSection(safe.reportsMade, report => this.normalizeReport(report)),
      reportsAgainstUser: this.normalizeSection(safe.reportsAgainstUser, report => this.normalizeReport(report)),
      recentReviews: this.normalizeSection(safe.recentReviews, review => this.normalizeReview(review)),
      moderationHistory: this.normalizeSection(safe.moderationHistory, item => this.normalizeHistoryItem(item)),
      permissions: this.normalizePermissions(safe.permissions),
    };
  }

  private normalizeUser(value: AppUser): AppUser {
    const userId = this.toNumber(value.userId, this.toNumber(value.id));
    const displayName = value.displayName?.trim()
      || [value.firstName, value.lastName].filter(Boolean).join(' ').trim()
      || value.email;
    const initials = value.initials?.trim() || this.buildInitials(displayName, value.email);
    const reviewsCount = this.toNumber(value.reviewsCount, this.toNumber(value.reviewCount));
    const reportedCount = this.toNumber(value.reportedCount, this.toNumber(value.reportedReviewCount));

    return {
      ...value,
      userId,
      id: value.id || String(userId),
      displayName,
      firstName: value.firstName ?? '',
      lastName: value.lastName ?? '',
      email: value.email ?? '',
      initials,
      avatarInitials: initials,
      avatarColor: value.avatarColor || this.avatarColorFor(String(userId || value.email)),
      status: this.normalizeStatus(value.status),
      roles: value.roles ?? [],
      isAdmin: !!value.isAdmin,
      isCurrentUser: !!value.isCurrentUser,
      reviewsCount,
      reportedCount,
      reviewCount: reviewsCount,
      reportedReviewCount: reportedCount,
      joinedAt: value.joinedAt,
      lastActiveAt: value.lastActiveAt ?? null,
      assignedTerritories: value.assignedTerritories ?? [],
      assignedTerritoryCount: this.toNumber(value.assignedTerritoryCount, value.assignedTerritories?.length ?? 0),
      canAssignTerritoryManager: !!value.canAssignTerritoryManager,
      canSuspend: !!value.canSuspend,
      canUnsuspend: !!value.canUnsuspend,
      canDelete: !!value.canDelete,
    };
  }

  private normalizePreviewUser(value: AdminUserPreviewProfile): AdminUserPreviewProfile {
    const userId = this.toNumber(value.userId, this.toNumber(value.id));
    const displayName = value.displayName?.trim()
      || [value.firstName, value.lastName].filter(Boolean).join(' ').trim()
      || value.email
      || 'Unknown user';
    const initials = value.initials?.trim() || this.buildInitials(displayName, value.email ?? '');

    return {
      ...value,
      userId,
      id: value.id || String(userId),
      displayName,
      firstName: value.firstName ?? '',
      lastName: value.lastName ?? '',
      email: value.email ?? '',
      avatarUrl: buildAvatarDisplayUrl(value.avatarUrl, null) || null,
      initials,
      avatarInitials: value.avatarInitials || initials,
      avatarColor: value.avatarColor || this.avatarColorFor(String(userId || value.email)),
      status: this.normalizeStatus(value.status),
      roles: value.roles ?? [],
      isCurrentUser: !!value.isCurrentUser,
      isAdmin: !!value.isAdmin,
      hasElevatedAccess: !!value.hasElevatedAccess,
      accountType: value.accountType || 'App User',
      joinedAt: value.joinedAt,
      lastActiveAt: value.lastActiveAt ?? null,
      lastLoginAt: value.lastLoginAt ?? null,
      assignedTerritories: value.assignedTerritories ?? [],
      assignedRegions: value.assignedRegions ?? [],
    };
  }

  private normalizeSection<T>(
    value: AdminUserPreviewSection<T> | null | undefined,
    normalizeItem: (item: T) => T,
  ): AdminUserPreviewSection<T> {
    return {
      items: (value?.items ?? []).map(item => normalizeItem(item)),
      totalCount: this.toNumber(value?.totalCount),
      skip: this.toNumber(value?.skip),
      take: this.toNumber(value?.take, 10),
      hasMore: !!value?.hasMore,
    };
  }

  private normalizeReport(value: AdminUserReportPreview): AdminUserReportPreview {
    return {
      ...value,
      reportId: this.toNumber(value.reportId),
      contentType: value.contentType || 'Review',
      targetType: value.targetType || 'Review',
      reviewId: this.toNumber(value.reviewId),
      entityId: this.toNumber(value.entityId),
      entityName: value.entityName || 'Unknown item',
      regionName: value.regionName || 'Unassigned region',
      reason: value.reason || 'Unspecified',
      status: value.status || 'Pending',
      decision: value.decision ?? null,
      actionTaken: value.actionTaken || 'Pending review',
      contentExcerpt: value.contentExcerpt || 'No content snapshot available.',
      reporterUserId: this.toNumber(value.reporterUserId),
      reporterName: value.reporterName || 'Unknown reporter',
      reviewAuthorUserId: this.toNumber(value.reviewAuthorUserId),
      reviewAuthorName: value.reviewAuthorName || 'Unknown author',
      moderationDecisionDate: value.moderationDecisionDate ?? null,
      detailRoute: value.detailRoute ?? null,
    };
  }

  private normalizeReview(value: AdminUserRecentReview): AdminUserRecentReview {
    return {
      ...value,
      reviewId: this.toNumber(value.reviewId),
      targetType: value.targetType || 'Review',
      entityId: this.toNumber(value.entityId),
      entityName: value.entityName || 'Unknown item',
      rating: this.toNumber(value.rating),
      commentExcerpt: value.commentExcerpt || 'No review text.',
      reportCount: this.toNumber(value.reportCount),
      status: value.status || 'Visible',
      updatedAt: value.updatedAt ?? null,
      detailRoute: value.detailRoute ?? null,
    };
  }

  private normalizeHistoryItem(value: AdminUserModerationHistoryItem): AdminUserModerationHistoryItem {
    return {
      ...value,
      eventId: value.eventId || `${value.source || 'history'}-${value.sourceId ?? value.createdAt}`,
      eventType: value.eventType || 'Moderation event',
      summary: value.summary || 'Moderation activity recorded.',
      status: value.status || 'Recorded',
      actorUserId: value.actorUserId ?? null,
      actorDisplayName: value.actorDisplayName ?? null,
      note: value.note ?? null,
      source: value.source || 'Moderation',
      sourceId: value.sourceId ?? null,
    };
  }

  private normalizePermissions(value: AdminUserPreviewPermissions | null | undefined): AdminUserPreviewPermissions {
    return {
      canAssignTerritoryManager: !!value?.canAssignTerritoryManager,
      canSuspend: !!value?.canSuspend,
      canUnsuspend: !!value?.canUnsuspend,
      canDelete: !!value?.canDelete,
      canViewReports: value?.canViewReports !== false,
      canViewModerationHistory: value?.canViewModerationHistory !== false,
    };
  }

  private normalizeCounts(value: AdminUserListCounts | null | undefined): AdminUserListCounts {
    return {
      appUsersCount: this.toNumber(value?.appUsersCount),
      territoryManagersCount: this.toNumber(value?.territoryManagersCount),
      activeCount: this.toNumber(value?.activeCount),
      suspendedCount: this.toNumber(value?.suspendedCount),
      deletedCount: this.toNumber(value?.deletedCount),
    };
  }

  private emptyCounts(): AdminUserListCounts {
    return {
      appUsersCount: 0,
      territoryManagersCount: 0,
      activeCount: 0,
      suspendedCount: 0,
      deletedCount: 0,
    };
  }

  private emptyPreview(): AdminUserPreview {
    return {
      user: {
        userId: 0,
        id: '0',
        displayName: 'Unknown user',
        firstName: '',
        lastName: '',
        email: '',
        avatarUrl: null,
        initials: '?',
        avatarInitials: '?',
        avatarColor: AVATAR_COLORS[0],
        status: 'Active',
        roles: [],
        isCurrentUser: false,
        isAdmin: false,
        hasElevatedAccess: false,
        accountType: 'App User',
        joinedAt: '',
        lastActiveAt: null,
        lastLoginAt: null,
        assignedTerritories: [],
        assignedRegions: [],
      },
      roles: [],
      accountSummary: this.emptySummary(),
      reportsMade: this.emptySection(),
      reportsAgainstUser: this.emptySection(),
      recentReviews: this.emptySection(),
      moderationHistory: this.emptySection(),
      permissions: this.normalizePermissions(null),
    };
  }

  private emptySummary() {
    return {
      reviewsCount: 0,
      reportsMadeCount: 0,
      reportedContentCount: 0,
      pendingReportsAgainstUserCount: 0,
      acceptedReportsAgainstUserCount: 0,
      rejectedReportsAgainstUserCount: 0,
      contributionsCount: 0,
      pendingContributionsCount: 0,
      favoritesCount: 0,
      collectionsCount: 0,
      savedItemsCount: 0,
      ownedPlacesCount: 0,
      activityEventsCount: 0,
      knownSuspensionCount: 0,
      lastKnownActivityAt: null,
      riskLevel: 'Low',
      riskSummary: 'No pending moderation risk.',
    };
  }

  private emptySection<T>(): AdminUserPreviewSection<T> {
    return {
      items: [],
      totalCount: 0,
      skip: 0,
      take: 10,
      hasMore: false,
    };
  }

  private normalizeStatus(status: unknown): AppUser['status'] {
    return status === 'Suspended' || status === 'Deleted' ? status : 'Active';
  }

  private roleDisplayName(roleName: string | null | undefined): string {
    if (!roleName) {
      return 'User';
    }

    return cleanRoleName(
      roleName
        .replace(/([a-z])([A-Z])/g, '$1 $2')
        .replace(/_/g, ' '),
    );
  }

  private toNumber(value: unknown, fallback = 0): number {
    return typeof value === 'number' && Number.isFinite(value) ? value : fallback;
  }

  private toNullableNumber(value: unknown): number | null {
    return typeof value === 'number' && Number.isFinite(value) ? value : null;
  }

  private resolveUserId(value: number | string): number {
    if (typeof value === 'number' && Number.isFinite(value) && value > 0) {
      return value;
    }

    const direct = Number(value);
    if (Number.isFinite(direct) && direct > 0) {
      return direct;
    }

    throw new Error('User id is invalid.');
  }

  private avatarColorFor(seed: string): string {
    const hash = Array.from(seed).reduce((total, char) => total + char.charCodeAt(0), 0);
    return AVATAR_COLORS[hash % AVATAR_COLORS.length];
  }

  private buildInitials(name: string, email: string): string {
    const source = name.trim() || email.trim();
    const parts = source.split(/\s+/).filter(Boolean);
    if (parts.length >= 2) {
      return `${parts[0][0] ?? ''}${parts[1][0] ?? ''}`.toUpperCase();
    }

    return (source.slice(0, 2) || '?').toUpperCase();
  }
}
