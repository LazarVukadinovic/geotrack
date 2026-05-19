import { ChangeDetectorRef, Component, EventEmitter, Input, OnInit, Output, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { filter, take } from 'rxjs/operators';
import { AuthService } from '../../../core/services/auth.service';
import { RegionalAssignedRegion } from '../../../core/models/dashboard.model';
import { TerritoryDashboardService } from '../../../core/services/territory-dashboard.service';

export interface SidebarNavItem {
  label: string;
  path: string;
  icon: string;
  exact?: boolean;
  badge?: number;
}

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './sidebar.component.html',
  styleUrl: './sidebar.component.css',
})
export class SidebarComponent implements OnInit {
  @Input() region = '';
  @Input() regions: RegionalAssignedRegion[] = [];
  @Input() activeRegionId: number | null = null;
  @Input() pendingRequestsCount: number | null = null;
  @Input() regionLoading = false;
  @Output() regionChange = new EventEmitter<number>();

  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly territoryDashboard = inject(TerritoryDashboardService);
  private readonly cdr = inject(ChangeDetectorRef);

  private readonly currentUrl = signal(this.router.url);
  private loadedTerritories: RegionalAssignedRegion[] = [];
  private loadedActiveTerritoryId: number | null = null;
  private territoryLoading = false;

  constructor() {
    this.router.events
      .pipe(filter(e => e instanceof NavigationEnd))
      .subscribe(e => this.currentUrl.set((e as NavigationEnd).urlAfterRedirects));
  }

  ngOnInit(): void {
    if (!this.auth.isTerritoryManager() || this.regions.length > 0) {
      return;
    }

    this.territoryLoading = true;
    this.territoryDashboard.getAssignedTerritories()
      .pipe(take(1))
      .subscribe({
        next: territories => {
          this.loadedTerritories = territories.map(territory => ({
            id: territory.id,
            name: territory.name,
            displayName: territory.displayName,
            statusLabel: territory.statusLabel,
          }));
          const storedId = this.territoryDashboard.getActiveTerritoryId();
          this.loadedActiveTerritoryId = storedId && this.loadedTerritories.some(item => item.id === storedId)
            ? storedId
            : this.loadedTerritories[0]?.id ?? null;
          if (this.loadedActiveTerritoryId) {
            this.territoryDashboard.setActiveTerritoryId(this.loadedActiveTerritoryId);
          }
          this.territoryLoading = false;
          this.cdr.markForCheck();
        },
        error: () => {
          this.territoryLoading = false;
          this.cdr.markForCheck();
        },
      });
  }

  isActive(path: string, exact = false): boolean {
    const url = this.currentUrl();
    if (exact) return url === path;
    return url === path || url.startsWith(path + '/') || url.startsWith(path + '?');
  }

  get pickerLabel(): string {
    return this.auth.isTerritoryManager() ? 'TERRITORY' : 'REGION';
  }

  get pickerValue(): string {
    return this.resolvedRegionLabel() ?? this.emptyPickerLabel;
  }

  get pickerDisplayValue(): string {
    const label = this.resolvedRegionLabel();
    return this.isPickerLoading && !label ? 'Loading...' : (label ?? this.emptyPickerLabel);
  }

  get canSelectRegion(): boolean {
    return this.pickerItems.length > 1;
  }

  get isTerritory(): boolean {
    return this.auth.isTerritoryManager();
  }

  get territoryMainNav(): SidebarNavItem[] {
    return [
      { label: 'Territory Overview', path: '/territory/overview', icon: 'dashboard', exact: true },
      { label: 'Regional Management', path: '/territory/regions', icon: 'map' },
      { label: 'User Administration', path: '/territory/users', icon: 'users' },
    ];
  }

  get territoryAnalyticsNav(): SidebarNavItem[] {
    return [
      { label: 'Analytics & Insights', path: '/territory/analytics', icon: 'chart' },
    ];
  }

  get mainNav(): SidebarNavItem[] {
    return [
      { label: 'Dashboard', path: '/dashboard', icon: 'dashboard', exact: true },
      {
        label: 'Requests Queue',
        path: '/requests',
        icon: 'inbox',
        badge: this.pendingRequestsCount || undefined,
      },
      { label: 'Map', path: '/map', icon: 'map' },
    ];
  }

  contentNav: SidebarNavItem[] = [
    { label: 'Destinations', path: '/destinations', icon: 'flag' },
    { label: 'Locations', path: '/locations', icon: 'pin' },
    { label: 'Places', path: '/places', icon: 'home' },
    { label: 'Events', path: '/events', icon: 'calendar' },
  ];

  analyticsNav: SidebarNavItem[] = [
    { label: 'Analytics & Insights', path: '/analytics', icon: 'chart' },
  ];

  signOut(): void {
    this.auth.logout();
    this.router.navigate(['/login']);
  }

  onRegionSelectChange(event: Event): void {
    const select = event.target instanceof HTMLSelectElement ? event.target : null;
    const regionId = Number(select?.value);
    if (Number.isFinite(regionId) && regionId > 0) {
      if (this.isTerritory) {
        this.loadedActiveTerritoryId = regionId;
        this.territoryDashboard.setActiveTerritoryId(regionId);
      }
      this.regionChange.emit(regionId);
    }
  }

  private resolvedRegionLabel(): string | null {
    const activeRegion = this.pickerItems.find((region) => region.id === this.resolvedActiveId);
    const value = this.region || activeRegion?.displayName || this.auth.getUser()?.region || '';
    return value.trim() || null;
  }

  get pickerItems(): RegionalAssignedRegion[] {
    return this.regions.length > 0 ? this.regions : this.loadedTerritories;
  }

  get resolvedActiveId(): number | null {
    return this.activeRegionId ?? this.loadedActiveTerritoryId;
  }

  get isPickerLoading(): boolean {
    return this.regionLoading || this.territoryLoading;
  }

  private get emptyPickerLabel(): string {
    return this.isTerritory ? 'No assigned territories' : 'No assigned regions';
  }
}
