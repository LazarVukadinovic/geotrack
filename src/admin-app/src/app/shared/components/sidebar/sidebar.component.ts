import { Component, Input, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, Router, NavigationEnd } from '@angular/router';
import { filter } from 'rxjs/operators';
import { ReviewRequestsService } from '../../../core/services/review-requests.service';
import { AuthService } from '../../../core/services/auth.service';

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

  private readonly auth            = inject(AuthService);
  private readonly requestsService = inject(ReviewRequestsService);
  private readonly router          = inject(Router);

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
    return this.auth.isNationalManager() ? 'COUNTRY' : 'REGION';
  }

  get pickerValue(): string {
    return this.region || this.auth.getUser()?.region || '';
  }

  get isNational(): boolean {
    return this.auth.isNationalManager();
  }

  get nationalMainNav(): SidebarNavItem[] {
    return [
      { label: 'National Overview',   path: '/national-dashboard',  icon: 'dashboard', exact: true },
      { label: 'Regional Management', path: '/regions',             icon: 'map'                    },
      { label: 'User Administration', path: '/user-administration', icon: 'users'                  },
    ];
  }

  get nationalAnalyticsNav(): SidebarNavItem[] {
    return [
      { label: 'Analytics & Insights', path: '/national-analytics', icon: 'chart' },
    ];
  }

  get mainNav(): SidebarNavItem[] {
    return [
      { label: 'Dashboard',      path: '/dashboard', icon: 'dashboard', exact: true },
      { label: 'Requests Queue', path: '/requests',  icon: 'inbox',
        badge: this.requestsService.pendingCount || undefined },
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
}