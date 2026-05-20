import { ChangeDetectionStrategy, ChangeDetectorRef, Component, OnInit, inject } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { Router } from '@angular/router';
import { finalize } from 'rxjs/operators';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { AuthService } from '../../../../core/services/auth.service';
import {
  TerritoryAssignedTerritory,
  TerritoryDashboardResponse,
  TerritoryRegionHealth,
  TerritoryRegionManager,
  TerritoryTopContentItem,
  TerritoryTopPerformingRegion,
} from '../../../../core/models/dashboard.model';
import { TerritoryDashboardService } from '../../../../core/services/territory-dashboard.service';

interface TerritoryStatCard {
  label: string;
  value: string;
  changeText: string;
  up: boolean;
  icon: 'globe' | 'users' | 'flag' | 'pin' | 'home' | 'calendar';
  route: string;
  queryParams?: Record<string, string>;
}

@Component({
  selector: 'app-national-dashboard',
  standalone: true,
  imports: [CommonModule, DatePipe, SidebarComponent, TopbarComponent],
  templateUrl: './national-dashboard.component.html',
  styleUrl: './national-dashboard.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NationalDashboardComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly territoryDashboard = inject(TerritoryDashboardService);
  private readonly router = inject(Router);
  private readonly cdr = inject(ChangeDetectorRef);

  assignedTerritories: TerritoryAssignedTerritory[] = [];
  activeTerritory: TerritoryAssignedTerritory | null = null;
  activeTerritoryId: number | null = null;
  dashboard: TerritoryDashboardResponse | null = null;
  stats: TerritoryStatCard[] = [];
  isLoading = false;
  isTerritoryLoading = false;
  errorMessage = '';
  private failedManagerAvatarKeys = new Set<string>();

  get userName(): string { return this.auth.fullName; }
  get userRole(): string { return this.auth.getUser()?.role ?? ''; }

  get regions(): TerritoryRegionHealth[] {
    return this.dashboard?.regionsHealth ?? [];
  }

  get topRegions(): TerritoryTopPerformingRegion[] {
    return this.dashboard?.topPerformingRegions ?? [];
  }

  get topDestinations(): TerritoryTopContentItem[] {
    return this.dashboard?.topContent.topDestinations ?? [];
  }

  get topLocations(): TerritoryTopContentItem[] {
    return this.dashboard?.topContent.topLocations ?? [];
  }

  get topPlaces(): TerritoryTopContentItem[] {
    return this.dashboard?.topContent.topPlaces ?? [];
  }

  get upcomingEvents(): TerritoryTopContentItem[] {
    return this.dashboard?.topContent.upcomingEvents ?? [];
  }

  ngOnInit(): void {
    this.loadTerritories();
  }

  onTerritoryChange(territoryId: number): void {
    const territory = this.assignedTerritories.find(item => item.id === territoryId) ?? null;
    this.activeTerritory = territory;
    this.activeTerritoryId = territory?.id ?? territoryId;
    this.territoryDashboard.setActiveTerritoryId(this.activeTerritoryId);
    this.loadDashboard(this.activeTerritoryId);
  }

  navigateToUsers(): void {
    this.router.navigate(['/territory/users']);
  }

  navigateToAnalytics(): void {
    this.router.navigate(['/territory/analytics']);
  }

  navigateToRegions(): void {
    this.router.navigate(['/territory/regions']);
  }

  openRegion(region: TerritoryRegionHealth): void {
    if (!Number.isFinite(region.regionId) || region.regionId <= 0) {
      console.warn('Unable to open region detail: missing region id.');
      return;
    }

    this.router.navigate(['/territory/regions', region.regionId], {
      state: { regionId: region.regionId, territoryId: this.activeTerritoryId },
    });
  }

  navigateStat(stat: TerritoryStatCard): void {
    this.router.navigate([stat.route], { queryParams: stat.queryParams });
  }

  visibleManagers(region: TerritoryRegionHealth): TerritoryRegionManager[] {
    return (region.assignedManagers ?? []).slice(0, 3);
  }

  hiddenManagerCount(region: TerritoryRegionHealth): number {
    return Math.max(0, (region.assignedManagers?.length ?? 0) - this.visibleManagers(region).length);
  }

  managerAvatarUrl(manager: TerritoryRegionManager): string {
    const avatarUrl = manager.avatarUrl?.trim() ?? '';
    return avatarUrl && !this.failedManagerAvatarKeys.has(this.managerAvatarKey(manager, avatarUrl))
      ? avatarUrl
      : '';
  }

  managerInitials(manager: TerritoryRegionManager): string {
    const initials = manager.initials?.trim();
    if (initials) return initials;

    return this.buildInitials(manager.displayName || manager.email);
  }

  managerAvatarAlt(manager: TerritoryRegionManager): string {
    const label = manager.displayName || manager.email || 'Regional Manager';
    return `${label} profile image`;
  }

  legacyManagerInitials(region: TerritoryRegionHealth): string[] {
    return (region.assignedManagers?.length ?? 0) > 0
      ? []
      : (region.assignedManagersPreview ?? []).slice(0, 3);
  }

  onManagerAvatarError(manager: TerritoryRegionManager): void {
    const avatarUrl = manager.avatarUrl?.trim() ?? '';
    if (avatarUrl) {
      this.failedManagerAvatarKeys.add(this.managerAvatarKey(manager, avatarUrl));
      this.cdr.markForCheck();
    }
  }

  formatCompact(value: number): string {
    return Intl.NumberFormat(undefined, { notation: 'compact', maximumFractionDigits: 1 }).format(value);
  }

  formatPercent(value: number): string {
    const prefix = value > 0 ? '+' : '';
    return `${prefix}${value}%`;
  }

  formatTopContentMetric(item: TerritoryTopContentItem): string {
    if (item.metricLabel === 'rating_x10') {
      return `${(item.metricValue / 10).toFixed(1)} rating`;
    }

    return `${this.formatCompact(item.metricValue)} ${item.metricLabel || 'views'}`;
  }

  private loadTerritories(): void {
    this.isTerritoryLoading = true;
    this.errorMessage = '';
    this.territoryDashboard.getAssignedTerritories()
      .pipe(finalize(() => {
        this.isTerritoryLoading = false;
        this.cdr.markForCheck();
      }))
      .subscribe({
        next: territories => {
          this.assignedTerritories = territories;
          const storedId = this.territoryDashboard.getActiveTerritoryId();
          this.activeTerritory = storedId
            ? territories.find(territory => territory.id === storedId) ?? territories[0] ?? null
            : territories[0] ?? null;
          this.activeTerritoryId = this.activeTerritory?.id ?? null;

          if (this.activeTerritoryId) {
            this.territoryDashboard.setActiveTerritoryId(this.activeTerritoryId);
            this.loadDashboard(this.activeTerritoryId);
          } else {
            this.dashboard = null;
            this.stats = [];
          }
        },
        error: () => {
          this.errorMessage = 'Unable to load assigned territories.';
          this.dashboard = null;
          this.stats = [];
        },
      });
  }

  private loadDashboard(territoryId: number): void {
    this.isLoading = true;
    this.errorMessage = '';
    this.territoryDashboard.getDashboard(territoryId)
      .pipe(finalize(() => {
        this.isLoading = false;
        this.cdr.markForCheck();
      }))
      .subscribe({
        next: dashboard => {
          this.dashboard = dashboard;
          this.failedManagerAvatarKeys.clear();
          this.activeTerritory = dashboard.activeTerritory ?? this.activeTerritory;
          this.activeTerritoryId = dashboard.activeTerritory?.id ?? territoryId;
          this.stats = this.buildStats(dashboard);
        },
        error: error => {
          this.dashboard = null;
          this.stats = [];
          this.errorMessage = error?.error?.message ?? 'Unable to load territory dashboard.';
        },
      });
  }

  private buildStats(dashboard: TerritoryDashboardResponse): TerritoryStatCard[] {
    const stats = dashboard.stats;

    return [
      {
        label: 'Total Regions',
        value: stats.totalRegions.toLocaleString(),
        changeText: this.formatCountDelta(stats.regionsAddedLastMonth),
        up: stats.regionsAddedLastMonth >= 0,
        icon: 'globe',
        route: '/territory/regions',
      },
      {
        label: 'Active Users',
        value: stats.activeUsers.toLocaleString(),
        changeText: `${this.formatPercent(stats.activeUsersChangePercent)} vs previous 30 days`,
        up: stats.activeUsersChangePercent >= 0,
        icon: 'users',
        route: '/territory/analytics',
      },
      {
        label: 'Total Destinations',
        value: stats.totalDestinations.toLocaleString(),
        changeText: this.formatCountDelta(stats.destinationsAddedLastMonth),
        up: stats.destinationsAddedLastMonth >= 0,
        icon: 'flag',
        route: '/territory/content',
        queryParams: { type: 'destinations' },
      },
      {
        label: 'Total Locations',
        value: stats.totalLocations.toLocaleString(),
        changeText: this.formatCountDelta(stats.locationsAddedLastMonth),
        up: stats.locationsAddedLastMonth >= 0,
        icon: 'pin',
        route: '/territory/content',
        queryParams: { type: 'locations' },
      },
      {
        label: 'Total Places',
        value: stats.totalPlaces.toLocaleString(),
        changeText: this.formatCountDelta(stats.placesAddedLastMonth),
        up: stats.placesAddedLastMonth >= 0,
        icon: 'home',
        route: '/territory/content',
        queryParams: { type: 'places' },
      },
      {
        label: 'Total Events',
        value: stats.totalEvents.toLocaleString(),
        changeText: this.formatCountDelta(stats.eventsAddedLastMonth),
        up: stats.eventsAddedLastMonth >= 0,
        icon: 'calendar',
        route: '/territory/content',
        queryParams: { type: 'events' },
      },
    ];
  }

  private formatCountDelta(value: number): string {
    const prefix = value > 0 ? '+' : '';
    return `${prefix}${value.toLocaleString()} in last 30 days`;
  }

  private managerAvatarKey(manager: TerritoryRegionManager, avatarUrl: string): string {
    return `${manager.userId}:${avatarUrl}`;
  }

  private buildInitials(value: string): string {
    const parts = value
      .trim()
      .split(/[\s._-]+/)
      .filter(Boolean);

    if (parts.length === 0) {
      return '?';
    }

    return parts
      .slice(0, 2)
      .map(part => part.charAt(0).toUpperCase())
      .join('');
  }
}
