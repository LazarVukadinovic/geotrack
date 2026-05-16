export type AppUserStatus = 'Active' | 'Suspended';

export interface AppUser {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  avatarInitials: string;
  avatarColor: string;
  status: AppUserStatus;
  reviewCount: number;
  reportedReviewCount: number;
  joinedAt: string;
  lastActiveAt: string;
}

export type AppUserSortField = 'name' | 'joinedAt' | 'reviewCount' | 'reportedReviewCount' | 'status';
export type AppUserSortDir = 'asc' | 'desc';

export interface AppUsersPage {
  items: AppUser[];
  total: number;
}

export interface AppUsersPageRequest {
  pageSize: number;
  search: string;
  sortField: AppUserSortField;
  sortDir: AppUserSortDir;
  statusFilter: 'all' | 'active' | 'suspended';
}
