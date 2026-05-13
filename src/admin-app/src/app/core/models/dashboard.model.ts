export interface DashboardStat {
  label: string;
  value: string;
  sub: string;
  icon: string;
  highlight: boolean;
  route: string;
}

export interface QuickAction {
  label: string;
  desc: string;
  icon: string;
  route: string;
}

export interface RegionalAssignedRegion {
  id: number;
  name: string;
  displayName: string;
  territoryName?: string | null;
  statusLabel?: string | null;
}

export type RegionalDashboardRegion = RegionalAssignedRegion;

export interface RegionalDashboardStats {
  pendingRequests: number;
  totalDestinations: number;
  destinationsAddedLastMonth: number;
  totalPlaces: number;
  placesAddedLastMonth: number;
  totalEvents: number;
  eventsAddedLastMonth: number;
}

export interface RegionalRequestPreview {
  requestKey?: string;
  requestId: number;
  source?: string;
  title: string;
  submittedByDisplayName: string;
  entityType: string;
  contributionType: string;
  submittedAt: string;
  status: string;
  thumbnailImageUrl?: string | null;
  detailRoute?: string | null;
}

export interface RegionalActivityItem {
  id: string;
  entityType: string;
  entityId?: number | null;
  action: string;
  description: string;
  actorDisplayName?: string | null;
  actorInitials?: string | null;
  occurredAt: string;
  status?: string | null;
  detailRoute?: string | null;
}

export type RegionalRequestQueuePreviewItem = RegionalRequestPreview;
export type RegionalDashboardActivityItem = RegionalActivityItem;

export interface RegionalDashboardResponse {
  activeRegion: RegionalDashboardRegion;
  stats: RegionalDashboardStats;
  requestQueuePreview: RegionalRequestPreview[];
  recentActivity: RegionalActivityItem[];
}

export type RegionalDashboard = RegionalDashboardResponse;

export interface RegionalDashboardActivityResponse {
  items: RegionalActivityItem[];
  skip: number;
  take: number;
  hasMore: boolean;
}
