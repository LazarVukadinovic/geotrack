import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable, map, tap } from 'rxjs';
import {
  ContentModeratorDashboard,
  ContentModeratorDashboardBadges,
  ContentModeratorDashboardQuickLinks,
  ContentModeratorDashboardStats,
} from '../../models/content-moderator/content-moderator-dashboard.model';
import { AdminDashboardCurrentUser } from '../../models/administrator/admin-dashboard.model';

@Injectable({ providedIn: 'root' })
export class ContentModeratorDashboardService {
  private readonly dashboardSubject = new BehaviorSubject<ContentModeratorDashboard | null>(null);

  readonly dashboard$ = this.dashboardSubject.asObservable();
  readonly pendingReviewReportsCount$ = this.dashboard$.pipe(
    map(dashboard => dashboard?.badges.pendingReviewReportsCount ?? dashboard?.stats.pendingReviewReportsCount ?? 0),
  );

  constructor(private readonly http: HttpClient) {}

  loadDashboard(): Observable<ContentModeratorDashboard> {
    return this.http.get<ContentModeratorDashboard>('/api/admin/content-moderator/dashboard').pipe(
      map(response => this.normalizeDashboard(response)),
      tap(dashboard => this.dashboardSubject.next(dashboard)),
    );
  }

  getSnapshot(): ContentModeratorDashboard | null {
    return this.dashboardSubject.value;
  }

  private normalizeDashboard(value: ContentModeratorDashboard | null | undefined): ContentModeratorDashboard {
    const stats = this.normalizeStats(value?.stats);

    return {
      currentUser: this.normalizeCurrentUser(value?.currentUser),
      stats,
      quickLinks: this.normalizeQuickLinks(value?.quickLinks),
      badges: this.normalizeBadges(value?.badges, stats.pendingReviewReportsCount),
    };
  }

  private normalizeCurrentUser(value: AdminDashboardCurrentUser | null | undefined): AdminDashboardCurrentUser {
    return {
      id: this.toNumber(value?.id),
      displayName: value?.displayName?.trim() || value?.email?.trim() || 'Content Moderator',
      email: value?.email?.trim() || '',
      avatarUrl: value?.avatarUrl ?? null,
      roleDisplayName: value?.roleDisplayName?.trim() || 'Content Moderator',
    };
  }

  private normalizeStats(value: ContentModeratorDashboardStats | null | undefined): ContentModeratorDashboardStats {
    return {
      destinationsCount: this.toNumber(value?.destinationsCount),
      locationsCount: this.toNumber(value?.locationsCount),
      placesCount: this.toNumber(value?.placesCount),
      eventsCount: this.toNumber(value?.eventsCount),
      suspendedDestinationsCount: this.toNumber(value?.suspendedDestinationsCount),
      suspendedLocationsCount: this.toNumber(value?.suspendedLocationsCount),
      suspendedPlacesCount: this.toNumber(value?.suspendedPlacesCount),
      suspendedEventsCount: this.toNumber(value?.suspendedEventsCount),
      deletedDestinationsCount: this.toNumber(value?.deletedDestinationsCount),
      deletedLocationsCount: this.toNumber(value?.deletedLocationsCount),
      deletedPlacesCount: this.toNumber(value?.deletedPlacesCount),
      deletedEventsCount: this.toNumber(value?.deletedEventsCount),
      pendingReviewReportsCount: this.toNumber(value?.pendingReviewReportsCount),
      reportedReviewsCount: this.toNumber(value?.reportedReviewsCount),
      activeReviewsCount: this.toNumber(value?.activeReviewsCount),
      resolvedReportsCount: this.toNumber(value?.resolvedReportsCount),
      removedReviewsCount: this.toNumber(value?.removedReviewsCount),
    };
  }

  private normalizeQuickLinks(value: ContentModeratorDashboardQuickLinks | null | undefined): ContentModeratorDashboardQuickLinks {
    return {
      destinationsReviewRoute: value?.destinationsReviewRoute || '/content-moderator/destinations',
      locationsReviewRoute: value?.locationsReviewRoute || '/content-moderator/locations',
      placesReviewRoute: value?.placesReviewRoute || '/content-moderator/places',
      eventsReviewRoute: value?.eventsReviewRoute || '/content-moderator/events',
      reviewManagementRoute: value?.reviewManagementRoute || '/content-moderator/review-management',
      pendingReportsRoute: value?.pendingReportsRoute || '/content-moderator/review-management?status=reported',
    };
  }

  private normalizeBadges(
    value: ContentModeratorDashboardBadges | null | undefined,
    fallbackPendingReviewReportsCount: number,
  ): ContentModeratorDashboardBadges {
    return {
      pendingReviewReportsCount: this.toNumber(value?.pendingReviewReportsCount, fallbackPendingReviewReportsCount),
    };
  }

  private toNumber(value: unknown, fallback = 0): number {
    return typeof value === 'number' && Number.isFinite(value) ? value : fallback;
  }
}
