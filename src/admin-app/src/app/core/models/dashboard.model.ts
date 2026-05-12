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

export interface RegionalDashboardStats {
  pendingRequests: number;
  totalDestinations: number;
  destinationsAddedLastMonth: number;
  totalPlaces: number;
  placesAddedLastMonth: number;
  totalEvents: number;
  eventsAddedLastMonth: number;
}

export interface RegionalRequestQueuePreviewItem {
  requestId: number;
  title: string;
  submittedByDisplayName: string;
  entityType: 'Place';
  contributionType: string;
  submittedAt: string;
  status: string;
  thumbnailImageUrl?: string | null;
  detailRoute: string;
}

export interface RegionalDashboardActivityItem {
  id: string;
  entityType: 'place' | 'destination' | 'location' | 'event' | 'request';
  entityId?: number | null;
  action: string;
  description: string;
  actorDisplayName?: string | null;
  actorInitials?: string | null;
  occurredAt: string;
  status?: string | null;
  detailRoute?: string | null;
}

export interface RegionalDashboard {
  activeRegion: RegionalAssignedRegion;
  stats: RegionalDashboardStats;
  requestQueuePreview: RegionalRequestQueuePreviewItem[];
  recentActivity: RegionalDashboardActivityItem[];
}

export interface RegionalDashboardActivityResponse {
  items: RegionalDashboardActivityItem[];
  skip: number;
  take: number;
  hasMore: boolean;
}
