import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { finalize } from 'rxjs';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { DashboardService } from '../../../../core/services/dashboard.service';
import { AuthService } from '../../../../core/services/auth.service';
import {
  DashboardStat,
  QuickAction,
  RegionalAssignedRegion,
  RegionalDashboardResponse,
  RegionalDashboardActivityItem,
  RegionalRequestQueuePreviewItem,
} from '../../../../core/models/dashboard.model';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, SidebarComponent, TopbarComponent],
  templateUrl: './dashboard-page.component.html',
  styleUrl: './dashboard-page.component.css',
})
export class DashboardComponent implements OnInit {
  private static readonly activeRegionStorageKey = 'geotrack_admin_active_region_id';

  assignedRegions: RegionalAssignedRegion[] = [];
  activeRegionId: number | null = null;
  dashboard: RegionalDashboardResponse | null = null;
  regionLoading = false;
  dashboardLoading = false;
  errorMessage: string | null = null;
  authRequired = false;

  constructor(
    private readonly router: Router,
    private readonly auth: AuthService,
    private readonly dashboardService: DashboardService,
    private readonly cdr: ChangeDetectorRef,
  ) {}

  ngOnInit(): void {
    this.loadAssignedRegions();
  }

  get userName(): string {
    return this.auth.fullName;
  }

  get userRole(): string {
    return this.auth.getUser()?.role ?? '';
  }

  get activeRegion(): RegionalAssignedRegion | null {
    return (
      this.dashboard?.activeRegion ??
      this.assignedRegions.find((region) => region.id === this.activeRegionId) ??
      null
    );
  }

  get regionDisplayName(): string {
    return this.activeRegion?.displayName ?? '';
  }

  get pageDescription(): string {
    return this.activeRegion
      ? `Manage content and operations for ${this.activeRegion.displayName}.`
      : 'Select an assigned region to manage content and operations.';
  }

  get hasNoAssignedRegions(): boolean {
    return (
      !this.regionLoading &&
      this.assignedRegions.length === 0 &&
      !this.activeRegion &&
      !this.errorMessage
    );
  }

  get pendingRequestsCount(): number | null {
    return this.dashboard?.stats.pendingRequests ?? null;
  }

  get activities(): RegionalDashboardActivityItem[] {
    return this.dashboard?.recentActivity ?? [];
  }

  get quickActions(): QuickAction[] {
    return this.dashboardService.getQuickActions();
  }

  get stats(): DashboardStat[] {
    return this.dashboard ? this.dashboardService.buildStats(this.dashboard.stats) : [];
  }

  get queue(): RegionalRequestQueuePreviewItem[] {
    return this.dashboard?.requestQueuePreview ?? [];
  }

