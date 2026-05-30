export interface AdminProfileUser {
  id: number;
  firstName: string;
  lastName: string;
  displayName?: string | null;
  email: string;
  shortBio?: string | null;
  avatarUrl?: string | null;
  avatarVersion?: number | null;
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

export interface AdminProfileRole {
  roleCode: string;
  roleDisplayName: string;
}

export interface AdminProfileAssignmentItem {
  id: number;
  name: string;
  displayName: string;
  assignedAt?: string | null;
}

export interface AdminProfileAssignment {
  assignmentType: string;
  sectionTitle: string;
  label: string;
  displayValue: string;
  accessLevel?: string | null;
  assignedItems: AdminProfileAssignmentItem[];
  memberSince?: string | null;
}

export interface AdminProfilePreferences {
  interfaceLanguage: string;
  timezone: string;
  emailNotificationsAvailable?: boolean;
  emailRequestsEnabled: boolean;
  emailNewReviewEnabled: boolean;
}

export interface AdminProfile {
  user: AdminProfileUser;
  roles: string[];
  role?: AdminProfileRole | null;
  assignment?: AdminProfileAssignment | null;
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
  avatarVersion?: number | null;
  updatedAt?: string | null;
}
