export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}

export interface ChangePasswordResponse {
  message: string;
}

export interface NotificationPreferences {
  generalNotifications: boolean;
  travelRecommendations: boolean;
  nearbyEvents: boolean;
  collectionUpdates: boolean;
  emailNotifications: boolean;
}

export interface AnalyticsPreferences {
  analyticsEnabled: boolean;
}

export interface SupportRequestItem {
  id: number;
  referenceCode: string;
  category: string;
  subject: string;
  message: string;
  status: string;
  createdAt: string;
}

export interface SupportRequestsResponse {
  requests: SupportRequestItem[];
}

export interface CreateSupportRequestRequest {
  category: string;
  subject: string;
  message: string;
}

export interface CreateSupportRequestResponse extends SupportRequestItem {}
