export type RequestSource = 'Contribution' | 'ReviewReport' | (string & {});
export type RequestEntityType =
  | 'Destination'
  | 'Location'
  | 'Place'
  | 'Event'
  | 'Review'
  | (string & {});
export type RequestStatus = 'Pending' | 'Approved' | 'Rejected' | (string & {});
export type RequestQueueTypeFilter =
  | 'all'
  | 'destination'
  | 'location'
  | 'place'
  | 'event'
  | 'review';
export type RequestQueueStatusFilter = 'pending' | 'approved' | 'rejected' | 'all';
export type RequestQueueSort = 'newest' | 'oldest' | 'status' | 'type';

export interface RegionalRequestQueueItem {
  requestKey: string;
  requestId: number;
  displayRequestId: string;
  source: RequestSource;
  title: string;
  entityType: RequestEntityType;
  contributionType?: string | null;
  regionId: number;
  regionDisplayName: string;
  submittedByUserId: number;
  submittedByDisplayName: string;
  submittedByInitials?: string | null;
  submittedAt: string;
  status: RequestStatus;
  thumbnailImageUrl?: string | null;
  previewText?: string | null;
  detailRoute?: string | null;
}

export type ReviewRequest = RegionalRequestQueueItem;

export interface RegionalRequestQueueCountsByType {
  destinations: number;
  locations: number;
  places: number;
  events: number;
  reviews: number;
}

export interface RegionalRequestQueueResponse {
  items: RegionalRequestQueueItem[];
  totalCount: number;
  pendingCount: number;
  approvedCount: number;
  rejectedCount: number;
  countsByType: RegionalRequestQueueCountsByType;
  skip: number;
  take: number;
}

export interface BookingPartner {
  id: string;
  url: string;
}

export interface PlaceContributionImage {
  id: number;
  url: string;
  altText?: string | null;
  caption?: string | null;
  isMain: boolean;
  sortOrder: number;
  imagePosition: string;
  imageSize: string;
}

export interface ReviewRequestDetail extends ReviewRequest {
  description?: string | null;
  address?: string | null;
  locationName?: string | null;
  destinationName?: string | null;
  city?: string | null;
  country?: string | null;
  latitude?: number | null;
  longitude?: number | null;
  contactEmail?: string | null;
  phoneNumber?: string | null;
  website?: string | null;
  placeType?: string | null;
  stayType?: string | null;
  targetEntityId?: number | null;
  targetEntityName?: string | null;
  tags?: string[];
  amenities?: string[];
  stayAmenities?: string[];
  bookingPartners?: BookingPartner[];
  images?: PlaceContributionImage[];
  reviewComment?: string | null;
  reviewRating?: number | null;
  reviewCreatedAt?: string | null;
  reviewAuthorDisplayName?: string | null;
  reportReason?: string | null;
  reportNote?: string | null;
  decision?: string | null;
  decisionNote?: string | null;
}

export interface RegionalRequestActionResult {
  requestKey: string;
  success: boolean;
  status?: RequestStatus | null;
  message?: string | null;
  item?: RegionalRequestQueueItem | null;
}

export interface RegionalRequestBulkActionResponse {
  results: RegionalRequestActionResult[];
  succeededCount: number;
  failedCount: number;
}
