export type PlaceSort = 'newest' | 'oldest' | 'nameAsc' | 'nameDesc';
export type PlaceStatusFilter = 'all' | 'published' | 'draft' | 'archived';

export interface RegionalPlaceListItemDto {
  id: number;
  name: string;
  summary?: string | null;
  description?: string | null;
  subtitle?: string | null;
  address?: string | null;
  placeType?: string | null;
  placeTypeId?: number | null;
  stayType?: string | null;
  stayTypeId?: number | null;
  status: string;
  regionId: number;
  regionDisplayName: string;
  destinationId?: number | null;
  destinationName?: string | null;
  locationId?: number | null;
  locationName?: string | null;
  lastUpdatedAt?: string | null;
  createdAt?: string | null;
  imageUrl?: string | null;
  detailRoute?: string | null;
  editRoute?: string | null;
}

export interface RegionalPlaceDetailDto extends RegionalPlaceListItemDto {
  latitude: number;
  longitude: number;
  openingHours?: string | null;
  contactEmail?: string | null;
  phoneNumber?: string | null;
}

export interface RegionalPlaceListResponse {
  items: RegionalPlaceListItemDto[];
  totalCount: number;
  skip: number;
  take: number;
}

export interface RegionalPlaceListParams {
  regionId: number;
  search?: string | null;
  status?: PlaceStatusFilter;
  placeType?: string | null;
  sort?: PlaceSort;
  skip?: number;
  take?: number;
}
