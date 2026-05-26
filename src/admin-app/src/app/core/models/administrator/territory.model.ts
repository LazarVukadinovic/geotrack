export interface GeoJsonFeature {
  type: 'Feature';
  geometry: GeoJsonGeometry;
  properties: Record<string, unknown> | null;
}

export interface GeoJsonGeometry {
  type: 'Polygon' | 'MultiPolygon';
  coordinates: number[][][] | number[][][][];
}

export interface Country {
  code: string;
  iso3?: string | null;
  name: string;
  continent: string;
  flag: string;
}

export interface Territory {
  id: string;
  territoryId?: number;
  countryId?: number;
  name: string;
  displayName?: string;
  internalReference?: string;
  territoryKind?: TerritoryKind | string | null;
  countryCode?: string | null;
  iso2?: string | null;
  iso3?: string | null;
  countries: string[];
  assignedManagerUserId?: number | null;
  assignedManagerId: string | null;
  assignedManagerName: string | null;
  assignedManagerEmail: string | null;
  status: 'Assigned' | 'Active' | 'Unassigned' | 'MissingBoundary' | 'MissingDisplayGeometry' | 'DisputedViewOnly' | 'DisputedAssigned';
  boundarySource?: string | null;
  boundarySourceUrl?: string | null;
  boundaryShapeType?: string | null;
  boundaryShapeGroup?: string | null;
  boundaryShapeId?: string | null;
  boundaryExternalKey?: string | null;
  boundaryAdmLevel?: string | null;
  boundaryReleaseType?: string | null;
  boundaryLicense?: string | null;
  boundaryImportedAt?: string | null;
  hasBoundary?: boolean;
  zone?: GeoJsonFeature | null;
  createdAt: string;
  updatedAt: string;
}

export interface TerritoryAssignedManager {
  id: string;
  userId: number;
  displayName: string;
  name: string;
  email: string;
  avatarUrl?: string | null;
  initials: string;
  roles: string[];
  assignedAt: string;
}

export interface TerritoryMapAssignedManager {
  id: string;
  userId: number;
  displayName: string;
  name: string;
  email: string;
  initials: string;
}

export interface TerritoryMapBounds {
  north: number;
  south: number;
  east: number;
  west: number;
}

export interface TerritoryMapViewport {
  centerLatitude?: number | null;
  centerLongitude?: number | null;
  zoom?: number | null;
  bounds?: TerritoryMapBounds | null;
  isEmpty: boolean;
}

export interface TerritoryDetail extends Territory {
  assignedManager?: TerritoryAssignedManager | null;
  boundaryGeoJson?: GeoJsonGeometry | null;
  viewport?: TerritoryMapViewport | null;
  boundaryRequiredMessage?: string | null;
  canAssign?: boolean;
}

export interface TerritoryManager {
  id: string;
  userId?: number;
  displayName?: string;
  name: string;
  firstName?: string;
  lastName?: string;
  email: string;
  avatarUrl?: string | null;
  initials: string;
  roles?: string[];
  assignedTerritories?: TerritoryManagerAssignedTerritory[];
  avatarColor: string;
  assignedTerritoryIds: string[];
  status: 'Active' | 'Inactive';
  isActive?: boolean;
  canAssign?: boolean;
  ineligibilityReason?: string | null;
}

export interface CreateTerritoryRequest {
  name: string;
  countries: string[];
  assignedManagerId?: string | null;
  assignedManagerUserId?: number | null;
  boundary?: GeoJsonGeometry | null;
  zone?: GeoJsonFeature | null;
}

export interface TerritoryManagerAssignedTerritory {
  territoryId: number;
  territoryName: string;
  displayName: string;
  countryCode?: string | null;
}

export interface TerritoryPage {
  items: Territory[];
  total: number;
}

