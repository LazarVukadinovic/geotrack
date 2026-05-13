export type EventSort = 'newest' | 'oldest' | 'nameAsc' | 'nameDesc' | 'eventDateAsc' | 'eventDateDesc';
export type EventStatusFilter = 'all' | 'published' | 'draft' | 'archived';

export interface RegionalEventListItemDto {
  id: number;
  name: string;
  summary?: string | null;
  description?: string | null;
  eventType?: string | null;
  eventTypeId?: number | null;
  category?: string | null;
  status: string;
  regionId: number;
  regionDisplayName: string;
  destinationId?: number | null;
  destinationName?: string | null;
  locationId?: number | null;
  locationName?: string | null;
  placeId?: number | null;
  placeName?: string | null;
  address?: string | null;
  startsAt?: string | null;
  endsAt?: string | null;
  lastUpdatedAt?: string | null;
  createdAt?: string | null;
  ticketPrice?: string | null;
  duration?: string | null;
  hostedBy?: string | null;
  venueDescription?: string | null;
  arrivalTips?: string | null;
  accessibility?: string | null;
  imageUrl?: string | null;
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
