export type DestinationSort = 'newest' | 'oldest' | 'nameAsc' | 'nameDesc';
export type DestinationStatusFilter = 'all' | 'published' | 'draft' | 'archived';

export interface RegionalDestinationListItemDto {
  id: number;
  name: string;
  summary?: string | null;
  description?: string | null;
  status: string;
  regionId: number;
  regionDisplayName: string;
  destinationTypeId: number;
  destinationTypeName?: string | null;
  lastUpdatedAt?: string | null;
  createdAt?: string | null;
  imageUrl?: string | null;
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
