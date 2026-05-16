export type AdminReviewStatus = 'Active' | 'Reported' | 'Deleted';

export interface AdminReview {
  id: string;
  targetType: 'Destination' | 'Place' | 'Location' | 'Event';
  targetName: string;
  rating: number;
  content: string;
  authorId: string;
  authorName: string;
  authorEmail: string;
  authorInitials: string;
  authorAvatarColor: string;
  status: AdminReviewStatus;
  reportCount: number;
  reportReason: string | null;
  createdAt: string;
  reportedAt: string | null;
}

export type ReviewSortField = 'createdAt' | 'reportedAt' | 'rating' | 'reportCount';
export type ReviewSortDir = 'asc' | 'desc';
export type ReviewStatusFilter = 'all' | 'reported' | 'active';

export interface AdminReviewsPage {
  items: AdminReview[];
  total: number;
  reportedCount: number;
}

export interface AdminReviewsPageRequest {
  pageSize: number;
  search: string;
  sortField: ReviewSortField;
  sortDir: ReviewSortDir;
  statusFilter: ReviewStatusFilter;
  typeFilter?: string[];
}

export interface ReviewNotification {
  id: string;
  reviewId: string;
  authorName: string;
  targetName: string;
  targetType: string;
  reportedAt: string;
  isRead: boolean;
}
