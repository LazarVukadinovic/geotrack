export type UserRole = 'Administrator' | 'Content Moderator' | 'Territory Manager' | 'Territory Associate' | 'Regional Manager';
export type AdminRoleCode = 'Admin' | 'ContentModerator' | 'TerritoryManager' | 'TerritoryAssociate' | 'RegionalManager';

export interface User {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  role: UserRole;
  activeRoleCode: AdminRoleCode;
  roles: AdminRoleCode[];
  region: string;
  memberSince: string;
  bio: string;
  language: string;
  timezone: string;
  notifyRequests: boolean;
  notifyNewReview: boolean;
  avatarUrl?: string;
  avatarVersion?: number | null;
}
