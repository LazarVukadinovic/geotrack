export type AdminNotificationSeverity = 'Info' | 'Warning' | 'Critical';
export type AdminNotificationFilter = 'all' | 'unread';

export interface AdminNotification {
  id: number;
  type: string;
  title: string;
  message: string;
  severity: AdminNotificationSeverity;
  isRead: boolean;
  readAt?: string | null;
  createdAt: string;
  targetType: string;
  targetId?: number | null;
  targetRoute?: string | null;
  metadataSummary?: string | null;
}

export interface AdminNotificationListResponse {
  items: AdminNotification[];
  unreadCount: number;
  totalCount: number;
  skip: number;
  take: number;
  nextSkip?: number | null;
  hasMore: boolean;
}

export interface AdminNotificationPage extends AdminNotificationListResponse {
  total: number;
}

export interface AdminNotificationListRequest {
  includeRead: boolean;
  skip: number;
  take: number;
}

export interface AdminNotificationUnreadCountResponse {
  unreadCount: number;
}

export interface AdminNotificationMutationResponse {
  unreadCount: number;
  readAt?: string | null;
}
