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
  countryCode?: string | null;
  countries: string[];
  assignedManagerUserId?: number | null;
  assignedManagerId: string | null;
  assignedManagerName: string | null;
  assignedManagerEmail: string | null;
  status: 'Assigned' | 'Active' | 'Unassigned';
  boundarySource?: string | null;
  boundaryShapeType?: string | null;
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
