import { Routes } from '@angular/router';
import { guestGuard } from './core/guards/guest.guard';
import { authGuard } from './core/guards/auth.guard';
import { interestsOnboardingGuard } from './core/guards/interests-onboarding.guard';
export const routes: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./core/layout/app-shell/app-shell.component').then(m => m.AppShellComponent),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'home' },
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
          import('./features/profile/pages/profile-tab/profile-tab.component').then(m => m.ProfileTabComponent),
      },
      {
        path: 'profile/edit',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/profile/pages/edit-profile/edit-profile.component').then(m => m.EditProfileComponent),
        data: { hideBottomNav: true },
      },
      {
        path: 'profile/collections',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/profile/pages/my-collections-page/my-collections-page.component').then(m => m.MyCollectionsPageComponent),
        data: { hideBottomNav: true },
      },
      {
        path: 'profile/collections/:id',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/profile/pages/collection-detail/collection-detail.component').then(m => m.CollectionDetailComponent),
        data: { hideBottomNav: true },
      },

      // ── Settings ─────────────────────────────────────────────────────────
      {
        path: 'settings',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/settings/pages/settings/settings.component').then(m => m.SettingsComponent),
        data: { hideBottomNav: true },
      },
      {
        path: 'settings/interests',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/settings/pages/my-interests/my-interests.component').then(m => m.MyInterestsComponent),
        data: { hideBottomNav: true },
      },
      {
        path: 'settings/change-password',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/settings/pages/change-password-page/change-password-page.component').then(m => m.ChangePasswordPageComponent),
        data: { hideBottomNav: true },
      },
      {
        path: 'settings/notifications',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/settings/pages/notifications-page/notifications-page.component').then(m => m.NotificationsPageComponent),
        data: { hideBottomNav: true },
      },
      {
        path: 'settings/privacy-settings',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/settings/pages/privacy-settings-page/privacy-settings-page.component').then(m => m.PrivacySettingsPageComponent),
        data: { hideBottomNav: true },
      },
      {
        path: 'settings/help-center',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/settings/pages/help-center-page/help-center-page.component').then(m => m.HelpCenterPageComponent),
        data: { hideBottomNav: true },
      },
      {
        path: 'settings/contact-support',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/settings/pages/contact-support-page/contact-support-page.component').then(m => m.ContactSupportPageComponent),
        data: { hideBottomNav: true },
      },
      {
        path: 'settings/terms-and-privacy',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/settings/pages/terms-privacy-page/terms-privacy-page.component').then(m => m.TermsPrivacyPageComponent),
        data: { hideBottomNav: true },
      },

      // ── Auth (registracija) ───────────────────────────────────────────────
      {
        path: 'login',
        canActivate: [guestGuard],
        loadComponent: () =>
          import('./features/auth/pages/login-page/login-page.component').then(m => m.LoginPageComponent),
      },
      {
        path: 'register',
        canActivate: [guestGuard],
        loadComponent: () =>
          import('./features/auth/pages/register-page/register-page.component').then(m => m.RegisterPageComponent),
        data: { hideBottomNav: true },
      },
      {
        path: 'register/interests',
        canActivate: [interestsOnboardingGuard],
        loadComponent: () =>
          import('./features/auth/pages/interests-page/interests-page.component').then(m => m.InterestsPageComponent),
        data: { hideBottomNav: true },
      },

      // ── Ostale rute ───────────────────────────────────────────────────────
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
