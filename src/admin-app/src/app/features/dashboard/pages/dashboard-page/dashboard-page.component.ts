import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { ReviewRequestsService } from '../../../../core/services/review-requests.service';
import { DashboardService } from '../../../../core/services/dashboard.service';
import { Activity, DashboardStat, QuickAction } from '../../../../core/models/dashboard.model';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, SidebarComponent, TopbarComponent],
  templateUrl: './dashboard-page.component.html',
  styleUrl: './dashboard-page.component.css'
})
export class DashboardComponent {
  constructor(
    private router: Router,
    public requestsService: ReviewRequestsService,
    private dashboardService: DashboardService,
  ) {}

  region   = 'Šumadija, Srbija';
  userName = 'Sarah Jenkins';
  userRole = 'Regional Manager';

  get activities(): Activity[] {
    return this.dashboardService.getActivities();
  }

  get quickActions(): QuickAction[] {
    return this.dashboardService.getQuickActions();
  }

  get stats(): DashboardStat[] {
    return this.dashboardService.getStats(this.requestsService.pendingCount);
  }

  get queue() {
    return this.requestsService.pendingRequests.slice(0, 4);
  }

  statusClass(status: string | null): string {
    if (status === 'PENDING')   return 'status-pending';
    if (status === 'PUBLISHED') return 'status-published';
    if (status === 'APPROVED')  return 'status-approved';
    return '';
  }

  typeIcon(type: string): string {
    if (type === 'place')       return 'pin';
    if (type === 'destination') return 'map';
    if (type === 'event')       return 'calendar';
    return 'inbox';
  }

  navigate(route: string): void {
    this.router.navigate([route]);
  }

  navigateToQueue(): void {
    this.router.navigate(['/requests']);
  }
}