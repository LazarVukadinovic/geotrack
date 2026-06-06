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
  sort?: DestinationSort;
  skip?: number;
  take?: number;
}

export interface RegionalDestinationTagDto {
  id: number;
  name: string;
  slug: string;
}

export interface RegionalDestinationCoordinateDto {
  latitude: number;
  longitude: number;
}

export interface RegionalDestinationBoundaryDto {
  shapeType: 'Polygon' | 'Rectangle' | 'Circle';
  coordinates: RegionalDestinationCoordinateDto[];
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
  tagIds?: number[] | null;
  cardImageUrl?: string | null;
  coverImageUrl?: string | null;
}