  loadAssignedRegions(): void {
    this.regionLoading = true;
    this.errorMessage = null;
    this.authRequired = false;
    this.notifyStateChanged();

    this.dashboardService
      .getAssignedRegions()
      .pipe(
        finalize(() => {
          this.regionLoading = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: (regions) => {
          this.assignedRegions = regions ?? [];

          if (this.assignedRegions.length === 0) {
            this.activeRegionId = null;
            this.dashboard = null;
            this.dashboardLoading = false;
            this.clearPersistedRegion();
            return;
          }

          const persistedRegionId = this.readPersistedRegionId();
          const selectedRegion =
            this.assignedRegions.find((region) => region.id === persistedRegionId) ??
            this.assignedRegions[0];
          this.setActiveRegion(selectedRegion.id, false);
          this.loadDashboard(selectedRegion.id);
        },
        error: (error) => {
          this.dashboard = null;
          this.dashboardLoading = false;
          this.handleLoadError(error);
        },
      });
  }

  onRegionSelected(regionId: number): void {
    if (regionId === this.activeRegionId) {
      return;
    }

    this.setActiveRegion(regionId, true);
  }

  statusClass(status: string | null | undefined): string {
    switch ((status ?? '').toLowerCase()) {
      case 'pendingreview':
      case 'pending':
        return 'status-pending';
      case 'approved':
        return 'status-approved';
      case 'rejected':
        return 'status-rejected';
      case 'created':
        return 'status-created';
      default:
        return '';
    }
  }

  typeIcon(type: string): string {
    if (type === 'place') return 'pin';
    if (type === 'destination') return 'map';
    if (type === 'location') return 'pin';
    if (type === 'event') return 'calendar';
    return 'inbox';
  }

  navigate(route: string): void {
    this.router.navigateByUrl(this.withActiveRegion(route));
  }

  navigateStat(stat: DashboardStat): void {
    this.navigate(stat.route);
  }

  navigateToQueue(): void {
    this.navigate('/requests');
  }

  navigateToRequest(item: RegionalRequestQueuePreviewItem): void {
    if (item.detailRoute) {
      this.navigate(item.detailRoute);
      return;
    }

    const routeSource = item.source === 'ReviewReport' ? 'review-report' : 'contribution';
    this.navigate(`/request/detail/${routeSource}/${item.requestId}`);
  }

  navigateToActivityHistory(): void {
    if (!this.activeRegionId) {
      return;
    }

    this.navigate('/activity-history');
  }

  queueImageStyle(item: RegionalRequestQueuePreviewItem): string {
    if (item.thumbnailImageUrl) {
      return `url("${item.thumbnailImageUrl}") center/cover`;
    }

    return this.fallbackGradient(item.requestId);
  }

  activityInitials(item: RegionalDashboardActivityItem): string {
    return item.actorInitials || item.action.slice(0, 2).toUpperCase();
  }

  activityAvatarColor(item: RegionalDashboardActivityItem): string {
    return this.fallbackColor(this.hashString(item.id));
  }

  timeAgo(value: string): string {
    const timestamp = new Date(value).getTime();
    if (Number.isNaN(timestamp)) {
      return '';
    }

    const diffSeconds = Math.max(0, Math.floor((Date.now() - timestamp) / 1000));
    if (diffSeconds < 60) return 'Just now';

    const diffMinutes = Math.floor(diffSeconds / 60);
    if (diffMinutes < 60) return `${diffMinutes} min${diffMinutes === 1 ? '' : 's'} ago`;

    const diffHours = Math.floor(diffMinutes / 60);
    if (diffHours < 24) return `${diffHours} hour${diffHours === 1 ? '' : 's'} ago`;

    const diffDays = Math.floor(diffHours / 24);
    if (diffDays < 7) return `${diffDays} day${diffDays === 1 ? '' : 's'} ago`;

    return new Date(value).toLocaleDateString(undefined, {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
    });
  }

  private setActiveRegion(regionId: number, reload: boolean): void {
    this.activeRegionId = regionId;
    this.persistRegion(regionId);

    if (reload) {
      this.loadDashboard(regionId);
    }
  }

  private loadDashboard(regionId: number): void {
    this.dashboardLoading = true;
    this.dashboard = null;
    this.errorMessage = null;
    this.authRequired = false;
    this.notifyStateChanged();

    this.dashboardService
      .getDashboard(regionId)
      .pipe(
        finalize(() => {
          this.dashboardLoading = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: (dashboard) => {
          this.dashboard = this.normalizeDashboard(dashboard);
          this.setActiveRegion(this.dashboard.activeRegion.id, false);
          this.upsertAssignedRegion(this.dashboard.activeRegion);
        },
        error: (error) => {
          this.dashboard = null;
          this.handleLoadError(error);
        },
      });
  }

  private handleLoadError(error: unknown): void {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 401) {
        this.authRequired = true;
        this.errorMessage =
          'Sign in with an authenticated Regional Manager account to view the dashboard.';
        return;
      }

      if (error.status === 403) {
        this.errorMessage = 'This account is not authorized for the selected region.';
        return;
      }
    }

    this.errorMessage = 'Unable to load regional dashboard.';
  }

  private normalizeDashboard(dashboard: RegionalDashboardResponse): RegionalDashboardResponse {
    return {
      ...dashboard,
      requestQueuePreview: dashboard.requestQueuePreview ?? [],
      recentActivity: dashboard.recentActivity ?? [],
    };
  }

  private upsertAssignedRegion(region: RegionalAssignedRegion): void {
    const existingIndex = this.assignedRegions.findIndex((item) => item.id === region.id);
    if (existingIndex >= 0) {
      this.assignedRegions = this.assignedRegions.map((item) =>
        item.id === region.id ? region : item,
      );
      return;
    }

    this.assignedRegions = [region, ...this.assignedRegions];
  }

  private notifyStateChanged(): void {
    this.cdr.markForCheck();
  }

  private withActiveRegion(route: string): string {
    if (!this.activeRegionId) {
      return route;
    }

    const separator = route.includes('?') ? '&' : '?';
    return `${route}${separator}regionId=${this.activeRegionId}`;
  }

  private readPersistedRegionId(): number | null {
    try {
      const value = sessionStorage.getItem(DashboardComponent.activeRegionStorageKey);
      const parsed = value ? Number(value) : NaN;
      return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
    } catch {
      return null;
    }
  }

  private persistRegion(regionId: number): void {
    try {
      sessionStorage.setItem(DashboardComponent.activeRegionStorageKey, String(regionId));
    } catch {
      // Active region persistence is only a session convenience.
    }
  }

  private clearPersistedRegion(): void {
    try {
      sessionStorage.removeItem(DashboardComponent.activeRegionStorageKey);
    } catch {
      // Active region persistence is only a session convenience.
    }
  }

  private fallbackGradient(seed: number): string {
    const color = this.fallbackColor(seed);
    const darker = this.fallbackColor(seed + 3);
    return `linear-gradient(135deg, ${color} 0%, ${darker} 100%)`;
  }

  private fallbackColor(seed: number): string {
    const colors = ['#0ea5e9', '#f97316', '#10b981', '#8b5cf6', '#e11d48', '#0891b2', '#2563eb'];
    return colors[Math.abs(seed) % colors.length];
  }

  private hashString(value: string): number {
    return value.split('').reduce((hash, char) => (hash << 5) - hash + char.charCodeAt(0), 0);
  }
}
