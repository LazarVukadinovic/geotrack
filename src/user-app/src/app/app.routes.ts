import { Routes } from '@angular/router';
import { Login } from './features/auth/pages/login/login';
import { Register } from './features/auth/pages/register/register';
import { Interests } from './features/auth/pages/interests/interests';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./core/layout/app-shell/app-shell.component').then((m) => m.AppShellComponent),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'explore' },
      {
        path: 'explore',
        loadComponent: () =>
          import('./features/explore/pages/explore-page/explore-page.component').then(
            (m) => m.ExplorePageComponent,
          ),
      },
      {
        path: 'map',
        loadComponent: () =>
          import('./features/map/pages/map-page/map-page.component').then((m) => m.MapPageComponent),
      },
      {
        path: 'agent',
        loadComponent: () =>
          import('./features/agent/pages/agent-page/agent-page.component').then((m) => m.AgentPageComponent),
      },
      {
        path: 'profile/edit',
        loadComponent: () =>
          import('./features/profile/pages/profile-edit-page/profile-edit-page.component').then(
            (m) => m.ProfileEditPageComponent,
          ),
        data: { hideBottomNav: true },
      },
      {
        path: 'profile',
        loadComponent: () =>
          import('./features/profile/pages/profile-page/profile-page.component').then(
            (m) => m.ProfilePageComponent,
          ),
      },
      {
        path: 'settings',
        loadComponent: () =>
          import('./features/settings/pages/settings-page/settings-page.component').then(
            (m) => m.SettingsPageComponent,
          ),
        data: { hideBottomNav: true },
      },
      {
        path: 'recommendations',
        loadComponent: () =>
          import('./features/recommendations/pages/recommendations-page/recommendations-page.component').then(
            (m) => m.RecommendationsPageComponent,
          ),
        data: { hideBottomNav: true },
      },
      {
        path: 'hotels',
        loadComponent: () =>
          import('./features/hotels/pages/hotels-listing-page/hotels-listing-page.component').then(
            (m) => m.HotelsListingPageComponent,
          ),
        data: { hideBottomNav: true },
      },
      {
        path: 'food-and-music',
        loadComponent: () =>
          import('./features/food-music/pages/food-music-page/food-music-page.component').then(
            (m) => m.FoodMusicPageComponent,
          ),
        data: { hideBottomNav: true },
      },
      {
        path: 'culture-and-history',
        loadComponent: () =>
          import('./features/culture-history/pages/culture-history-page/culture-history-page.component').then(
            (m) => m.CultureHistoryPageComponent,
          ),
        data: { hideBottomNav: true },
      },
      {
        path: 'nature',
        loadComponent: () =>
          import('./features/nature/pages/nature-page/nature-page.component').then(
            (m) => m.NaturePageComponent,
          ),
        data: { hideBottomNav: true },
      },
      {
        path: 'local-places',
        loadComponent: () =>
          import('./features/local-places/pages/local-places-page/local-places-page.component').then(
            (m) => m.LocalPlacesPageComponent,
          ),
        data: { hideBottomNav: true },
      },
      {
        path: 'local-places/:category',
        loadComponent: () =>
          import('./features/local-places/pages/local-places-category-page/local-places-category-page.component').then(
            (m) => m.LocalPlacesCategoryPageComponent,
          ),
        data: { hideBottomNav: true },
      },
      {
        path: 'places/:id',
        loadComponent: () =>
          import('./features/place-detail/pages/place-detail-page/place-detail-page.component').then(
            (m) => m.PlaceDetailPageComponent,
          ),
        data: { hideBottomNav: true },
      },
      {
        path: 'stays/:id',
        loadComponent: () =>
          import('./features/hotel-detail/pages/hotel-detail-page/hotel-detail-page.component').then(
            (m) => m.HotelDetailPageComponent,
          ),
        data: { hideBottomNav: true },
      },
      {
        path: 'events',
        loadComponent: () =>
          import('./features/events-listing/pages/events-listing-page/events-listing-page.component').then(
            (m) => m.EventsListingPageComponent,
          ),
        data: { hideBottomNav: true },
      },
      {
        path: 'events/:id',
        loadComponent: () =>
          import('./features/event-detail/pages/event-detail-page/event-detail-page.component').then(
            (m) => m.EventDetailPageComponent,
          ),
        data: { hideBottomNav: true },
      },
    ],
  },
  {
    path: 'auth',
    loadComponent: () =>
      import('./features/auth/pages/auth-shell/auth-shell.component').then((m) => m.AuthShellComponent),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'login' },
      {
        path: 'login',
        loadComponent: () =>
          import('./features/auth/pages/login-page/login-page.component').then((m) => m.LoginPageComponent),
      },
      {
        path: 'register',
        loadComponent: () =>
          import('./features/auth/pages/register-page/register-page.component').then(
            (m) => m.RegisterPageComponent,
          ),
      },
      {
        path: 'interests',
        loadComponent: () =>
          import('./features/auth/pages/interests-page/interests-page.component').then(
            (m) => m.InterestsPageComponent,
          ),
      },
    ],
  },
  { path: '**', redirectTo: 'explore' },
];
