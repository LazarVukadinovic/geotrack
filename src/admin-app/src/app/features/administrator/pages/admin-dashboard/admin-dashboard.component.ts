import { Component, OnInit, OnDestroy, ChangeDetectionStrategy, ChangeDetectorRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';

import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { AuthService } from '../../../../core/services/auth.service';
import { TerritoryService } from '../../../../core/services/administrator/territory.service';
import { AppUsersService } from '../../../../core/services/administrator/app-users.service';
import { AdminReviewsService } from '../../../../core/services/administrator/admin-reviews.service';
import { ReviewNotification } from '../../../../core/models/administrator/admin-review.model';

@Component({
  selector: 'app-admin-dashboard',
  standalone: true,
  imports: [CommonModule, SidebarComponent, TopbarComponent],
  templateUrl: './admin-dashboard.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminDashboardComponent implements OnInit, OnDestroy {
  private readonly auth    = inject(AuthService);
  private readonly terrSvc = inject(TerritoryService);
  private readonly userSvc = inject(AppUsersService);
  private readonly revSvc  = inject(AdminReviewsService);
  private readonly cdr     = inject(ChangeDetectorRef);
  private readonly router  = inject(Router);

  private destroy$ = new Subject<void>();

  get userName(): string { return this.auth.fullName; }
  get userRole(): string { return this.auth.getUser()?.role ?? ''; }

  totalTerritories = 0;
  assignedTerritories = 0;
  totalAppUsers = 0;
  suspendedUsers = 0;
  totalReviews = 0;
  reportedReviews = 0;
  notifications: ReviewNotification[] = [];
  unreadCount = 0;
  showNotifications = false;

  stats: { label: string; value: number; sub: string; color: string; icon: string; route: string }[] = [];

  ngOnInit(): void {
    this.terrSvc.getPage$({ page: 1, pageSize: 100, search: '', sortField: 'createdAt', sortDir: 'desc', countryFilter: [] }).pipe(takeUntil(this.destroy$)).subscribe(page => {
      this.totalTerritories   = page.total;
      this.assignedTerritories = page.items.filter(t => t.status === 'Active').length;
      this.buildStats();
      this.cdr.detectChanges();
    });

    this.totalAppUsers    = this.userSvc.getTotalCount();
    this.suspendedUsers   = this.userSvc.getSuspendedCount();
    this.totalReviews     = this.revSvc.getTotalCount();
    this.reportedReviews  = this.revSvc.getReportedCount();
    this.notifications    = this.revSvc.getNotifications();
    this.unreadCount      = this.revSvc.getUnreadCount();

    this.revSvc.notifications$.pipe(takeUntil(this.destroy$)).subscribe(notifs => {
      this.notifications = [...notifs].sort((a, b) => b.reportedAt.localeCompare(a.reportedAt));
      this.unreadCount   = notifs.filter(n => !n.isRead).length;
      this.cdr.detectChanges();
    });

    this.buildStats();
    this.cdr.detectChanges();
  }

  ngOnDestroy(): void { this.destroy$.next(); this.destroy$.complete(); }

  private buildStats(): void {
    this.stats = [
      { label: 'Territories', value: this.totalTerritories, sub: `${this.assignedTerritories} assigned`, color: '#3b82f6', icon: 'globe', route: '/admin-territories' },
      { label: 'App Users', value: this.totalAppUsers, sub: `${this.suspendedUsers} suspended`, color: '#10b981', icon: 'users', route: '/admin-users' },
      { label: 'Reviews', value: this.totalReviews, sub: `${this.reportedReviews} reported`, color: '#8b5cf6', icon: 'review', route: '/admin-reviews' },
      { label: 'Pending Reports', value: this.reportedReviews, sub: 'Need attention', color: '#ef4444', icon: 'alert', route: '/admin-reviews' },
    ];
  }

  toggleNotifications(event: MouseEvent): void {
    event.stopPropagation();
    this.showNotifications = !this.showNotifications;
    if (this.showNotifications && this.unreadCount > 0) {
      this.revSvc.markAllRead();
    }
    this.cdr.detectChanges();
  }

  closeNotifications(): void {
    this.showNotifications = false;
    this.cdr.detectChanges();
  }

  goToReview(reviewId: string): void {
    this.showNotifications = false;
    this.router.navigate(['/admin-reviews'], { queryParams: { highlight: reviewId } });
  }

  navigateTo(route: string): void {
    this.router.navigate([route]);
  }

  formatDatetime(iso: string): string {
    return this.revSvc.formatDatetime(iso);
  }

  targetTypeClass(type: string): string {
    const map: Record<string, string> = {
      Destination: 'bg-blue-50 text-blue-700 ring-1 ring-blue-200',
      Place:       'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200',
      Location:    'bg-violet-50 text-violet-700 ring-1 ring-violet-200',
      Event:       'bg-amber-50 text-amber-700 ring-1 ring-amber-200',
    };
    return map[type] ?? 'bg-gray-50 text-gray-600 ring-1 ring-gray-200';
  }
}
