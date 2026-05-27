import { Component, OnInit, OnDestroy, ChangeDetectionStrategy, ChangeDetectorRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';

import { Subject } from 'rxjs';
import { finalize, takeUntil } from 'rxjs/operators';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { AuthService } from '../../../../core/services/auth.service';
import { AdminDashboardService } from '../../../../core/services/administrator/admin-dashboard.service';
import {
  AdminDashboard,
  AdminDashboardStats,
} from '../../../../core/models/administrator/admin-dashboard.model';

@Component({
  selector: 'app-admin-dashboard',
  standalone: true,
  imports: [CommonModule, SidebarComponent, TopbarComponent],
  templateUrl: './admin-dashboard.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminDashboardComponent implements OnInit, OnDestroy {
  private readonly auth = inject(AuthService);
  private readonly dashboardService = inject(AdminDashboardService);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly router = inject(Router);

  private destroy$ = new Subject<void>();

  dashboard: AdminDashboard | null = null;
  isLoading = false;
  errorMessage: string | null = null;
  supportReportsCount = 0;

  get userName(): string {
    return this.dashboard?.currentUser.displayName || this.auth.fullName || 'Administrator';
  }

  get userRole(): string {
    return this.dashboard?.currentUser.roleDisplayName || this.auth.getUser()?.role || 'Administrator';
  }

  stats: { label: string; value: number; sub: string; color: string; icon: string; route: string }[] = [];

  ngOnInit(): void {
    this.loadDashboard();
  }

  ngOnDestroy(): void { this.destroy$.next(); this.destroy$.complete(); }

  loadDashboard(): void {
    this.isLoading = true;
    this.errorMessage = null;
    this.cdr.markForCheck();

    this.dashboardService.loadDashboard()
      .pipe(
        takeUntil(this.destroy$),
        finalize(() => {
          this.isLoading = false;
          this.cdr.markForCheck();
        }),
      )
      .subscribe({
        next: dashboard => {
          this.dashboard = dashboard;
          this.supportReportsCount = dashboard.badges.supportReportsCount;
          this.buildStats(dashboard.stats);
          this.cdr.markForCheck();
        },
        error: () => {
          this.dashboard = null;
          this.supportReportsCount = 0;
          this.stats = [];
          this.errorMessage = 'Unable to load administrator dashboard data.';
          this.cdr.markForCheck();
        },
      });
  }

  private buildStats(stats: AdminDashboardStats): void {
    const quickLinks = this.dashboard?.quickLinks;
    this.stats = [
      {
        label: 'Territories',
        value: stats.territoriesCount,
        sub: `${stats.assignedTerritoriesCount} assigned`,
        color: '#3b82f6',
        icon: 'globe',
        route: quickLinks?.territoryManagementRoute ?? '/admin-territories',
      },
      {
        label: 'App Users',
        value: stats.appUsersCount,
        sub: `${stats.suspendedUsersCount} suspended`,
        color: '#10b981',
        icon: 'users',
        route: quickLinks?.userManagementRoute ?? '/admin-users',
      },
      {
        label: 'Support Reports',
        value: stats.unresolvedSupportTicketsCount,
        sub: this.supportReportsSubtitle(stats),
        color: '#8b5cf6',
        icon: 'support',
        route: quickLinks?.supportTicketsRoute ?? '/admin-support-tickets',
      },
    ];
  }

  openPendingReports(): void {
    this.navigateTo(this.dashboard?.quickLinks.supportTicketsRoute ?? '/admin-support-tickets');
  }

  navigateTo(route: string): void {
    this.router.navigateByUrl(route);
  }

  private supportReportsSubtitle(stats: AdminDashboardStats): string {
    if (stats.highPrioritySupportTicketsCount > 0) {
      return `${stats.highPrioritySupportTicketsCount} high priority`;
    }

    if (stats.unresolvedSupportTicketsCount === 0) {
      return 'No unresolved tickets';
    }

    return `${stats.openSupportTicketsCount} open, ${stats.inProgressSupportTicketsCount} in progress`;
  }
}
