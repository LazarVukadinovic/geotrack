import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

export interface BottomNavTab {
  label: string;
  path: string;
  /** When true, only exact URL matches activate the tab (e.g. Explore). */
  exact?: boolean;
}

@Component({
  selector: 'app-bottom-nav',
  standalone: true,
  imports: [RouterLink, RouterLinkActive],
  templateUrl: './bottom-nav.component.html',
  styleUrl: './bottom-nav.component.scss',
})
export class BottomNavComponent {
  protected readonly tabs: BottomNavTab[] = [
    { label: 'Explore', path: '/explore', exact: true },
    { label: 'Map', path: '/map', exact: false },
    { label: 'Ask Agent', path: '/agent', exact: true },
    { label: 'Profile', path: '/profile', exact: false },
  ];
}
