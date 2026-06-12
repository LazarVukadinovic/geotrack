import { Component, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { finalize } from 'rxjs';
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
export class ActivityHistoryComponent {
  private static readonly activeRegionStorageKey = 'geotrack_admin_active_region_id';

  readonly regions = signal<RegionalAssignedRegion[]>([]);
  readonly activeRegionId = signal<number | null>(null);
  readonly items = signal<RegionalDashboardActivityItem[]>([]);
  readonly skip = signal(0);
  readonly take = 20;
  readonly hasMore = signal(false);
  readonly loading = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly regionDisplayName = computed(() => {
    const activeRegionId = this.activeRegionId();

    return this.regions().find(region => region.id === activeRegionId)?.displayName ?? '';
  });

  constructor(
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly dashboardService: DashboardService,
    private readonly auth: AuthService,
  ) {
    this.loadAssignedRegions();
  }

  get userName(): string {
    return this.auth.fullName;
  }

  get userRole(): string {
    return this.auth.getUser()?.role ?? '';
  }

  onRegionSelected(regionId: number): void {
    if (regionId === this.activeRegionId()) {
      return;
    }

    this.setActiveRegion(regionId);
    this.loadActivity(true);
  }

  loadMore(): void {
    if (this.loading() || !this.hasMore()) {
      return;
    }

    this.loadActivity(false);
  }

  backToDashboard(): void {
    const activeRegionId = this.activeRegionId();

    this.router.navigate(['/dashboard'], {
      queryParams: activeRegionId ? { regionId: activeRegionId } : undefined,
    });
  }

  timeAgo(value: string): string {
    const timestamp = new Date(value).getTime();

    if (Number.isNaN(timestamp)) {
      return '';
    }

    const diffSeconds = Math.max(0, Math.floor((Date.now() - timestamp) / 1000));

    if (diffSeconds < 60) {
      return 'Just now';
    }

    const diffMinutes = Math.floor(diffSeconds / 60);

    if (diffMinutes < 60) {
      return `${diffMinutes} min${diffMinutes === 1 ? '' : 's'} ago`;
    }

    const diffHours = Math.floor(diffMinutes / 60);

    if (diffHours < 24) {
      return `${diffHours} hour${diffHours === 1 ? '' : 's'} ago`;
    }

    return new Date(value).toLocaleDateString(undefined, {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
    });
  }

  private loadAssignedRegions(): void {
    this.dashboardService.getAssignedRegions().subscribe({
      next: regions => {
        const assignedRegions = regions ?? [];

        this.regions.set(assignedRegions);

        if (assignedRegions.length === 0) {
          this.errorMessage.set('No assigned regions are available for activity history.');
          return;
        }

        const queryRegionId = Number(this.route.snapshot.queryParamMap.get('regionId'));
        const persistedRegionId = this.readPersistedRegionId();

        const selected =
          assignedRegions.find(region => region.id === queryRegionId)
          ?? assignedRegions.find(region => region.id === persistedRegionId)
          ?? assignedRegions[0];

        this.setActiveRegion(selected.id);
        this.loadActivity(true);
      },
      error: () => {
        this.errorMessage.set('Activity history could not be loaded.');
      },
    });
  }

  private loadActivity(reset: boolean): void {
    const activeRegionId = this.activeRegionId();

    if (!activeRegionId) {
      return;
    }

    this.loading.set(true);
    this.errorMessage.set(null);

    if (reset) {
      this.items.set([]);
      this.skip.set(0);
      this.hasMore.set(false);
    }

    const nextSkip = reset ? 0 : this.skip();

    this.dashboardService
      .getActivity(activeRegionId, nextSkip, this.take)
      .pipe(
        finalize(() => {
          this.loading.set(false);
        }),
      )
      .subscribe({
        next: response => {
          const responseItems = response.items ?? [];

          this.items.set(reset ? responseItems : [...this.items(), ...responseItems]);
          this.skip.set(response.skip + responseItems.length);
          this.hasMore.set(response.hasMore);
        },
        error: () => {
          this.errorMessage.set('Activity history could not be loaded.');
          this.hasMore.set(false);
        },
      });
  }

  private setActiveRegion(regionId: number): void {
    this.activeRegionId.set(regionId);

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