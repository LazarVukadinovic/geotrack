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
        data: { desktopLayout: 'full' },
      },
      {
        path: 'search',
        loadComponent: () =>
          import('./features/explore/pages/explore-page/explore-page.component').then(m => m.ExplorePageComponent),
        data: { hideBottomNav: true, searchOverlay: true, desktopLayout: 'wide' },
      },
      {
        path: 'explore',
        loadComponent: () =>
          import('./features/explore/pages/explore-page/explore-page.component').then(m => m.ExplorePageComponent),
        data: { desktopLayout: 'wide' },
      },
      {
        path: 'explore/:sectionKey',
        loadComponent: () =>
          import('./features/explore/pages/explore-section-listing-page/explore-section-listing-page.component')
            .then(m => m.ExploreSectionListingPageComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide' },
      },
      {
        path: 'map',
        loadComponent: () =>
          import('./features/map/pages/map-page/map-page.component').then(m => m.MapPageComponent),
        data: { desktopLayout: 'full' },
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
        data: { desktopLayout: 'full' },
      },

      // Ã¢â€â‚¬Ã¢â€â‚¬ Profile Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬
      {
        path: 'profile',
        loadComponent: () =>
          import('./features/profile/pages/profile-tab/profile-tab.component').then(m => m.ProfileTabComponent),
        data: { desktopLayout: 'wide' },
      },
      {
        path: 'profile/edit',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/profile/pages/edit-profile/edit-profile.component').then(m => m.EditProfileComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide' },
      },
      {
        path: 'profile/collections',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/profile/pages/my-collections-page/my-collections-page.component').then(m => m.MyCollectionsPageComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide' },
      },
      {
        path: 'profile/collections/:id',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/profile/pages/collection-detail/collection-detail.component').then(m => m.CollectionDetailComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide' },
      },
      {
        path: 'profile/reviews',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/reviews/pages/my-reviews-page/my-reviews-page.component')
            .then(m => m.MyReviewsPageComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide' },
      },
      {
        path: 'users/:userId/reviews',
        loadComponent: () =>
          import('./features/reviews/pages/user-reviews-page/user-reviews-page.component')
            .then(m => m.UserReviewsPageComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide' },
      },
      {
        path: 'contributions',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/contributions/pages/my-contributions/my-contributions.component')
            .then(m => m.MyContributionsPageComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide' },
      },
      {
        path: 'contributions/submit',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/contributions/pages/submit-contribution/submit-contribution.component')
            .then(m => m.SubmitContributionComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide' },
      },
      {
        path: 'contributions/detail',
        redirectTo: 'contributions',
        pathMatch: 'full',
      },
      {
        path: 'contributions/:id/edit',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/contributions/pages/submit-contribution/submit-contribution.component')
            .then(m => m.SubmitContributionComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide' },
      },
      {
        path: 'contributions/:id',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/contributions/pages/contribution-detail/contribution-detail.component')
            .then(m => m.ContributionDetailPageComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide' },
      },

      // Ã¢â€â‚¬Ã¢â€â‚¬ Settings Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬
      {
        path: 'settings',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/settings/pages/settings/settings.component').then(m => m.SettingsComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide' },
      },
      {
        path: 'settings/interests',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/settings/pages/my-interests/my-interests.component').then(m => m.MyInterestsComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide' },
      },
      {
        path: 'settings/change-password',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/settings/pages/change-password-page/change-password-page.component').then(m => m.ChangePasswordPageComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide' },
      },
      {
        path: 'settings/notifications',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/settings/pages/notifications-page/notifications-page.component').then(m => m.NotificationsPageComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide' },
      },
      {
        path: 'settings/privacy-settings',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/settings/pages/privacy-settings-page/privacy-settings-page.component').then(m => m.PrivacySettingsPageComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide' },
      },
      {
        path: 'settings/analytics',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/settings/pages/analytics-settings-page/analytics-settings-page.component').then(m => m.AnalyticsSettingsPageComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide' },
      },
      {
        path: 'settings/help-center',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/settings/pages/help-center-page/help-center-page.component').then(m => m.HelpCenterPageComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide' },
      },
      {
        path: 'settings/contact-support',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/settings/pages/contact-support-page/contact-support-page.component').then(m => m.ContactSupportPageComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide' },
      },
      {
        path: 'settings/terms-and-privacy',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/settings/pages/terms-privacy-page/terms-privacy-page.component').then(m => m.TermsPrivacyPageComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide' },
      },

      // Ã¢â€â‚¬Ã¢â€â‚¬ Auth (registracija) Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬
      {
        path: 'recommendations',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/recommendations/pages/recommendations-page/recommendations-page.component').then(m => m.RecommendationsPageComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide' },
      },
      {
        path: 'notifications',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./features/notifications/pages/notifications-center-page/notifications-center-page.component').then(m => m.NotificationsCenterPageComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide' },
      },
      {
        path: 'reviews/:entityType/:id',
        loadComponent: () =>
          import('./features/reviews/pages/reviews-page/reviews-page.component').then(m => m.ReviewsPageComponent),
        data: { hideBottomNav: true },
      },
      {
        path: 'login',
        canActivate: [guestGuard],
        loadComponent: () =>
          import('./features/auth/pages/login-page/login-page.component').then(m => m.LoginPageComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide' },
      },
      {
        path: 'register',
        canActivate: [guestGuard],
        loadComponent: () =>
          import('./features/auth/pages/register-page/register-page.component').then(m => m.RegisterPageComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide' },
      },
      {
        path: 'register/interests',
        canActivate: [interestsOnboardingGuard],
        loadComponent: () =>
          import('./features/auth/pages/interests-page/interests-page.component').then(m => m.InterestsPageComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide' },
      },

      // Ã¢â€â‚¬Ã¢â€â‚¬ Ostale rute Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬
      {
        path: 'destinations',
        loadComponent: () =>
          import('./features/destinations/pages/destination-discovery-page/destination-discovery-page.component')
            .then(m => m.DestinationDiscoveryPageComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide' },
      },
      {
        path: 'hotels',
        loadComponent: () =>
          import('./features/hotels/pages/hotels-listing-page/hotels-listing-page.component').then(m => m.HotelsListingPageComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide' },
      },
      {
        path: 'food-and-music',
        loadComponent: () =>
          import('./features/food-music/pages/food-music-page/food-music-page.component').then(m => m.FoodMusicPageComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide' },
      },
      {
        path: 'culture-and-history',
        loadComponent: () =>
          import('./features/culture-history/pages/culture-history-page/culture-history-page.component').then(m => m.CultureHistoryPageComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide' },
      },
      {
        path: 'nature',
        loadComponent: () =>
          import('./features/nature/pages/nature-page/nature-page.component').then(m => m.NaturePageComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide' },
      },
      {
        path: 'local-places',
        loadComponent: () =>
          import('./features/local-places/pages/local-places-page/local-places-page.component').then(m => m.LocalPlacesPageComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide' },
      },
      {
        path: 'local-places/:category',
        loadComponent: () =>
          import('./features/local-places/pages/local-places-category-page/local-places-category-page.component').then(m => m.LocalPlacesCategoryPageComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide' },
      },

      {
        path: 'territories/:id',
        loadComponent: () =>
          import('./features/hierarchy-detail/pages/entity-hierarchy-detail-page/entity-hierarchy-detail-page.component')
            .then(m => m.EntityHierarchyDetailPageComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide', entityType: 'territory' },
      },
      {
        path: 'regions/:id',
        loadComponent: () =>
          import('./features/hierarchy-detail/pages/entity-hierarchy-detail-page/entity-hierarchy-detail-page.component')
            .then(m => m.EntityHierarchyDetailPageComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide', entityType: 'region' },
      },
      {
        path: 'destinations/:id',
        loadComponent: () =>
          import('./features/hierarchy-detail/pages/entity-hierarchy-detail-page/entity-hierarchy-detail-page.component')
            .then(m => m.EntityHierarchyDetailPageComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide', entityType: 'destination' },
      },
      {
        path: 'locations/:id',
        loadComponent: () =>
          import('./features/hierarchy-detail/pages/entity-hierarchy-detail-page/entity-hierarchy-detail-page.component')
            .then(m => m.EntityHierarchyDetailPageComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide', entityType: 'location' },
      },
      {
        path: 'places/:id',
        loadComponent: () =>
          import('./features/place-detail/pages/place-detail-page/place-detail-page.component').then(m => m.PlaceDetailPageComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide' },
      },
      {
        path: 'stays/:id',
        loadComponent: () =>
          import('./features/hotel-detail/pages/hotel-detail-page/hotel-detail-page.component').then(m => m.HotelDetailPageComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide' },
      },
      {
        path: 'events',
        loadComponent: () =>
          import('./features/events-listing/pages/events-listing-page/events-listing-page.component').then(m => m.EventsListingPageComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide' },
      },
      {
        path: 'events/:id',
        loadComponent: () =>
          import('./features/event-detail/pages/event-detail-page/event-detail-page.component').then(m => m.EventDetailPageComponent),
        data: { hideBottomNav: true, desktopLayout: 'wide' },
      },
      {
        path: '**',
        loadComponent: () =>
          import('./core/pages/not-found-page/not-found-page.component').then(m => m.NotFoundPageComponent),
        data: { hideBottomNav: true },
      },
    ],
  },
];
