import { RegionalEntityImageDto } from './regional-image.model';

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
  cardImageUrl?: string | null;
  coverImageUrl?: string | null;
  galleryImageUrls?: string[];
  images?: RegionalEntityImageDto[];
  detailRoute?: string | null;
  editRoute?: string | null;
}

export interface RegionalPlaceDetailDto extends RegionalPlaceListItemDto {
  isOwnable: boolean;
  hasOwner?: boolean;
  hasLinkedContribution?: boolean;
  owners: RegionalPlaceOwnerDto[];
  canDelete?: boolean;
  latitude: number;
  longitude: number;
  openingHours?: string | null;
  contactEmail?: string | null;
  phoneNumber?: string | null;
  contactPhone?: string | null;
  website?: string | null;
  admissionPriceRange?: string | null;
  bookingExternalLink?: string | null;
  bestTimeToVisit?: string | null;
  visitorTips?: string | null;
  accessNotes?: string | null;
  accessibilityNotes?: string | null;
  operatingHoursEnabled: boolean;
  operatingHours: RegionalPlaceOperatingHourDto[];
  amenities?: RegionalPlaceAmenityDto[];
}


export interface RegionalPlaceOwnerDto {
  userId: number;
  username: string;
  email: string;
  displayName: string;
}

export interface RegionalPlaceAddOwnerRequest {
  emailOrUsername: string;
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
  tagTypeId?: number | null;
  tagTypeName?: string | null;
  tagTypeSlug?: string | null;
  tagTypeLabel?: string | null;
  tagTypeOrder?: number | null;
}

export interface RegionalPlaceAmenityDto {
  name: string;
}

export interface RegionalPlaceParentDestinationDto {
  id: number;
  name: string;
  displayName: string;
  status: string;
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

export interface RegionalStayTypeDto {
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
  stayTypeName?: string | null;
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
  phoneNumber?: string | null;
  contactPhone?: string | null;
  contactEmail?: string | null;
  admissionPriceRange?: string | null;
  bookingExternalLink?: string | null;
  amenities?: string[];
  operatingHoursEnabled: boolean;
  operatingHours: RegionalPlaceOperatingHourDto[];
  tagIds: number[];
  cardImageFile?: File | null;
  coverImageFile?: File | null;
  galleryImageFiles?: File[];
}

export interface RegionalPlaceUpdateRequest {
  locationId?: number | null;
  placeTypeId?: number | null;
  stayTypeId?: number | null;
  stayTypeName?: string | null;
  name?: string | null;
  subtitle?: string | null;
  description?: string | null;
  address?: string | null;
  openingHours?: string | null;
  contactEmail?: string | null;
  phoneNumber?: string | null;
  contactPhone?: string | null;
  website?: string | null;
  admissionPriceRange?: string | null;
  bookingExternalLink?: string | null;
  amenities?: string[] | null;
  bestTimeToVisit?: string | null;
  visitorTips?: string | null;
  accessNotes?: string | null;
  accessibilityNotes?: string | null;
  operatingHoursEnabled?: boolean | null;
  operatingHours?: RegionalPlaceOperatingHourDto[] | null;
  latitude?: number | null;
  longitude?: number | null;
  tagIds?: number[] | null;
  cardImageUrl?: string | null;
  coverImageUrl?: string | null;
  galleryImageUrls?: string[] | null;
  galleryImageFiles?: File[];
}
