export type AdminMapEntityType = 'Region' | 'Destination' | 'Location' | 'Place';

export interface AdminMapBounds {
  north: number;
  south: number;
  east: number;
  west: number;
}

export interface AdminMapViewport {
  centerLatitude?: number | null;
  centerLongitude?: number | null;
  zoom?: number | null;
  bounds?: AdminMapBounds | null;
  isEmpty: boolean;
}

export interface AdminMapGeometry {
  type: 'Polygon' | 'MultiPolygon';
  coordinates: number[][][] | number[][][][];
}

export interface AdminMapZone {
  id: number;
  entityType: Exclude<AdminMapEntityType, 'Place'>;
  name: string;
  displayName: string;
  status?: string | null;
  geometryGeoJson?: AdminMapGeometry | null;
  centerLatitude?: number | null;
  centerLongitude?: number | null;
  detailRoute?: string | null;
  editRoute?: string | null;
}

export interface AdminMapMarker {
  id: number;
  entityType: Exclude<AdminMapEntityType, 'Region'>;
  title: string;
  subtitle?: string | null;
  categoryLabel?: string | null;
  latitude: number;
  longitude: number;
  imageUrl?: string | null;
  status: string;
  regionId: number;
  destinationId?: number | null;
  locationId?: number | null;
  placeId?: number | null;
  detailRoute?: string | null;
  editRoute?: string | null;
}

export interface AdminMapResponse {
  viewport: AdminMapViewport;
  markers: AdminMapMarker[];
  zones: AdminMapZone[];
  emptyStateMessage?: string | null;
}

export interface AdminMapSearchResponse {
  items: AdminMapSearchResult[];
}

export interface AdminMapSearchResult {
  id?: number | null;
  entityType: Exclude<AdminMapEntityType, 'Region'>;
  title: string;
  subtitle?: string | null;
  categoryLabel?: string | null;
  latitude: number;
  longitude: number;
  imageUrl?: string | null;
  detailRoute?: string | null;
}