export type TerritoryKind = 'Country' | 'DisputedTerritory';
export type TerritoryMapStatus = 'Assigned' | 'Available' | 'MissingBoundary' | 'MissingDisplayGeometry' | 'DisputedViewOnly' | 'DisputedAssigned';

export interface TerritoryMapFeatureCollection {
  type: 'FeatureCollection';
  features: TerritoryMapFeature[];
  attribution?: string | null;
  generatedAt?: string | null;
}

export interface TerritoryMapBoundaryFeatureCollection {
  type: 'FeatureCollection';
  features: TerritoryMapBoundaryFeature[];
  attribution?: string | null;
  generatedAt?: string | null;
}

export interface TerritoryMapBoundaryFeature {
  type: 'Feature';
  geometry: GeoJsonGeometry | null;
  properties: TerritoryMapBoundaryProperties;
}

export interface TerritoryMapBoundaryProperties {
  territoryId?: number | null;
  countryId?: number | null;
  territoryKind?: TerritoryKind | string | null;
  iso2?: string | null;
  iso3?: string | null;
  countryCode?: string | null;
  name: string;
  displayName: string;
  shapeGroup?: string | null;
  shapeType?: string | null;
  shapeId?: string | null;
  hasDisplayBoundary?: boolean;
  viewport?: TerritoryMapViewport | null;
}

export interface TerritoryMapStatusCollection {
  items: TerritoryMapStatusItem[];
  generatedAt?: string | null;
}

export interface TerritoryMapStatusItem {
  territoryId?: number | null;
  countryId?: number | null;
  territoryKind?: TerritoryKind | string | null;
  iso2?: string | null;
  iso3?: string | null;
  countryCode?: string | null;
  name: string;
  displayName: string;
  status: TerritoryMapStatus;
  shapeGroup?: string | null;
  shapeType?: string | null;
  shapeId?: string | null;
  assignedManager?: TerritoryMapAssignedManager | null;
  hasBoundary: boolean;
  hasValidationBoundary?: boolean;
  hasDisplayBoundary?: boolean;
  canAssign: boolean;
  canEdit: boolean;
  boundaryRequiredMessage?: string | null;
  boundaryMissingReason?: string | null;
}

export interface TerritoryMapFeature {
  type: 'Feature';
  geometry: GeoJsonGeometry | null;
  properties: TerritoryMapFeatureProperties;
}

export interface TerritoryMapFeatureProperties {
  territoryId?: number | null;
  countryId?: number | null;
  territoryKind?: TerritoryKind | string | null;
  iso2?: string | null;
  iso3?: string | null;
  countryCode?: string | null;
  name: string;
  displayName: string;
  status: TerritoryMapStatus;
  shapeGroup?: string | null;
  shapeType?: string | null;
  shapeId?: string | null;
  assignedManager?: TerritoryMapAssignedManager | null;
  hasBoundary: boolean;
  hasValidationBoundary?: boolean;
  hasDisplayBoundary?: boolean;
  canAssign: boolean;
  canEdit: boolean;
  boundaryRequiredMessage?: string | null;
  boundaryMissingReason?: string | null;
  boundarySource?: string | null;
  boundarySourceUrl?: string | null;
  boundaryShapeType?: string | null;
  boundaryShapeGroup?: string | null;
  boundaryShapeId?: string | null;
  boundaryExternalKey?: string | null;
  boundaryAdmLevel?: string | null;
  boundaryReleaseType?: string | null;
  boundaryLicense?: string | null;
  boundaryImportedAt?: string | null;
  viewport?: TerritoryMapViewport | null;
  createdAt?: string | null;
  updatedAt?: string | null;
}

export type TerritoryPageSortField = 'name' | 'status' | 'countries' | 'createdAt';
export type TerritoryPageSortDir = 'asc' | 'desc';

export interface TerritoryPageRequest {
  page: number;
  pageSize: number;
  search: string;
  sortField: TerritoryPageSortField;
  sortDir: TerritoryPageSortDir;
  countryFilter: string[];
}
