export type ContentModeratorEntityType = 'destination' | 'location' | 'place' | 'event';
export type ContentModeratorEntityStatusFilter = 'all' | 'active' | 'suspended' | 'deleted';
export type ContentModeratorEntitySort = 'newest' | 'oldest' | 'nameAsc' | 'nameDesc' | 'status';

export interface ContentModeratorEntityReviewCounts {
  allCount: number;
  activeCount: number;
  suspendedCount: number;
  deletedCount: number;
}

export interface ContentModeratorEntityReviewPermissions {
  canView: boolean;
  canSuspend: boolean;
  canUnsuspend: boolean;
  canDelete: boolean;
  canRestore: boolean;
}

export interface ContentModeratorEntityReviewListItem {
  entityId: number;
  entityType: ContentModeratorEntityType;
  name: string;
  summary: string | null;
  descriptionPreview: string | null;
  thumbnailUrl: string | null;
  status: string;
  rawStatus: string;
  isPublished: boolean;
  isSuspended: boolean;
  isDeleted: boolean;
  createdAt: string | null;
  updatedAt: string | null;
  publishedAt: string | null;
  regionId: number | null;
  regionName: string | null;
  territoryName: string | null;
  destinationId: number | null;
  destinationName: string | null;
  locationId: number | null;
  locationName: string | null;
  placeId: number | null;
  placeName: string | null;
  typeName: string | null;
  createdByUserId: number | null;
  startsAt: string | null;
  endsAt: string | null;
  publicDetailRoute: string | null;
  canOpenInUserApp: boolean;
  permissions: ContentModeratorEntityReviewPermissions;
}

export interface ContentModeratorEntityReviewListResponse {
  items: ContentModeratorEntityReviewListItem[];
  totalCount: number;
  skip: number;
  take: number;
  nextSkip: number | null;
  hasMore: boolean;
  counts: ContentModeratorEntityReviewCounts;
}

export interface ContentModeratorEntityImage {
  id: number;
  url: string;
  altText: string | null;
  caption: string | null;
  imageRole: string;
  isMain: boolean;
  sortOrder: number;
}

export interface ContentModeratorEntityTag {
  id: number;
  name: string;
  slug: string;
}

export interface ContentModeratorEntityMetadata {
  latitude: number | null;
  longitude: number | null;
  address: string | null;
  boundaryNote: string | null;
  boundaryShapeType: string | null;
  parentEntityType: string | null;
  venueName: string | null;
  ticketPrice: string | null;
  isFree: boolean | null;
  isAllDay: boolean | null;
  isRecurring: boolean | null;
  contactEmail: string | null;
  phoneNumber: string | null;
  website: string | null;
  contentCounts: Record<string, number>;
}

export interface ContentModeratorEntityReviewDetail extends ContentModeratorEntityReviewListItem {
  description: string | null;
  images: ContentModeratorEntityImage[];
  tags: ContentModeratorEntityTag[];
  metadata: ContentModeratorEntityMetadata;
}

export interface ContentModeratorModerationActionRequest {
  reason?: string;
  internalNote?: string;
}

export interface ContentModeratorEntityReviewPageQuery {
  type: ContentModeratorEntityType;
  search?: string;
  status?: ContentModeratorEntityStatusFilter;
  sort?: ContentModeratorEntitySort;
  skip?: number;
  take?: number;
}
