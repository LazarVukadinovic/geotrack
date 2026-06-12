export type AdminMapEntityType = 'Region' | 'Destination' | 'Location' | 'Place' | 'Event';

export function buildPlaceReturnQueryParams(draft: unknown): Record<string, string> {
  const placeDraft = draft as {
    activeRegionId?: number | null;
    pinLat?: string;
    pinLng?: string;
  } | null | undefined;

  const queryParams: Record<string, string> = {};
  if (placeDraft?.activeRegionId) {
    queryParams['regionId'] = String(placeDraft.activeRegionId);
  }
  if (placeDraft?.pinLat && placeDraft?.pinLng) {
    queryParams['latitude'] = placeDraft.pinLat;
    queryParams['longitude'] = placeDraft.pinLng;
    queryParams['source'] = 'map';
  }

  return queryParams;
}

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

export interface AdminMapAssignedRegionBoundary {
  regionId: number;
  regionName: string;
  displayName: string;
  geometrySource: string;
  hasBoundary: boolean;
  geometryGeoJson?: AdminMapGeometry | null;
  viewport: AdminMapViewport;
  message?: string | null;
}

export interface AdminMapZone {
  id: number;
  entityType: Exclude<AdminMapEntityType, 'Place'>;
  name: string;
  displayName: string;
  status?: string | null;
  geometryGeoJson?: AdminMapGeometry | null;
  geometrySource?: string | null;
  isGeometryFallback?: boolean;
  boundaryNotice?: string | null;
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
  assignedRegionBoundary?: AdminMapAssignedRegionBoundary | null;
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

export type RegionalScopeValidationMode =
  | 'InsideAssignedRegion'
  | 'AllowedWithinToleranceInsideAssignedCountry'
  | 'RejectedOutsideAssignedCountry'
  | 'AllowedWithinToleranceOutsideRegion'
  | 'RejectedInsideAnotherRegion'
  | 'RejectedTooFarOutsideRegion'
  | 'RejectedMissingAssignedRegion'
  | 'RejectedMissingAssignedCountryBoundary'
  | 'RejectedMissingRegionBoundary'
  | 'RejectedMissingBoundary';

export interface RegionalEntityLocationValidationRequest {
  regionId: number;
  entityType: 'Destination' | 'Location' | 'Place' | 'Event' | string;
  operationType?: 'Create' | 'Update' | string | null;
  latitude: number;
  longitude: number;
  destinationId?: number | null;
  locationId?: number | null;
  placeId?: number | null;
  parentEntityType?: string | null;
}

export interface RegionalScopeValidationResult {
  isAllowed: boolean;
  validationMode: RegionalScopeValidationMode | string;
  assignedRegionId?: number | null;
  assignedRegionName?: string | null;
  assignedCountryOrTerritoryId?: number | null;
  assignedCountryOrTerritoryName?: string | null;
  matchedOtherRegionId?: number | null;
  matchedOtherRegionName?: string | null;
  distanceFromAssignedRegionMeters?: number | null;
  message: string;
}
