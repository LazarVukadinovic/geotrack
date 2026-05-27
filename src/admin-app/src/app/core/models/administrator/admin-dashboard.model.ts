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
  supportTicketsCount: number;
  openSupportTicketsCount: number;
  inProgressSupportTicketsCount: number;
  resolvedSupportTicketsCount: number;
  dismissedSupportTicketsCount: number;
  unresolvedSupportTicketsCount: number;
  highPrioritySupportTicketsCount: number;
}

export interface AdminDashboardQuickLinks {
  territoryManagementRoute: string;
  userManagementRoute: string;
  reviewManagementRoute: string;
  supportTicketsRoute: string;
  pendingReportsRoute: string;
}

export interface AdminDashboardBadges {
  pendingReportsCount: number;
  supportReportsCount: number;
}

export interface AdminDashboard {
  currentUser: AdminDashboardCurrentUser;
  stats: AdminDashboardStats;
  quickLinks: AdminDashboardQuickLinks;
  badges: AdminDashboardBadges;
}
