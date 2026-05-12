import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { DashboardService } from '../../../../core/services/dashboard.service';
import { AuthService } from '../../../../core/services/auth.service';
import { RegionalAssignedRegion, RegionalDashboardActivityItem } from '../../../../core/models/dashboard.model';

@Component({
  selector: 'app-activity-history',
  standalone: true,
  imports: [CommonModule, SidebarComponent, TopbarComponent],
  templateUrl: './activity-history.component.html',
  styleUrl: './activity-history.component.css',
})
export class ActivityHistoryComponent implements OnInit {
  private static readonly activeRegionStorageKey = 'geotrack_admin_active_region_id';

  regions: RegionalAssignedRegion[] = [];
  activeRegionId: number | null = null;
  items: RegionalDashboardActivityItem[] = [];
  skip = 0;
  take = 20;
  hasMore = false;
  loading = false;
  errorMessage: string | null = null;

  constructor(
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly dashboardService: DashboardService,
    private readonly auth: AuthService,
  ) {}

  ngOnInit(): void {
    this.dashboardService.getAssignedRegions().subscribe({
      next: regions => {
        this.regions = regions;
        if (regions.length === 0) {
          this.errorMessage = 'No assigned regions are available for activity history.';
          return;
        }

        const queryRegionId = Number(this.route.snapshot.queryParamMap.get('regionId'));
        const persistedRegionId = this.readPersistedRegionId();
        const selected = regions.find(region => region.id === queryRegionId)
          ?? regions.find(region => region.id === persistedRegionId)
          ?? regions[0];

        this.setActiveRegion(selected.id);
        this.loadActivity(true);
      },
      error: () => {
        this.errorMessage = 'Activity history could not be loaded.';
      },
    });
  }

  get userName(): string {
    return this.auth.fullName;
  }

  get userRole(): string {
    return this.auth.getUser()?.role ?? '';
  }

  get regionDisplayName(): string {
    return this.regions.find(region => region.id === this.activeRegionId)?.displayName ?? '';
  }

  onRegionSelected(regionId: number): void {
    if (regionId === this.activeRegionId) {
      return;
    }

    this.setActiveRegion(regionId);
    this.loadActivity(true);
  }

  loadMore(): void {
    this.loadActivity(false);
  }

  backToDashboard(): void {
    this.router.navigate(['/dashboard'], { queryParams: this.activeRegionId ? { regionId: this.activeRegionId } : undefined });
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

    return new Date(value).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' });
  }

  private loadActivity(reset: boolean): void {
    if (!this.activeRegionId) {
      return;
    }

    this.loading = true;
    this.errorMessage = null;
    const nextSkip = reset ? 0 : this.skip;

    this.dashboardService.getActivity(this.activeRegionId, nextSkip, this.take).subscribe({
      next: response => {
        this.items = reset ? response.items : [...this.items, ...response.items];
        this.skip = response.skip + response.items.length;
        this.hasMore = response.hasMore;
        this.loading = false;
      },
      error: () => {
        this.errorMessage = 'Activity history could not be loaded.';
        this.loading = false;
      },
    });
  }

  private setActiveRegion(regionId: number): void {
    this.activeRegionId = regionId;
    try {
      sessionStorage.setItem(ActivityHistoryComponent.activeRegionStorageKey, String(regionId));
    } catch {
      // Active region persistence is only a session convenience.
    }
  }

  private readPersistedRegionId(): number | null {
    try {
      const value = sessionStorage.getItem(ActivityHistoryComponent.activeRegionStorageKey);
      const parsed = value ? Number(value) : NaN;
      return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
    } catch {
      return null;
    }
  }
}
