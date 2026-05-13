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
  regionId: number;
  regionDisplayName: string;
  destinationId?: number | null;
  destinationName?: string | null;
  lastUpdatedAt?: string | null;
  createdAt?: string | null;
  imageUrl?: string | null;
  detailRoute?: string | null;
  editRoute?: string | null;
}

export interface RegionalLocationDetailDto extends RegionalLocationListItemDto {
  latitude: number;
  longitude: number;
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
