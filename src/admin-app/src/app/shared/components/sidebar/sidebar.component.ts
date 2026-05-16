import { Component, EventEmitter, Input, Output, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { filter } from 'rxjs/operators';
import { AuthService } from '../../../core/services/auth.service';
import { RegionalAssignedRegion } from '../../../core/models/dashboard.model';

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
export class SidebarComponent {
  @Input() region = '';
  @Input() regions: RegionalAssignedRegion[] = [];
  @Input() activeRegionId: number | null = null;
  @Input() pendingRequestsCount: number | null = null;
  @Input() regionLoading = false;
  @Output() regionChange = new EventEmitter<number>();

  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  private readonly currentUrl = signal(this.router.url);

  constructor() {
    this.router.events
      .pipe(filter(e => e instanceof NavigationEnd))
      .subscribe(e => this.currentUrl.set((e as NavigationEnd).urlAfterRedirects));
  }

  isActive(path: string, exact = false): boolean {
    const url = this.currentUrl();
    if (exact) return url === path;
    return url === path || url.startsWith(path + '/') || url.startsWith(path + '?');
  }

  get pickerLabel(): string {
    if (this.auth.isAdministrator()) return 'SYSTEM';
    return this.auth.isNationalManager() ? 'COUNTRY' : 'REGION';
  }

  get pickerValue(): string {
    return this.resolvedRegionLabel() ?? 'No assigned regions';
  }

  get pickerDisplayValue(): string {
    const label = this.resolvedRegionLabel();
    return this.regionLoading && !label ? 'Loading...' : (label ?? 'No assigned regions');
  }

  get canSelectRegion(): boolean {
    return this.regions.length > 1;
  }

  get isAdministrator(): boolean {
    return this.auth.isAdministrator();
  }

  get isNational(): boolean {
    return this.auth.isNationalManager();
  }

  @Input() reportedReviewsCount: number | null = null;

  get administratorMainNav(): SidebarNavItem[] {
    return [
      { label: 'Admin Dashboard', path: '/admin-dashboard', icon: 'dashboard', exact: true },
      { label: 'Territory Management', path: '/admin-territories', icon: 'globe' },
      { label: 'User Management', path: '/admin-users', icon: 'users' },
      {
        label: 'Review Management',
        path: '/admin-reviews',
        icon: 'review',
        badge: this.reportedReviewsCount || undefined,
      },
    ];
  }

  get nationalMainNav(): SidebarNavItem[] {
    return [
      { label: 'National Overview', path: '/national-dashboard', icon: 'dashboard', exact: true },
      { label: 'Regional Management', path: '/regions', icon: 'map' },
      { label: 'User Administration', path: '/user-administration', icon: 'users' },
    ];
  }

  get nationalAnalyticsNav(): SidebarNavItem[] {
    return [
      { label: 'Analytics & Insights', path: '/national-analytics', icon: 'chart' },
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
      this.regionChange.emit(regionId);
    }
  }

  private resolvedRegionLabel(): string | null {
    const activeRegion = this.regions.find((region) => region.id === this.activeRegionId);
    const value = this.region || activeRegion?.displayName || this.auth.getUser()?.region || '';
    return value.trim() || null;
  }
}
