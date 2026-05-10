export type PlatformUserRole = 'Regional Manager' | 'Viewer';
export type PlatformUserStatus = 'Active' | 'Inactive';

export interface PlatformUser {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  avatarUrl?: string;
  avatarInitials: string;
  avatarColor: string;
  role: PlatformUserRole;
  status: PlatformUserStatus;
  assignedRegions: string[];
  lastLogin: string; // ISO date string
  createdAt: string;
}