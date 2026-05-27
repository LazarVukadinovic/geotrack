import { AdminDashboardCurrentUser } from '../administrator/admin-dashboard.model';

export interface ContentModeratorDashboardStats {
  destinationsCount: number;
  locationsCount: number;
  placesCount: number;
  eventsCount: number;
  suspendedDestinationsCount: number;
  suspendedLocationsCount: number;
  suspendedPlacesCount: number;
  suspendedEventsCount: number;
  deletedDestinationsCount: number;
  deletedLocationsCount: number;
  deletedPlacesCount: number;
  deletedEventsCount: number;
  pendingReviewReportsCount: number;
  reportedReviewsCount: number;
  activeReviewsCount: number;
  resolvedReportsCount: number;
  removedReviewsCount: number;
}

export interface ContentModeratorDashboardQuickLinks {
  destinationsReviewRoute: string;
  locationsReviewRoute: string;
  placesReviewRoute: string;
  eventsReviewRoute: string;
  reviewManagementRoute: string;
  pendingReportsRoute: string;
}

export interface ContentModeratorDashboardBadges {
  pendingReviewReportsCount: number;
}

export interface ContentModeratorDashboard {
  currentUser: AdminDashboardCurrentUser;
  stats: ContentModeratorDashboardStats;
  quickLinks: ContentModeratorDashboardQuickLinks;
  badges: ContentModeratorDashboardBadges;
}
