import { Routes } from '@angular/router';
import { authGuard, nationalManagerGuard, regionalManagerGuard } from './core/guards/auth.guard';
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
    canActivate:[authGuard],
    children: [
      {
        path: 'dashboard',
        canActivate: [regionalManagerGuard],
        loadComponent: () =>
          import('./features/dashboard/pages/dashboard-page/dashboard-page.component').then(m => m.DashboardComponent),
      },
      {
        path: 'locations',
        canActivate: [regionalManagerGuard],
        loadComponent: () =>
          import('./features/locations/pages/all-locations/all-locations/all-locations.component').then(m => m.AllLocationsComponent),
      },
      {
        path: 'locations/detail/:id',
        canActivate: [regionalManagerGuard],
        loadComponent: () =>
          import('./features/locations/pages/location-detail/location-detail/location-detail.component').then(m => m.LocationDetailComponent),
      },
      {
        path: 'destinations',
        canActivate: [regionalManagerGuard],
        loadComponent: () =>
          import('./features/destinations/pages/all-destinations/all-destinations/all-destinations.component').then(m => m.AllDestinationsComponent),
      },
      {
        path: 'destinations/detail/:id',
        canActivate: [regionalManagerGuard],
        loadComponent: () =>
          import('./features/destinations/pages/destination-detail/destination-detail/destination-detail.component').then(m => m.DestinationDetailComponent),
      },
      {
        path: 'places',
        canActivate: [regionalManagerGuard],
        loadComponent: () =>
          import('./features/places/pages/all-places/all-places/all-places.component').then(m => m.AllPlacesComponent),
      },
      {
        path: 'places/detail/:id',
        canActivate: [regionalManagerGuard],
        loadComponent: () =>
          import('./features/places/pages/place-detail/place-detail/place-detail.component').then(m => m.PlaceDetailComponent),
      },
      {
        path: 'events',
        canActivate: [regionalManagerGuard],
        loadComponent: () =>
          import('./features/events/pages/all-events/all-events/all-events.component').then(m => m.AllEventsComponent),
      },
      {
        path: 'events/detail/:id',
        canActivate: [regionalManagerGuard],
        loadComponent: () =>
          import('./features/events/pages/event-detail/event-detail/event-detail.component').then(m => m.EventDetailComponent),
      },
      {
        path: 'destinations/new',
        canActivate: [regionalManagerGuard],
        loadComponent: () =>
          import('./features/add-pages/pages/add-destination/add-destination.component').then(m => m.AddDestinationComponent),
      },
      {
        path: 'destinations/edit/:id',
        canActivate: [regionalManagerGuard],
        loadComponent: () =>
          import('./features/add-pages/pages/add-destination/add-destination.component').then(m => m.AddDestinationComponent),
      },
      {
        path: 'locations/new',
        canActivate: [regionalManagerGuard],
        loadComponent: () =>
          import('./features/add-pages/pages/add-location/add-location.component').then(m => m.NewLocationComponent),
      },
      {
        path: 'places/new',
        canActivate: [regionalManagerGuard],
        loadComponent: () =>
          import('./features/add-pages/pages/add-place/add-place.component').then(m => m.NewPlaceComponent),
      },
      {
        path: 'places/edit/:id',
        canActivate: [regionalManagerGuard],
        loadComponent: () =>
          import('./features/add-pages/pages/add-place/add-place.component').then(m => m.NewPlaceComponent),
      },
      {
        path: 'events/new',
        canActivate: [regionalManagerGuard],
        loadComponent: () =>
          import('./features/add-pages/pages/add-event/add-event.component').then(m => m.NewEventComponent),
      },
      {
        path: 'requests',
        canActivate: [regionalManagerGuard],
        loadComponent: () =>
          import('./features/requests/pages/requests-queue/requests-queue.component').then(m => m.ReviewQueueComponent),
      },
      {
        path: 'activity-history',
        canActivate: [regionalManagerGuard],
        loadComponent: () =>
          import('./features/dashboard/pages/activity-history/activity-history.component').then(m => m.ActivityHistoryComponent),
      },
      {
        path: 'request/detail/:source/:id',
        canActivate: [regionalManagerGuard],
        loadComponent: () =>
          import('./features/requests/pages/request-detail/request-detail.component').then(m => m.RequestDetailComponent),
      },
      {
        path: 'request/detail/:id',
        canActivate: [regionalManagerGuard],
        loadComponent: () =>
          import('./features/requests/pages/request-detail/request-detail.component').then(m => m.RequestDetailComponent),
      },
      {
        path: 'map',
        canActivate: [regionalManagerGuard],
        loadComponent: () =>
          import('./features/map/pages/map-page/map-page.component').then(m => m.MapPageComponent),
      },
      {
        path: 'analytics',
        canActivate:[regionalManagerGuard],
        loadComponent: () =>
          import('./features/analytics/pages/analytics-page/analytics-page/analytics-page.component')
            .then(m => m.AnalyticsPageComponent),
      },
      {
        path:'national-analytics',
        canActivate:[nationalManagerGuard],
        loadComponent: () => import('./features/analytics/pages/national-analytics/national-analytics/national-analytics.component').then(m=>m.NationalAnalyticsComponent),
      },
      {
        path: 'national-dashboard',
        canActivate:[nationalManagerGuard],
        loadComponent: () => import('./features/dashboard/pages/national-dashboard/national-dashboard.component').then(m=>m.NationalDashboardComponent),
      },
      {
        path: 'regions',
        canActivate:[nationalManagerGuard],
        loadComponent: () => import('./features/regions/pages/regions/regions.component').then(m=>m.AllRegionsComponent),
      },
      {
        path: 'regions/:id',
        loadComponent: () => import('./features/regions/pages/region-detail/region-detail/region-detail.component').then(m=>m.RegionDetailComponent),
      },
      {
        path: 'territories/:id',
        loadComponent: () => import('./features/territories/pages/territory-detail/territory-detail.component').then(m=>m.TerritoryDetailComponent),
      },
      {
        path: 'user-administration',
        canActivate: [nationalManagerGuard],
        loadComponent: () =>
          import('./features/users/pages/user-administration/user-administration/user-administration.component').then(m => m.UserAdministrationComponent),
      },
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
