import { RegionalEntityImageDto } from './regional-image.model';

export type LocationSort = 'newest' | 'oldest' | 'nameAsc' | 'nameDesc';
export type LocationStatusFilter = 'all' | 'published' | 'draft' | 'archived';

export interface RegionalLocationListItemDto {
  id: number;
  name: string;
  summary?: string | null;
  description?: string | null;
  locationType?: string | null;
  locationTypeId?: number | null;
  status: string;
  internalReferenceId?: string | null;
  regionId: number;
  regionDisplayName: string;
  destinationId?: number | null;
  destinationName?: string | null;
  createdByUserId?: number | null;
  boundaryNote?: string | null;
  boundaryShapeType?: string | null;
  tags?: RegionalLocationTagDto[];
  lastUpdatedAt?: string | null;
  createdAt?: string | null;
  publishedAt?: string | null;
  imageUrl?: string | null;
  cardImageUrl?: string | null;
  coverImageUrl?: string | null;
  galleryImageUrls?: string[];
  images?: RegionalEntityImageDto[];
  detailRoute?: string | null;
  editRoute?: string | null;
}

export interface RegionalLocationDetailDto extends RegionalLocationListItemDto {
  latitude: number;
  longitude: number;
  canDelete?: boolean;
}

export interface RegionalLocationListResponse {
  items: RegionalLocationListItemDto[];
  totalCount: number;
  skip: number;
  take: number;
}

export interface RegionalLocationListParams {
  regionId: number;
  search?: string | null;
  status?: LocationStatusFilter;
  sort?: LocationSort;
  skip?: number;
  take?: number;
}

export interface RegionalLocationTagDto {
  id: number;
  name: string;
  slug: string;
}

export interface RegionalLocationParentDestinationDto {
  id: number;
  name: string;
  displayName: string;
  status: string;
}

export interface RegionalLocationTypeDto {
  id: number;
  name: string;
  description?: string | null;
}

export interface RegionalLocationCoordinateDto {
  latitude: number;
  longitude: number;
}

export interface RegionalLocationBoundaryDto {
  shapeType: 'Polygon' | 'Rectangle' | 'Circle';
  coordinates: RegionalLocationCoordinateDto[];
}

export interface RegionalLocationCreateRequest {
  regionId: number;
  destinationId: number;
  name: string;
  summary?: string | null;
  description?: string | null;
  status: 'Published' | 'Draft';
  locationTypeId?: number | null;
  latitude?: number | null;
  longitude?: number | null;
  boundaryNote?: string | null;
  boundary?: RegionalLocationBoundaryDto | null;
  tagIds: number[];
  cardImageFile?: File | null;
  coverImageFile?: File | null;
  galleryImageFiles?: File[];
}

export interface RegionalLocationUpdateRequest {
  destinationId?: number | null;
  name?: string | null;
  summary?: string | null;
  description?: string | null;
  status?: 'Published' | 'Draft' | 'Archived';
  locationTypeId?: number | null;
  latitude?: number | null;
  longitude?: number | null;
  tagIds?: number[] | null;
  cardImageUrl?: string | null;
  coverImageUrl?: string | null;
}
