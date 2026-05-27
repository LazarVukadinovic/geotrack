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
  staysCount: number;
  eventsCount: number;
  pendingRequestsCount: number;
  healthScore: number;
  health: TerritoryHealth;
  activityScore: number;
  assignedManagers: TerritoryRegionManager[];
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
  regionId: number;
  name: string;
  regionName: string;
  displayName: string;
  description?: string | null;
  territoryName: string;
  territoryId: number;
  territoryDisplayName: string;
  status: string;
  managerNames: string[];
  destinationsCount: number;
  locationsCount: number;
  placesCount: number;
  staysCount: number;
  eventsCount: number;
  pendingRequestsCount: number;
  assignedPersonsCount: number;
  activeManagersCount: number;
  managerDisplayName?: string | null;
  assignedManagers: TerritoryRegionManager[];
  healthScore?: number | null;
  health?: TerritoryHealth | null;
  activityScore: number;
  imageUrl?: string | null;
  detailRoute?: string | null;
  createdAt: string;
}

export interface TerritoryRegionDirectoryResponse {
  territory?: TerritoryAssignedTerritory | null;
  items: TerritoryRegionDirectoryItem[];
  totalCount: number;
  skip: number;
  take: number;
  hasMore: boolean;
}

export interface TerritoryRegionBoundaryCoordinate {
  latitude: number;
  longitude: number;
}

export interface TerritoryRegionBoundary {
  shapeType?: 'Polygon' | 'Rectangle' | string | null;
  coordinates: TerritoryRegionBoundaryCoordinate[];
}

export interface TerritoryRegionCreateRequest {
  territoryId: number;
  name?: string | null;
  candidateExternalKey?: string | null;
  boundaryCandidateId?: string | null;
  boundaryExternalKey?: string | null;
  shapeGroup?: string | null;
  shapeId?: string | null;
  admLevel?: string | null;
  boundarySource?: 'ManualDraw' | 'OsmBoundary' | string | null;
  osmRelationId?: number | null;
  osmAdminLevel?: number | null;
  boundary?: TerritoryRegionBoundary | null;
}

export interface TerritoryRegionCreateResponse {
  id: number;
  regionId: number;
  name: string;
  displayName: string;
  territoryId: number;
  territoryDisplayName: string;
  status: string;
  boundarySource?: string | null;
  boundaryShapeType?: string | null;
  boundaryShapeGroup?: string | null;
  boundaryShapeId?: string | null;
  boundaryExternalKey?: string | null;
  boundaryAdmLevel?: string | null;
  boundaryReleaseType?: string | null;
  boundaryCandidateId?: number | null;
  boundaryGeometryGeoJson?: TerritoryRegionGeometry | null;
  createdAt: string;
  detailRoute?: string | null;
}

export interface TerritoryRegionCreateContext {
  territory?: TerritoryAssignedTerritory | null;
  territoryBoundaryGeoJson?: TerritoryRegionGeometry | null;
  existingRegions: TerritoryRegionMapItem[];
  viewport: TerritoryRegionMapViewport;
  canCreate: boolean;
  boundaryRequiredMessage?: string | null;
}

export interface TerritoryRegionMapItem {
  featureKind?: 'ExistingRegion' | string;
  id: number;
  regionId: number;
  name: string;
  displayName: string;
  geometryGeoJson?: TerritoryRegionGeometry | null;
  centerLatitude?: number | null;
  centerLongitude?: number | null;
}

export interface TerritoryRegionGeometry {
  type: 'Polygon' | 'MultiPolygon' | string;
  coordinates: unknown;
}

export interface TerritoryRegionMapBounds {
  north: number;
  south: number;
  east: number;
  west: number;
}

export interface TerritoryRegionMapViewport {
  centerLatitude?: number | null;
  centerLongitude?: number | null;
  zoom?: number | null;
  bounds?: TerritoryRegionMapBounds | null;
  isEmpty: boolean;
}

export interface BoundaryRegionCandidateResponse {
  source: string;
  admLevel: string;
  iso3: string;
  scannedFeatures: number;
  matchedFeatures: number;
  returnedCount: number;
  items: BoundaryRegionCandidate[];
  attribution?: string | null;
}

