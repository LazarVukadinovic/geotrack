import { ChangeDetectionStrategy, ChangeDetectorRef, Component, OnDestroy, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { Subject, finalize, takeUntil } from 'rxjs';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { AuthService } from '../../../../core/services/auth.service';
import { ContentModeratorDashboardService } from '../../../../core/services/content-moderator/content-moderator-dashboard.service';
import {
  ContentModeratorDashboard,
  ContentModeratorDashboardStats,
} from '../../../../core/models/content-moderator/content-moderator-dashboard.model';

@Component({
  selector: 'app-content-moderator-dashboard',
  standalone: true,
  imports: [CommonModule, SidebarComponent, TopbarComponent],
  templateUrl: './content-moderator-dashboard.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ContentModeratorDashboardComponent implements OnInit, OnDestroy {
  private readonly auth = inject(AuthService);
  private readonly dashboardService = inject(ContentModeratorDashboardService);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly router = inject(Router);
  private readonly destroy$ = new Subject<void>();

  dashboard: ContentModeratorDashboard | null = null;
  isLoading = false;
  errorMessage: string | null = null;
  pendingReviewReportsCount = 0;

  stats: { label: string; value: number; sub: string; color: string; icon: string; route: string }[] = [];

  get userName(): string {
    return this.dashboard?.currentUser.displayName || this.auth.fullName || 'Content Moderator';
  }

  get userRole(): string {
    return this.dashboard?.currentUser.roleDisplayName || this.auth.getUser()?.role || 'Content Moderator';
  }

  ngOnInit(): void {
    this.loadDashboard();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

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
          this.pendingReviewReportsCount = dashboard.badges.pendingReviewReportsCount;
          this.buildStats(dashboard.stats);
          this.cdr.markForCheck();
        },
        error: () => {
          this.dashboard = null;
          this.pendingReviewReportsCount = 0;
          this.stats = [];
          this.errorMessage = 'Unable to load content moderator dashboard data.';
          this.cdr.markForCheck();
        },
      });
  }

  openPendingReports(): void {
    this.navigateTo(this.dashboard?.quickLinks.pendingReportsRoute ?? '/content-moderator/review-management?status=reported');
  }

  navigateTo(route: string): void {
    this.router.navigateByUrl(route);
  }

  private buildStats(stats: ContentModeratorDashboardStats): void {
    const quickLinks = this.dashboard?.quickLinks;
    this.stats = [
      {
        label: 'Review Destinations',
        value: stats.destinationsCount,
        sub: 'Total destinations',
        color: '#2563eb',
        icon: 'flag',
        route: quickLinks?.destinationsReviewRoute ?? '/content-moderator/destinations',
      },
      {
        label: 'Review Locations',
        value: stats.locationsCount,
        sub: 'Total locations',
        color: '#0891b2',
        icon: 'pin',
        route: quickLinks?.locationsReviewRoute ?? '/content-moderator/locations',
      },
      {
        label: 'Review Places',
        value: stats.placesCount,
        sub: 'Total places',
        color: '#10b981',
        icon: 'home',
        route: quickLinks?.placesReviewRoute ?? '/content-moderator/places',
      },
      {
        label: 'Review Events',
        value: stats.eventsCount,
        sub: 'Total events',
        color: '#f59e0b',
        icon: 'calendar',
        route: quickLinks?.eventsReviewRoute ?? '/content-moderator/events',
      },
    ];
  }
}
