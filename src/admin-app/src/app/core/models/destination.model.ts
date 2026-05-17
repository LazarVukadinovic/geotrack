import { RegionalEntityImageDto, RegionalEntityImageInput } from './regional-image.model';

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
  images?: RegionalEntityImageDto[];
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
  images?: RegionalEntityImageInput[];
  tagIds: number[];
}

export interface RegionalDestinationUpdateRequest {
  name: string;
  summary?: string | null;
  description?: string | null;
  status?: 'Published' | 'Draft' | 'Archived';
  destinationTypeId?: number | null;
  latitude?: number | null;
  longitude?: number | null;
  boundaryNote?: string | null;
  boundary?: RegionalDestinationBoundaryDto | null;
  images?: RegionalEntityImageInput[];
  tagIds?: number[];
}
