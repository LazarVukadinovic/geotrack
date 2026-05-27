export type AdminReviewStatus = 'Active' | 'Reported' | 'Removed' | 'Hidden' | 'Deleted';
export type AdminReviewAuthorStatus = 'Active' | 'Suspended' | 'Deleted' | 'Unknown';
export type AdminReviewStatusFilter = 'reported' | 'active' | 'all';
export type AdminReviewTypeFilter = 'all' | 'destination' | 'location' | 'place' | 'stay' | 'event' | 'comment';
export type AdminReviewSort = 'newest' | 'oldest' | 'reportsDesc' | 'ratingAsc' | 'ratingDesc';

export interface AdminReviewAuthor {
  userId: number;
  displayName: string;
  email: string;
  avatarUrl?: string | null;
  initials: string;
  status: AdminReviewAuthorStatus;
  avatarColor?: string;
}

export interface AdminReviewedEntity {
  entityId: number;
  entityType: string;
  name: string;
  region?: string | null;
  location?: string | null;
  destination?: string | null;
}

export interface AdminReviewReportSummary {
  reportCount: number;
  pendingReportCount: number;
  latestReportReason?: string | null;
  latestReportCreatedAt?: string | null;
  latestReportStatus?: string | null;
}

export interface AdminReviewPermissions {
  canDeleteReview: boolean;
  canSuspendAuthor: boolean;
  canUnsuspendAuthor: boolean;
  canDeleteAuthor: boolean;
  canRemoveReviewAndSuspendAuthor: boolean;
  canRemoveReviewAndDeleteAuthor: boolean;
}

export interface AdminReviewListItem {
  reviewKey: string;
  reviewId: number;
  author: AdminReviewAuthor;
  rating: number;
  maxRating: number;
  content: string;
  createdAt: string;
  updatedAt?: string | null;
  status: AdminReviewStatus;
  reviewedEntity: AdminReviewedEntity;
  reports: AdminReviewReportSummary;
  permissions: AdminReviewPermissions;
}

export interface AdminReviewListCounts {
  reportedCount: number;
  activeCount: number;
  allCount: number;
}

export interface AdminReviewListResponse {
  items: AdminReviewListItem[];
  totalCount: number;
  skip: number;
  take: number;
  nextSkip: number | null;
  hasMore: boolean;
  counts: AdminReviewListCounts;
}

export interface AdminReviewsPage {
  items: AdminReviewListItem[];
  totalCount: number;
  total: number;
  skip: number;
  take: number;
  nextSkip: number | null;
  hasMore: boolean;
  counts: AdminReviewListCounts;
}

export interface AdminReviewsPageRequest {
  status: AdminReviewStatusFilter;
  type: AdminReviewTypeFilter[];
  search: string;
  sort: AdminReviewSort;
  skip: number;
  take: number;
}

export interface AdminReviewReporter {
  userId: number;
  displayName: string;
}

export interface AdminReviewModerator {
  userId: number;
  displayName: string;
}

export interface AdminReviewReport {
  reportId: number;
  reporter: AdminReviewReporter;
  reason: string;
  details?: string | null;
  status: string;
  createdAt: string;
  decidedAt?: string | null;
  decidedBy?: AdminReviewModerator | null;
  decision?: string | null;
  moderatorNote?: string | null;
}

export interface AdminReviewDetailReportSummary {
  totalReports: number;
  pendingReports: number;
  acceptedReports: number;
  rejectedReports: number;
}

export interface AdminReviewModerationHistoryItem {
  action: string;
  moderator?: AdminReviewModerator | null;
  createdAt: string;
  note?: string | null;
  reportId?: number | null;
}

export interface AdminReviewDetail {
  reviewKey: string;
  reviewId: number;
  author: AdminReviewAuthor;
  rating: number;
  maxRating: number;
  content: string;
  createdAt: string;
  updatedAt?: string | null;
  status: AdminReviewStatus;
  reviewedEntity: AdminReviewedEntity;
  allReports: AdminReviewReport[];
  reportSummary: AdminReviewDetailReportSummary;
  moderationHistory: AdminReviewModerationHistoryItem[];
  permissions: AdminReviewPermissions;
}

export interface AdminReviewActionResult {
  reviewKey: string;
  success: boolean;
  message: string;
  counts: AdminReviewListCounts;
}
