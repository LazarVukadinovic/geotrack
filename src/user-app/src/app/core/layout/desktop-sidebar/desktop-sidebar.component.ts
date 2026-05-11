import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

interface DesktopNavItem {
  label: string;
  path: string;
  exact: boolean;
}

@Component({
  selector: 'app-desktop-sidebar',
  standalone: true,
  imports: [RouterLink, RouterLinkActive],
  templateUrl: './desktop-sidebar.component.html',
  styleUrl: './desktop-sidebar.component.scss',
})
export class DesktopSidebarComponent {
  protected readonly navItems: DesktopNavItem[] = [
    { label: 'Explore', path: '/explore', exact: true },
    { label: 'Map', path: '/map', exact: true },
    { label: 'Ask Agent', path: '/agent', exact: true },
    { label: 'Profile', path: '/profile', exact: false },
  ];
}
