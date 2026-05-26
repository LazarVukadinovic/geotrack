import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable, map, tap } from 'rxjs';
import {
  AdminDashboard,
  AdminDashboardBadges,
  AdminDashboardCurrentUser,
  AdminDashboardQuickLinks,
  AdminDashboardStats,
} from '../../models/administrator/admin-dashboard.model';

@Injectable({ providedIn: 'root' })
export class AdminDashboardService {
  private readonly dashboardSubject = new BehaviorSubject<AdminDashboard | null>(null);

  readonly dashboard$ = this.dashboardSubject.asObservable();
  readonly pendingReportsCount$ = this.dashboard$.pipe(
    map(dashboard => dashboard?.badges.pendingReportsCount ?? dashboard?.stats.pendingReportsCount ?? 0),
  );
  readonly supportReportsCount$ = this.dashboard$.pipe(
    map(dashboard => this.supportReportsCountFromDashboard(dashboard)),
  );

  constructor(private readonly http: HttpClient) {}

  loadDashboard(): Observable<AdminDashboard> {
    return this.http.get<AdminDashboard>('/api/admin/dashboard').pipe(
      map(response => this.normalizeDashboard(response)),
      tap(dashboard => this.dashboardSubject.next(dashboard)),
    );
  }

  getSnapshot(): AdminDashboard | null {
    return this.dashboardSubject.value;
  }

  getPendingReportsCount(): number {
    const dashboard = this.dashboardSubject.value;
    return dashboard?.badges.pendingReportsCount ?? dashboard?.stats.pendingReportsCount ?? 0;
  }

  getSupportReportsCount(): number {
    return this.supportReportsCountFromDashboard(this.dashboardSubject.value);
  }

  private normalizeDashboard(value: AdminDashboard | null | undefined): AdminDashboard {
    const stats = this.normalizeStats(value?.stats);

    return {
      currentUser: this.normalizeCurrentUser(value?.currentUser),
      stats,
      quickLinks: this.normalizeQuickLinks(value?.quickLinks),
      badges: this.normalizeBadges(value?.badges, stats.pendingReportsCount, stats.unresolvedSupportTicketsCount),
    };
  }

  private normalizeCurrentUser(value: AdminDashboardCurrentUser | null | undefined): AdminDashboardCurrentUser {
    return {
      id: this.toNumber(value?.id),
      displayName: value?.displayName?.trim() || value?.email?.trim() || 'Administrator',
      email: value?.email?.trim() || '',
      avatarUrl: value?.avatarUrl ?? null,
      roleDisplayName: value?.roleDisplayName?.trim() || 'Administrator',
    };
  }

  private normalizeStats(value: AdminDashboardStats | null | undefined): AdminDashboardStats {
    return {
      territoriesCount: this.toNumber(value?.territoriesCount),
      assignedTerritoriesCount: this.toNumber(value?.assignedTerritoriesCount),
      appUsersCount: this.toNumber(value?.appUsersCount),
      suspendedUsersCount: this.toNumber(value?.suspendedUsersCount),
      reviewsCount: this.toNumber(value?.reviewsCount),
      reportedReviewsCount: this.toNumber(value?.reportedReviewsCount),
      pendingReportsCount: this.toNumber(value?.pendingReportsCount),
      supportTicketsCount: this.toNumber(value?.supportTicketsCount),
      openSupportTicketsCount: this.toNumber(value?.openSupportTicketsCount),
      inProgressSupportTicketsCount: this.toNumber(value?.inProgressSupportTicketsCount),
      resolvedSupportTicketsCount: this.toNumber(value?.resolvedSupportTicketsCount),
      dismissedSupportTicketsCount: this.toNumber(value?.dismissedSupportTicketsCount),
      unresolvedSupportTicketsCount: this.toNumber(value?.unresolvedSupportTicketsCount),
      highPrioritySupportTicketsCount: this.toNumber(value?.highPrioritySupportTicketsCount),
    };
  }

  private normalizeQuickLinks(value: AdminDashboardQuickLinks | null | undefined): AdminDashboardQuickLinks {
    return {
      territoryManagementRoute: value?.territoryManagementRoute || '/admin-territories',
      userManagementRoute: value?.userManagementRoute || '/admin-users',
      reviewManagementRoute: value?.reviewManagementRoute || '/admin-reviews',
      supportTicketsRoute: value?.supportTicketsRoute || '/admin-support-tickets',
      pendingReportsRoute: value?.pendingReportsRoute || '/admin-reviews?status=reported',
    };
  }

  private normalizeBadges(
    value: AdminDashboardBadges | null | undefined,
    fallbackPendingReportsCount: number,
    fallbackSupportReportsCount: number,
  ): AdminDashboardBadges {
    return {
      pendingReportsCount: this.toNumber(value?.pendingReportsCount, fallbackPendingReportsCount),
      supportReportsCount: this.toNumber(value?.supportReportsCount, fallbackSupportReportsCount),
    };
  }

  private supportReportsCountFromDashboard(dashboard: AdminDashboard | null | undefined): number {
    return dashboard?.badges.supportReportsCount ?? dashboard?.stats.unresolvedSupportTicketsCount ?? 0;
  }

  private toNumber(value: unknown, fallback = 0): number {
    return typeof value === 'number' && Number.isFinite(value) ? value : fallback;
  }
}
