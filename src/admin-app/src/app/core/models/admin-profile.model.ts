export interface AdminProfileUser {
  id: number;
  firstName: string;
  lastName: string;
  displayName?: string | null;
  email: string;
  shortBio?: string | null;
  avatarUrl?: string | null;
  createdAt: string;
  updatedAt?: string | null;
}

export interface AdminProfileRegion {
  id: number;
  name: string;
  displayName: string;
  territoryName?: string | null;
  assignedAt: string;
}

export interface AdminProfileRegionalManager {
  assignedRegions: AdminProfileRegion[];
  memberSince?: string | null;
}

export interface AdminProfilePreferences {
  interfaceLanguage: string;
  timezone: string;
  emailRequestsEnabled: boolean;
  emailNewReviewEnabled: boolean;
}

export interface AdminProfile {
  user: AdminProfileUser;
  roles: string[];
  regionalManager: AdminProfileRegionalManager;
  preferences: AdminProfilePreferences;
}

export interface UpdateAdminProfileRequest {
  firstName: string;
  lastName: string;
  shortBio: string | null;
  interfaceLanguage: string;
  timezone: string;
  emailRequestsEnabled: boolean;
  emailNewReviewEnabled: boolean;
}

export interface AdminProfileAvatarResponse {
  avatarUrl: string;
  updatedAt?: string | null;
}
