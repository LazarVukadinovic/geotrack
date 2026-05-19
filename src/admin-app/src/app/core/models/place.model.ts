export type PlaceSort = 'newest' | 'oldest' | 'nameAsc' | 'nameDesc';
export type PlaceStatusFilter = 'all' | 'published' | 'draft' | 'archived';

export interface RegionalPlaceListItemDto {
  id: number;
  name: string;
  summary?: string | null;
  internalReferenceId?: string | null;
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
  createdByUserId?: number | null;
  tags?: RegionalPlaceTagDto[];
  lastUpdatedAt?: string | null;
  createdAt?: string | null;
  publishedAt?: string | null;
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
  website?: string | null;
  admissionPriceRange?: string | null;
  bookingExternalLink?: string | null;
  bestTimeToVisit?: string | null;
  visitorTips?: string | null;
  accessNotes?: string | null;
  accessibilityNotes?: string | null;
  operatingHoursEnabled: boolean;
  operatingHours: RegionalPlaceOperatingHourDto[];
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

export interface RegionalPlaceTagDto {
  id: number;
  name: string;
  slug: string;
}

export interface RegionalPlaceParentLocationDto {
  id: number;
  name: string;
  displayName: string;
  destinationId: number;
  destinationName: string;
  status: string;
}

export interface RegionalPlaceTypeDto {
  id: number;
  name: string;
  description?: string | null;
}

export interface RegionalPlaceOperatingHourDto {
  dayOfWeek: string;
  isOpen: boolean;
  openTime?: string | null;
  closeTime?: string | null;
}

export interface RegionalPlaceCreateRequest {
  regionId: number;
  locationId: number;
  placeTypeId: number;
  stayTypeId?: number | null;
  name: string;
  summary: string;
  description?: string | null;
  status: 'Published' | 'Draft';
  latitude: number;
  longitude: number;
  streetAddress?: string | null;
  bestTimeToVisit?: string | null;
  visitorTips?: string | null;
  accessNotes?: string | null;
  accessibilityNotes?: string | null;
  website?: string | null;
  contactPhone?: string | null;
  admissionPriceRange?: string | null;
  bookingExternalLink?: string | null;
  operatingHoursEnabled: boolean;
  operatingHours: RegionalPlaceOperatingHourDto[];
  tagIds: number[];
  cardImageFile?: File | null;
  coverImageFile?: File | null;
  galleryImageFiles?: File[];
}
