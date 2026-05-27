export type AdminSupportTicketStatus = 'Open' | 'InProgress' | 'Resolved' | 'Dismissed';
export type AdminSupportTicketStatusFilter = 'open' | 'inProgress' | 'resolved' | 'dismissed' | 'all';
export type AdminSupportTicketCategoryFilter =
  | 'all'
  | 'account-access'
  | 'saved-places-and-collections'
  | 'location-and-maps'
  | 'ask-agent'
  | 'events-and-stays'
  | 'privacy-and-data';
export type AdminSupportTicketPriority = 'Low' | 'Normal' | 'High' | 'Critical';
export type AdminSupportTicketPriorityFilter = 'all' | 'low' | 'normal' | 'high' | 'critical';
export type AdminSupportTicketSort = 'newest' | 'oldest' | 'priorityDesc' | 'updatedDesc';

export interface AdminSupportTicketReporter {
  userId?: number | null;
  displayName: string;
  email: string;
  avatarUrl?: string | null;
  initials: string;
  isAnonymous: boolean;
  avatarColor?: string;
}

export interface AdminSupportTicketAdminSummary {
  userId: number;
  displayName: string;
  email: string;
}

export interface AdminSupportTicketPermissions {
  canView: boolean;
  canMarkInProgress: boolean;
  canResolve: boolean;
  canDismiss: boolean;
  canReopen: boolean;
  canAddNote: boolean;
}

export interface AdminSupportTicketHistoryItem {
  action: string;
  actor?: AdminSupportTicketAdminSummary | null;
  createdAt: string;
  note?: string | null;
}

export interface AdminSupportTicketListItem {
  ticketId: number;
  referenceCode: string;
  reporter: AdminSupportTicketReporter;
  category: string;
  categoryLabel: string;
  subject: string;
  messagePreview: string;
  status: AdminSupportTicketStatus;
  priority: AdminSupportTicketPriority;
  pageUrl?: string | null;
  appRoute?: string | null;
  createdAt: string;
  updatedAt?: string | null;
  resolvedAt?: string | null;
  hasAttachment: boolean;
  internalNotePreview?: string | null;
  permissions: AdminSupportTicketPermissions;
}

export interface AdminSupportTicketDetail extends AdminSupportTicketListItem {
  message: string;
  userAgent?: string | null;
  deviceInfo?: string | null;
  browser?: string | null;
  platform?: string | null;
  attachmentUrl?: string | null;
  resolvedBy?: AdminSupportTicketAdminSummary | null;
  assignedAdmin?: AdminSupportTicketAdminSummary | null;
  internalNote?: string | null;
  resolutionMessage?: string | null;
  statusHistory: AdminSupportTicketHistoryItem[];
}

export interface AdminSupportTicketListCounts {
  openCount: number;
  inProgressCount: number;
  resolvedCount: number;
  dismissedCount: number;
  allCount: number;
}

export interface AdminSupportTicketListResponse {
  items: AdminSupportTicketListItem[];
  totalCount: number;
  skip: number;
  take: number;
  nextSkip: number | null;
  hasMore: boolean;
  counts: AdminSupportTicketListCounts;
}

export interface AdminSupportTicketsPage extends AdminSupportTicketListResponse {
  total: number;
}

export interface AdminSupportTicketPageRequest {
  status: AdminSupportTicketStatusFilter;
  category: AdminSupportTicketCategoryFilter;
  priority: AdminSupportTicketPriorityFilter;
  search: string;
  sort: AdminSupportTicketSort;
  skip: number;
  take: number;
}

export interface AdminSupportTicketStatusUpdateRequest {
  status: AdminSupportTicketStatus;
  internalNote?: string | null;
  resolutionMessage?: string | null;
}
