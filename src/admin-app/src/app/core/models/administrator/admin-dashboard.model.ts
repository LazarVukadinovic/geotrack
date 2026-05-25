export interface AdminDashboardCurrentUser {
  id: number;
  displayName: string;
  email: string;
  avatarUrl?: string | null;
  roleDisplayName: string;
}

export interface AdminDashboardStats {
  territoriesCount: number;
  assignedTerritoriesCount: number;
  appUsersCount: number;
  suspendedUsersCount: number;
  reviewsCount: number;
  reportedReviewsCount: number;
  pendingReportsCount: number;
}

export interface AdminDashboardQuickLinks {
  territoryManagementRoute: string;
  userManagementRoute: string;
  reviewManagementRoute: string;
  pendingReportsRoute: string;
}

export interface AdminDashboardBadges {
  pendingReportsCount: number;
}

export interface AdminDashboard {
  currentUser: AdminDashboardCurrentUser;
  stats: AdminDashboardStats;
  quickLinks: AdminDashboardQuickLinks;
  badges: AdminDashboardBadges;
}