export interface BoundaryRegionCandidate {
  featureKind?: 'Adm1Candidate' | string;
  candidateId: string;
  boundaryCandidateId: number;
  externalKey: string;
  boundaryExternalKey: string;
  admLevel: string;
  iso3: string;
  countryIso3: string;
  shapeGroup?: string | null;
  shapeId?: string | null;
  shapeName: string;
  shapeType: string;
  name: string;
  displayName: string;
  status: 'Available' | 'ExistingRegion' | 'InvalidOutsideTerritory' | string;
  hasBoundary: boolean;
  hasDisplayBoundary: boolean;
  canSelect: boolean;
  existingRegionId?: number | null;
  existingRegionName?: string | null;
  isInsideAssignedTerritory: boolean;
  validationMessage?: string | null;
  viewport?: TerritoryRegionMapViewport | null;
  geometryGeoJson?: TerritoryRegionGeometry | null;
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
  publicDetailRoute?: string | null;
}

export interface TerritoryContentDirectoryResponse {
  entityType: string;
  items: TerritoryContentDirectoryItem[];
  totalCount: number;
  skip: number;
  take: number;
  hasMore: boolean;
}

export interface TerritoryHealthInsight {
  key?: string | null;
  label: string;
  title?: string | null;
  detail: string;
  message?: string | null;
  severity: 'info' | 'positive' | 'warning' | 'action' | string;
  scoreContribution?: number | null;
  maxPoints?: number | null;
  status?: string | null;
  recommendationKey?: string | null;
  priority?: number | null;
  actionRoute?: string | null;
}

export interface TerritoryHealth {
  score?: number | null;
  status?: string | null;
  statusLabel: string;
  periodLabel?: string | null;
  calculatedAt?: string | null;
  drivers: TerritoryHealthInsight[];
  issues: TerritoryHealthInsight[];
  recommendations: TerritoryHealthInsight[];
}

export interface TerritoryRegionDetailRegion {
  id: number;
  name: string;
  displayName: string;
  description?: string | null;
  status: string;
  territoryId: number;
  territoryDisplayName: string;
  imageUrl?: string | null;
  centerLatitude?: number | null;
  centerLongitude?: number | null;
}

export interface TerritoryRegionManager {
  userId: number;
  displayName: string;
  email: string;
  initials: string;
  avatarUrl?: string | null;
  role: string;
  assignedAt: string;
}

export interface TerritoryRegionSummary {
  destinationsCount: number;
  locationsCount: number;
  placesCount: number;
  staysCount: number;
  eventsCount: number;
  pendingRequestsCount: number;
  activeUsers: number;
  visits: number;
  saves: number;
  reviews: number;
  averageRating: number;
}

export interface TerritoryRegionContentItem {
  id: number;
  entityType: TerritoryRegionContentType | string;
  publicDetailRoute?: string | null;
  name: string;
  summary?: string | null;
  description?: string | null;
  status: string;
  regionId: number;
  regionName: string;
  destinationId?: number | null;
  destinationName?: string | null;
  locationId?: number | null;
  locationName?: string | null;
  placeId?: number | null;
  placeName?: string | null;
  parentName?: string | null;
  typeName?: string | null;
  createdAt: string;
  updatedAt?: string | null;
  startsAt?: string | null;
  locationsCount: number;
  placesCount: number;
  staysCount: number;
  eventsCount: number;
  views: number;
  saves: number;
  externalClicks: number;
  reviewsCount: number;
  averageRating?: number | null;
  imageUrl?: string | null;
  health?: TerritoryHealth | null;
}

export type TerritoryRegionContentType = 'destinations' | 'locations' | 'places' | 'stays' | 'events' | 'all';

export interface TerritoryRegionContentResponse {
  entityType: TerritoryRegionContentType | string;
  items: TerritoryRegionContentItem[];
  totalCount: number;
  skip: number;
  take: number;
  hasMore: boolean;
}

export interface TerritoryRegionContentPreview {
  destinations: TerritoryRegionContentItem[];
  locations: TerritoryRegionContentItem[];
  places: TerritoryRegionContentItem[];
  stays: TerritoryRegionContentItem[];
  events: TerritoryRegionContentItem[];
}

export interface TerritoryRegionTopEntity {
  entityType: string;
  entityId: number;
  name: string;
  views: number;
  imageUrl?: string | null;
}

export interface TerritoryRegionActivity {
  eventType: string;
  entityType?: string | null;
  entityId?: number | null;
  entityName?: string | null;
  occurredAt: string;
}

export interface TerritoryRegionAnalytics {
  topEntities: TerritoryRegionTopEntity[];
  recentActivity: TerritoryRegionActivity[];
}

export interface TerritoryRegionDetail {
  region: TerritoryRegionDetailRegion;
  managers: TerritoryRegionManager[];
  summary: TerritoryRegionSummary;
  health: TerritoryHealth;
  content: TerritoryRegionContentPreview;
  analytics: TerritoryRegionAnalytics;
}
