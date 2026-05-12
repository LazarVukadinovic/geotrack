import { Component, EventEmitter, Input, Output, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, RouterLinkActive, Router } from '@angular/router';
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
  imports: [CommonModule, RouterLink, RouterLinkActive],
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

  private readonly auth            = inject(AuthService);
  private readonly router          = inject(Router);

  get pickerLabel(): string {
    return this.auth.isNationalManager() ? 'COUNTRY' : 'REGION';
  }

  get pickerValue(): string {
    const activeRegion = this.regions.find(region => region.id === this.activeRegionId);
    return this.region || activeRegion?.displayName || this.auth.getUser()?.region || 'No assigned regions';
  }

  get canSelectRegion(): boolean {
    return this.regions.length > 1;
  }

  get isNational(): boolean {
    return this.auth.isNationalManager();
  }

  get nationalMainNav(): SidebarNavItem[] {
    return [
      { label: 'National Overview',    path: '/national-overview',    icon: 'dashboard',  exact: true },
      { label: 'Regional Management',  path: '/regional-management',  icon: 'map'                     },
      { label: 'Content Directory',    path: '/content-directory',    icon: 'folder'                  },
      { label: 'User Administration',  path: '/user-administration',  icon: 'users'                   },
    ];
  }

  get nationalAnalyticsNav(): SidebarNavItem[] {
    return [
      { label: 'Analytics & Insights', path: '/analytics',           icon: 'chart' },
      { label: 'National Analytics',   path: '/national-analytics',  icon: 'globe' },
    ];
  }

  get mainNav(): SidebarNavItem[] {
    return [
      { label: 'Dashboard',      path: '/dashboard', icon: 'dashboard', exact: true },
      { label: 'Requests Queue', path: '/requests',  icon: 'inbox',
        badge: this.pendingRequestsCount || undefined },
      { label: 'Map',            path: '/map',       icon: 'map' },
    ];
  }

  contentNav: SidebarNavItem[] = [
    { label: 'Destinations', path: '/destinations', icon: 'flag'     },
    { label: 'Locations',    path: '/locations',    icon: 'pin'      },
    { label: 'Places',       path: '/places',       icon: 'home'     },
    { label: 'Events',       path: '/events',       icon: 'calendar' },
  ];

  analyticsNav: SidebarNavItem[] = [
    { label: 'Analytics & Insights', path: '/analytics', icon: 'chart' },
  ];

  signOut(): void {
    this.auth.logout();
    this.router.navigate(['/login']);
  }

  onRegionChange(value: string): void {
    const regionId = Number(value);
    if (Number.isFinite(regionId) && regionId > 0) {
      this.regionChange.emit(regionId);
    }
  }
}
