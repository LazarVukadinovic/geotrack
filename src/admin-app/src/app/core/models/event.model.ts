import { RegionalEntityImageDto, RegionalEntityImageInput } from './regional-image.model';

export type EventSort = 'newest' | 'oldest' | 'nameAsc' | 'nameDesc' | 'eventDateAsc' | 'eventDateDesc';
export type EventStatusFilter = 'all' | 'published' | 'draft' | 'archived';

export interface RegionalEventListItemDto {
  id: number;
  name: string;
  summary?: string | null;
  description?: string | null;
  internalReferenceId?: string | null;
  eventType?: string | null;
  eventTypeId?: number | null;
  category?: string | null;
  status: string;
  parentEntityType?: string | null;
  regionId: number;
  regionDisplayName: string;
  destinationId: number;
  destinationName: string;
  locationId?: number | null;
  locationName?: string | null;
  placeId?: number | null;
  placeName?: string | null;
  address?: string | null;
  startsAt?: string | null;
  endsAt?: string | null;
  isAllDay?: boolean;
  isRecurring?: boolean;
  lastUpdatedAt?: string | null;
  createdAt?: string | null;
  createdByUserId?: number | null;
  publishedAt?: string | null;
  venueName?: string | null;
  ticketPrice?: string | null;
  isFree?: boolean;
  externalTicketLink?: string | null;
  duration?: string | null;
  hostedBy?: string | null;
  contactEmail?: string | null;
  attendanceLimit?: number | null;
  ageRestriction?: string | null;
  venueDescription?: string | null;
  arrivalTips?: string | null;
  accessibility?: string | null;
  tags?: RegionalEventTagDto[];
  imageUrl?: string | null;
  images?: RegionalEntityImageDto[];
  detailRoute?: string | null;
  editRoute?: string | null;
  latitude?: number | null;
  longitude?: number | null;
}

export interface RegionalEventDetailDto extends RegionalEventListItemDto {}

export interface RegionalEventListResponse {
  items: RegionalEventListItemDto[];
  totalCount: number;
  skip: number;
  take: number;
}

export interface RegionalEventListParams {
  regionId: number;
  search?: string | null;
  status?: EventStatusFilter;
  eventType?: string | null;
  sort?: EventSort;
  skip?: number;
  take?: number;
}

export interface RegionalEventCreateRequest {
  regionId: number;
  parentEntityType: 'Destination' | 'Location' | 'Place';
  destinationId?: number | null;
  locationId?: number | null;
  placeId?: number | null;
  eventTypeId: number;
  name: string;
  summary: string;
  description?: string | null;
  status?: 'Published' | 'Draft';
  venueName?: string | null;
  isAllDay: boolean;
  startsAt: string;
  endsAt: string;
  isRecurring: boolean;
  isFree: boolean;
  ticketPrice?: string | null;
  externalTicketLink?: string | null;
  organizerName?: string | null;
  contactEmail?: string | null;
  attendanceLimit?: number | null;
  ageRestriction?: string | null;
  tagIds: number[];
  images?: RegionalEntityImageInput[];
}

export interface RegionalEventUpdateRequest {
  regionId?: number;
  parentEntityType: 'Destination' | 'Location' | 'Place';
  destinationId?: number | null;
  locationId?: number | null;
  placeId?: number | null;
  eventTypeId: number;
  name: string;
  summary: string;
  description?: string | null;
  status?: 'Published' | 'Draft' | 'Archived';
  venueName?: string | null;
  isAllDay: boolean;
  startsAt: string;
  endsAt: string;
  isRecurring: boolean;
  isFree: boolean;
  ticketPrice?: string | null;
  externalTicketLink?: string | null;
  organizerName?: string | null;
  contactEmail?: string | null;
  attendanceLimit?: number | null;
  ageRestriction?: string | null;
  tagIds?: number[];
  images?: RegionalEntityImageInput[];
}

export interface RegionalEventTagDto {
  id: number;
  name: string;
  slug: string;
}

export interface RegionalEventTypeDto {
  id: number;
  name: string;
  description?: string | null;
}

export interface RegionalEventParentDestinationDto {
  id: number;
  name: string;
  displayName: string;
  status: string;
  latitude?: number | null;
  longitude?: number | null;
}

export interface RegionalEventParentLocationDto {
  id: number;
  name: string;
  displayName: string;
  destinationId: number;
  destinationName: string;
  status: string;
  latitude?: number | null;
  longitude?: number | null;
}

export interface RegionalEventParentPlaceDto {
  id: number;
  name: string;
  displayName: string;
  locationId: number;
  locationName: string;
  destinationId: number;
  destinationName: string;
  address?: string | null;
  status: string;
  latitude?: number | null;
  longitude?: number | null;
}
