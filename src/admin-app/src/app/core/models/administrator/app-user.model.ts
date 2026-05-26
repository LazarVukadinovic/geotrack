export type AdminUserTab = 'app-users' | 'territory-managers';
export type AdminUserStatusFilter = 'all' | 'active' | 'suspended' | 'deleted';
export type AdminUserSort =
  | 'newest'
  | 'oldest'
  | 'lastActiveDesc'
  | 'nameAsc'
  | 'reviewsDesc'
  | 'reportedDesc';

export type AppUserStatus = 'Active' | 'Suspended' | 'Deleted';

export interface AdminUserListCounts {
  appUsersCount: number;
  territoryManagersCount: number;
  activeCount: number;
  suspendedCount: number;
  deletedCount: number;
}

export interface AdminUserAssignedTerritory {
  territoryId: number;
  territoryName: string;
  displayName: string;
  countryCode?: string | null;
}

export interface AppUser {
  userId: number;
  id: string;
  displayName: string;
  firstName: string;
  lastName: string;
  email: string;
  avatarUrl?: string | null;
  initials: string;
  avatarInitials: string;
  avatarColor: string;
  status: AppUserStatus;
  roles: string[];
  isAdmin: boolean;
  isCurrentUser: boolean;
  reviewsCount: number;
  reportedCount: number;
  reviewCount: number;
  reportedReviewCount: number;
  joinedAt: string;
  lastActiveAt: string | null;
  assignedTerritories: AdminUserAssignedTerritory[];
  assignedTerritoryCount: number;
  canAssignTerritoryManager: boolean;
  canSuspend: boolean;
  canUnsuspend: boolean;
  canDelete: boolean;
}

export interface AdminUserListResponse {
  items: AppUser[];
  totalCount: number;
  skip: number;
  take: number;
  nextSkip: number | null;
  hasMore: boolean;
  counts: AdminUserListCounts;
}

export interface AppUsersPage {
  items: AppUser[];
  total: number;
  totalCount: number;
  skip: number;
  take: number;
  nextSkip: number | null;
  hasMore: boolean;
  counts: AdminUserListCounts;
}

export interface AppUsersPageRequest {
  tab: AdminUserTab;
  search: string;
  status: AdminUserStatusFilter;
  sort: AdminUserSort;
  skip: number;
  take: number;
}

export interface AssignTerritoryManagerRequest {
  territoryId: number;
}

export interface AdminUserPreview {
  user: AdminUserPreviewProfile;
  roles: AdminUserRole[];
  accountSummary: AdminUserAccountSummary;
  reportsMade: AdminUserPreviewSection<AdminUserReportPreview>;
  reportsAgainstUser: AdminUserPreviewSection<AdminUserReportPreview>;
  recentReviews: AdminUserPreviewSection<AdminUserRecentReview>;
  moderationHistory: AdminUserPreviewSection<AdminUserModerationHistoryItem>;
  permissions: AdminUserPreviewPermissions;
}

export interface AdminUserPreviewProfile {
  userId: number;
  id: string;
  displayName: string;
  firstName: string;
  lastName: string;
  email: string;
  avatarUrl?: string | null;
  initials: string;
  avatarInitials: string;
  avatarColor: string;
  status: AppUserStatus;
  roles: string[];
  isCurrentUser: boolean;
  isAdmin: boolean;
  hasElevatedAccess: boolean;
  accountType: string;
  joinedAt: string;
  lastActiveAt: string | null;
  lastLoginAt: string | null;
  assignedTerritories: AdminUserAssignedTerritory[];
  assignedRegions: AdminUserAssignedRegion[];
}

export interface AdminUserAssignedRegion {
  regionId: number;
  regionName: string;
  territoryId: number;
  territoryName: string;
  countryCode?: string | null;
  assignedAt?: string | null;
}

export interface AdminUserRole {
  roleName: string;
  displayName: string;
  assignedScope: string;
  assignedAt?: string | null;
  scopes: AdminUserAccessScope[];
}

export interface AdminUserAccessScope {
  scopeType: string;
  scopeId?: number | null;
  displayName: string;
  countryCode?: string | null;
  assignedAt?: string | null;
}

export interface AdminUserAccountSummary {
  reviewsCount: number;
  reportsMadeCount: number;
  reportedContentCount: number;
  pendingReportsAgainstUserCount: number;
  acceptedReportsAgainstUserCount: number;
  rejectedReportsAgainstUserCount: number;
  contributionsCount: number;
  pendingContributionsCount: number;
  favoritesCount: number;
  collectionsCount: number;
  savedItemsCount: number;
  ownedPlacesCount: number;
  activityEventsCount: number;
  knownSuspensionCount: number;
  lastKnownActivityAt: string | null;
  riskLevel: string;
  riskSummary: string;
}

export interface AdminUserPreviewSection<T> {
  items: T[];
  totalCount: number;
  skip: number;
  take: number;
  hasMore: boolean;
}

export interface AdminUserReportPreview {
  reportId: number;
  contentType: string;
  targetType: string;
  reviewId: number;
  entityId: number;
  entityName: string;
  regionName: string;
  reason: string;
  status: string;
  decision?: string | null;
  actionTaken: string;
  contentExcerpt: string;
  reporterUserId: number;
  reporterName: string;
  reviewAuthorUserId: number;
  reviewAuthorName: string;
  createdAt: string;
  moderationDecisionDate?: string | null;
  detailRoute?: string | null;
}

export interface AdminUserRecentReview {
  reviewId: number;
  targetType: string;
  entityId: number;
  entityName: string;
  rating: number;
  commentExcerpt: string;
  reportCount: number;
  status: string;
  createdAt: string;
  updatedAt?: string | null;
  detailRoute?: string | null;
}

export interface AdminUserModerationHistoryItem {
  eventId: string;
  eventType: string;
  summary: string;
  status: string;
  actorUserId?: number | null;
  actorDisplayName?: string | null;
  createdAt: string;
  note?: string | null;
  source: string;
  sourceId?: number | null;
}

export interface AdminUserPreviewPermissions {
  canAssignTerritoryManager: boolean;
  canSuspend: boolean;
  canUnsuspend: boolean;
  canDelete: boolean;
  canViewReports: boolean;
  canViewModerationHistory: boolean;
}
