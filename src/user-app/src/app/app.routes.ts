import { Routes } from '@angular/router';
export const routes: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./core/layout/app-shell/app-shell.component').then(m => m.AppShellComponent),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'contributions' },
      {
        path: 'home',
        loadComponent: () =>
          import('./features/home/pages/home/home.component').then((m) => m.Home),
      },
      {
        path: 'explore',
        loadComponent: () =>
          import('./features/explore/pages/explore-page/explore-page.component').then(m => m.ExplorePageComponent),
      },
      {
        path: 'map',
        loadComponent: () =>
          import('./features/map/pages/map-page/map-page.component').then(m => m.MapPageComponent),
      },
      {
      path: 'filters',
        loadComponent: () =>
          import('./features/map/pages/filters/filters.component').then(
            (m) => m.Filters
          ),
        data: { hideBottomNav: true },
      },
      {
        path: 'agent',
        loadComponent: () =>
          import('./features/agent/pages/agent-page/agent-page.component').then(m => m.AgentPageComponent),
      },

      // ── Profile ──────────────────────────────────────────────────────────
      {
        path: 'profile',
        loadComponent: () =>
          import('./features/profile/pages/profile/profile.component').then(m => m.ProfileComponent),
      },
      {
        path: 'profile/edit',
        loadComponent: () =>
          import('./features/profile/pages/edit-profile/edit-profile.component').then(m => m.EditProfileComponent),
        data: { hideBottomNav: true },
      },
      {
        path: 'profile/collections',
        loadComponent: () =>
          import('./features/profile/pages/my-collections-page/my-collections-page.component').then(m => m.MyCollectionsPageComponent),
        data: { hideBottomNav: true },
      },
      {
        path: 'profile/collections/:id',
        loadComponent: () =>
          import('./features/profile/pages/collection-detail/collection-detail.component').then(m => m.CollectionDetailComponent),
        data: { hideBottomNav: true },
      },
      {
        path: 'contributions',
        loadComponent: () =>
          import('./features/contributions/pages/my-contributions/my-contributions.component')
            .then(m => m.MyContributionsPageComponent),
        data: { hideBottomNav: true },
      },
      {
        path: 'contributions/submit',
        loadComponent: () =>
          import('./features/contributions/pages/submit-contribution/submit-contribution.component')
            .then(m => m.SubmitContributionComponent),
        data: { hideBottomNav: true },
      },

      // ── Settings ─────────────────────────────────────────────────────────
      {
        path: 'settings',
        loadComponent: () =>
          import('./features/settings/pages/settings/settings.component').then(m => m.SettingsComponent),
        data: { hideBottomNav: true },
      },
      {
        path: 'settings/interests',
        loadComponent: () =>
          import('./features/settings/pages/my-interests/my-interests.component').then(m => m.MyInterestsComponent),
        data: { hideBottomNav: true },
      },

      // ── Auth (registracija) ───────────────────────────────────────────────
      {
        path: 'register',
        loadComponent: () =>
          import('./features/auth/pages/register-page/register-page.component').then(m => m.RegisterPageComponent),
        data: { hideBottomNav: true },
      },
      {
        path: 'register/interests',
        loadComponent: () =>
          import('./features/auth/pages/interests-page/interests-page.component').then(m => m.InterestsPageComponent),
        data: { hideBottomNav: true },
      },

      // ── Ostale rute ───────────────────────────────────────────────────────
      {
        path: 'recommendations',
        loadComponent: () =>
          import('./features/recommendations/pages/recommendations-page/recommendations-page.component').then(m => m.RecommendationsPageComponent),
        data: { hideBottomNav: true },
      },
      {
        path: 'hotels',
        loadComponent: () =>
          import('./features/hotels/pages/hotels-listing-page/hotels-listing-page.component').then(m => m.HotelsListingPageComponent),
        data: { hideBottomNav: true },
      },
      {
        path: 'food-and-music',
        loadComponent: () =>
          import('./features/food-music/pages/food-music-page/food-music-page.component').then(m => m.FoodMusicPageComponent),
        data: { hideBottomNav: true },
      },
      {
        path: 'culture-and-history',
        loadComponent: () =>
          import('./features/culture-history/pages/culture-history-page/culture-history-page.component').then(m => m.CultureHistoryPageComponent),
        data: { hideBottomNav: true },
      },
      {
        path: 'nature',
        loadComponent: () =>
          import('./features/nature/pages/nature-page/nature-page.component').then(m => m.NaturePageComponent),
        data: { hideBottomNav: true },
      },
      {
        path: 'local-places',
        loadComponent: () =>
          import('./features/local-places/pages/local-places-page/local-places-page.component').then(m => m.LocalPlacesPageComponent),
        data: { hideBottomNav: true },
      },
      {
        path: 'local-places/:category',
        loadComponent: () =>
          import('./features/local-places/pages/local-places-category-page/local-places-category-page.component').then(m => m.LocalPlacesCategoryPageComponent),
        data: { hideBottomNav: true },
      },
      {
        path: 'places/:id',
        loadComponent: () =>
          import('./features/place-detail/pages/place-detail-page/place-detail-page.component').then(m => m.PlaceDetailPageComponent),
        data: { hideBottomNav: true },
      },
      {
        path: 'stays/:id',
        loadComponent: () =>
          import('./features/hotel-detail/pages/hotel-detail-page/hotel-detail-page.component').then(m => m.HotelDetailPageComponent),
        data: { hideBottomNav: true },
      },
      {
        path: 'events',
        loadComponent: () =>
          import('./features/events-listing/pages/events-listing-page/events-listing-page.component').then(m => m.EventsListingPageComponent),
        data: { hideBottomNav: true },
      },
      {
        path: 'events/:id',
        loadComponent: () =>
          import('./features/event-detail/pages/event-detail-page/event-detail-page.component').then(m => m.EventDetailPageComponent),
        data: { hideBottomNav: true },
      },
    ],
  },
  { path: '**', redirectTo: 'explore' },
];