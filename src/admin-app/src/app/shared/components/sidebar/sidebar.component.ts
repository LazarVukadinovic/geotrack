import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, RouterLinkActive } from '@angular/router';

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
  @Input() region = 'Šumadija, Srbija';

  mainNav: SidebarNavItem[] = [
    { label: 'Dashboard',      path: '/dashboard', icon: 'dashboard', exact: true },
    { label: 'Requests Queue', path: '/requests',  icon: 'inbox',     badge: 5   },
    { label: 'Map',            path: '/map',        icon: 'map'                   },
  ];

  contentNav: SidebarNavItem[] = [
    { label: 'Destinations', path: '/destinations', icon: 'flag'     },
    { label: 'Locations',    path: '/locations',    icon: 'pin'      },
    { label: 'Places',       path: '/places',       icon: 'home'     },
    { label: 'Events',       path: '/events',       icon: 'calendar' },
  ];

  analyticsNav: SidebarNavItem[] = [
    { label: 'Analytics & Insights', path: '/analytics', icon: 'chart' },
  ];
}