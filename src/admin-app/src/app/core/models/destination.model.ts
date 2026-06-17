export type DestinationSort = 'newest' | 'oldest' | 'nameAsc' | 'nameDesc';
export type DestinationStatusFilter = 'all' | 'published' | 'draft' | 'archived';

export interface RegionalDestinationListItemDto {
  id: number;
  name: string;
  summary?: string | null;
  description?: string | null;
  status: string;
  internalReferenceId?: string | null;
  regionId: number;
  regionDisplayName: string;
  destinationTypeId: number;
  destinationTypeName?: string | null;
  createdByUserId?: number | null;
  boundaryNote?: string | null;
  boundaryShapeType?: string | null;
  hasBoundary?: boolean;
  boundarySource?: string | null;
  boundaryExternalKey?: string | null;
  boundaryDisplayName?: string | null;
  boundaryImportedAt?: string | null;
  tags?: RegionalDestinationTagDto[];
  lastUpdatedAt?: string | null;
  createdAt?: string | null;
  publishedAt?: string | null;
  imageUrl?: string | null;
  cardImageUrl?: string | null;
  coverImageUrl?: string | null;
  galleryImageUrls?: string[];
  detailRoute?: string | null;
  editRoute?: string | null;
}

export interface RegionalDestinationDetailDto extends RegionalDestinationListItemDto {
  latitude: number;
  longitude: number;
  canDelete?: boolean;
}

export interface RegionalDestinationListResponse {
  items: RegionalDestinationListItemDto[];
  totalCount: number;
  skip: number;
  take: number;
}

export interface RegionalDestinationListParams {
  regionId: number;
  search?: string | null;
  status?: DestinationStatusFilter;
  destinationTypeId?: number | null;
  tagIds?: number[];
  sort?: DestinationSort;
  skip?: number;
  take?: number;
}

export interface RegionalDestinationTagDto {
  id: number;
  name: string;
  slug: string;
  tagTypeId?: number | null;
  tagTypeName?: string | null;
  tagTypeSlug?: string | null;
  tagTypeLabel?: string | null;
  tagTypeOrder?: number | null;
  isFilterable?: boolean;
}

export interface RegionalDestinationCoordinateDto {
  latitude: number;
  longitude: number;
}

export interface RegionalDestinationBoundaryDto {
  shapeType?: string | null;
  coordinates?: RegionalDestinationCoordinateDto[];
  geoJson?: string | null;
  source?: 'OpenStreetMap' | 'UploadedGeoJson' | 'Manual' | string | null;
  externalKey?: string | null;
  displayName?: string | null;
}

export interface RegionalDestinationBoundarySuggestionDto {
  source: string;
  externalKey?: string | null;
  displayName: string;
  category?: string | null;
  providerType?: string | null;
  geometryType: string;
  geoJson: string;
  centerLatitude: number;
  centerLongitude: number;
}

export interface RegionalDestinationBoundarySuggestionsDto {
  items: RegionalDestinationBoundarySuggestionDto[];
}

export interface DestinationCoordinateResolutionDto {
  isResolved: boolean;
  isAmbiguous: boolean;
  destinationId?: number | null;
  destinationName?: string | null;
  regionId?: number | null;
  regionName?: string | null;
  message: string;
}

export interface RegionalDestinationCreateRequest {
  regionId: number;
  name: string;
  summary?: string | null;
  description?: string | null;
  status: 'Published' | 'Draft';
  latitude?: number | null;
  longitude?: number | null;
  boundaryNote?: string | null;
  boundary?: RegionalDestinationBoundaryDto | null;
  tagIds: number[];
  cardImageFile?: File | null;
  coverImageFile?: File | null;
  galleryImageFiles?: File[];
}

export interface RegionalDestinationUpdateRequest {
  name?: string | null;
  summary?: string | null;
  description?: string | null;
  destinationTypeId?: number | null;
  latitude?: number | null;
  longitude?: number | null;
  boundaryNote?: string | null;
  boundary?: RegionalDestinationBoundaryDto | null;
  tagIds?: number[] | null;
  cardImageUrl?: string | null;
  coverImageUrl?: string | null;
  galleryImageUrls?: string[] | null;
  galleryImageFiles?: File[];
}

export interface RegionalDestinationTypeDto {
  id: number;
  name: string;
  description?: string | null;
}
