import { Routes } from '@angular/router';

export const routes: Routes = [
  // Redirect root → login
  { path: '', redirectTo: 'login', pathMatch: 'full' },

  // ── Auth ─────────────────────────────────
  {
    path: 'login',
    loadComponent: () =>
      import('./features/auth/pages/login-page/login-page.component').then(m => m.LoginComponent),
  },

  // ── Shell ──────────────────────────────────
  {
    path: '',
    loadComponent: () =>
      import('./core/layout/app-shell/app-shell.component').then(m => m.AppShellComponent),
    children: [
      {
        path: 'dashboard',
        loadComponent: () =>
          import('./features/dashboard/pages/dashboard-page/dashboard-page.component').then(m => m.DashboardComponent),
      },
      {
        path: 'locations/new',
        loadComponent: () =>
          import('./features/add-pages/pages/add-location/add-location.component').then(m => m.NewLocationComponent),
      },
    //   {
    //     path: 'requests',
    //     loadComponent: () =>
    //       import('./features/requests/pages/requests-queue/requests-queue.component')
    //         .then(m => m.RequestsQueueComponent),
    //   },
    //   {
    //     path: 'map',
    //     loadComponent: () =>
    //       import('./features/map/pages/map-page/map-page.component')
    //         .then(m => m.MapPageComponent),
    //   },
    //   {
    //     path: 'destinations',
    //     loadComponent: () =>
    //       import('./features/destinations/pages/destinations-list/destinations-list.component')
    //         .then(m => m.DestinationsListComponent),
    //   },
    //   {
    //     path: 'locations',
    //     loadComponent: () =>
    //       import('./features/locations/pages/locations-list/locations-list.component')
    //         .then(m => m.LocationsListComponent),
    //   },
    //   {
    //     path: 'places',
    //     loadComponent: () =>
    //       import('./features/places/pages/places-list/places-list.component')
    //         .then(m => m.PlacesListComponent),
    //   },
    //   {
    //     path: 'events',
    //     loadComponent: () =>
    //       import('./features/events/pages/events-list/events-list.component')
    //         .then(m => m.EventsListComponent),
    //   },
    //   {
    //     path: 'analytics',
    //     loadComponent: () =>
    //       import('./features/analytics/pages/analytics-page/analytics-page.component')
    //         .then(m => m.AnalyticsPageComponent),
    //   },
      {
        path: 'settings',
        loadComponent: () =>
          import('./features/settings/pages/settings-page/settings-page.component')
            .then(m => m.SettingsComponent),
      },
    ],
  },

  { path: '**', redirectTo: 'login' },
];