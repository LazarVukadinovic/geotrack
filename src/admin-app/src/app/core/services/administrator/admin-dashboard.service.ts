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

  private normalizeDashboard(value: AdminDashboard | null | undefined): AdminDashboard {
    const stats = this.normalizeStats(value?.stats);

    return {
      currentUser: this.normalizeCurrentUser(value?.currentUser),
      stats,
      quickLinks: this.normalizeQuickLinks(value?.quickLinks),
      badges: this.normalizeBadges(value?.badges, stats.pendingReportsCount),
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
    };
  }

  private normalizeQuickLinks(value: AdminDashboardQuickLinks | null | undefined): AdminDashboardQuickLinks {
    return {
      territoryManagementRoute: value?.territoryManagementRoute || '/admin-territories',
      userManagementRoute: value?.userManagementRoute || '/admin-users',
      reviewManagementRoute: value?.reviewManagementRoute || '/admin-reviews',
      pendingReportsRoute: value?.pendingReportsRoute || '/admin-reviews?status=reported',
    };
  }

  private normalizeBadges(value: AdminDashboardBadges | null | undefined, fallbackPendingReportsCount: number): AdminDashboardBadges {
    return {
      pendingReportsCount: this.toNumber(value?.pendingReportsCount, fallbackPendingReportsCount),
    };
  }

  private toNumber(value: unknown, fallback = 0): number {
    return typeof value === 'number' && Number.isFinite(value) ? value : fallback;
  }
}
