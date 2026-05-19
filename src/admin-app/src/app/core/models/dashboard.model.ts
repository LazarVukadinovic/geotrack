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

export interface RegionalSidebarSummary {
  assignedRegions: RegionalAssignedRegion[];
  activeRegion?: RegionalAssignedRegion | null;
  pendingRequestsCount: number;
}

export interface TerritoryAssignedTerritory {
  id: number;
  name: string;
  displayName: string;
  countryCode?: string | null;
  statusLabel?: string | null;
}

export interface TerritorySidebarSummary {
  assignedTerritories: TerritoryAssignedTerritory[];
  activeTerritory?: TerritoryAssignedTerritory | null;
}

export interface TerritoryDashboardStats {
  totalRegions: number;
  regionsAddedLastMonth: number;
  activeUsers: number;
  activeUsersChangePercent: number;
  totalDestinations: number;
  destinationsAddedLastMonth: number;
  totalLocations: number;
  locationsAddedLastMonth: number;
  totalPlaces: number;
  placesAddedLastMonth: number;
  totalEvents: number;
  eventsAddedLastMonth: number;
}

export interface TerritoryRegionHealth {
  regionId: number;
  regionName: string;
  managerDisplayName?: string | null;
  destinationsCount: number;
  locationsCount: number;
  placesCount: number;
  eventsCount: number;
  pendingRequestsCount: number;
  activityScore: number;
  assignedManagersPreview: string[];
}

export interface TerritoryTopPerformingRegion {
  regionId: number;
  regionName: string;
  visitsThisMonth: number;
  changePercent: number;
  rank: number;
}

export interface TerritoryTopContentItem {
  id: number;
  name: string;
  regionName: string;
  metricValue: number;
  metricLabel: string;
  imageUrl?: string | null;
  detailRoute?: string | null;
  startsAt?: string | null;
}

export interface TerritoryTopContent {
  topDestinations: TerritoryTopContentItem[];
  topLocations: TerritoryTopContentItem[];
  topPlaces: TerritoryTopContentItem[];
  upcomingEvents: TerritoryTopContentItem[];
}

export interface TerritoryDashboardResponse {
  activeTerritory?: TerritoryAssignedTerritory | null;
  stats: TerritoryDashboardStats;
  regionsHealth: TerritoryRegionHealth[];
  topPerformingRegions: TerritoryTopPerformingRegion[];
  topContent: TerritoryTopContent;
}

export interface TerritoryRegionDirectoryItem {
  id: number;
  name: string;
  territoryName: string;
  destinationsCount: number;
  locationsCount: number;
  placesCount: number;
  eventsCount: number;
  pendingRequestsCount: number;
  activeManagersCount: number;
  managerDisplayName?: string | null;
  createdAt: string;
}

export interface TerritoryRegionDirectoryResponse {
  items: TerritoryRegionDirectoryItem[];
  totalCount: number;
  skip: number;
  take: number;
  hasMore: boolean;
}

export interface TerritoryContentDirectoryItem {
  id: number;
  entityType: string;
  name: string;
  regionName: string;
  parentName?: string | null;
  typeName?: string | null;
  status: string;
  createdAt: string;
  updatedAt?: string | null;
  startsAt?: string | null;
  viewsLast30Days: number;
  imageUrl?: string | null;
  detailRoute?: string | null;
}

export interface TerritoryContentDirectoryResponse {
  entityType: string;
  items: TerritoryContentDirectoryItem[];
  totalCount: number;
  skip: number;
  take: number;
  hasMore: boolean;
}
