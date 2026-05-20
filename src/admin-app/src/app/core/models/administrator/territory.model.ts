export interface GeoJsonFeature {
  type: 'Feature';
  geometry: {
    type: 'Polygon' | 'MultiPolygon';
    coordinates: number[][][] | number[][][][];
  };
  properties: Record<string, unknown> | null;
}

export interface Country {
  code: string;
  name: string;
  continent: string;
  flag: string;
}

export interface Territory {
  id: string;
  name: string;
  countries: string[];
  assignedManagerId: string | null;
  assignedManagerName: string | null;
  assignedManagerEmail: string | null;
  status: 'Active' | 'Unassigned';
  zone?: GeoJsonFeature | null;
  createdAt: string;
  updatedAt: string;
}

export interface TerritoryManager {
  id: string;
  name: string;
  email: string;
  initials: string;
  avatarColor: string;
  assignedTerritoryIds: string[];
  status: 'Active' | 'Inactive';
}

export interface CreateTerritoryRequest {
  name: string;
  countries: string[];
  assignedManagerId: string | null;
  zone?: GeoJsonFeature | null;
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
