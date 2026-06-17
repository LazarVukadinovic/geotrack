import type { EntityImage } from './entity-image.model';

/** Distinguishes content types for routing purposes. */
export type PlaceContentType = 'place' | 'hotel' | 'destination';

export type PlaceCategory =
  | 'beach'
  | 'restaurant'
  | 'bar'
  | 'attraction'
  | 'landmark'
  | 'activity'
  | 'cafe'
  | 'nature'
  | 'museum';

export interface PlaceTag {
  label: string;
}

export interface OpeningHoursDay {
  day: string;
  hours: string;
}

export interface PlaceReview {
  id: string;
  authorUserId: number;
  authorName: string;
  avatarUrl: string;
  rating: number;
  timeAgo: string;
  body: string;
  helpfulCount: number;
  reportedByCurrentUser: boolean;
  isOwnReview: boolean;
}

export interface PlaceRelatedEvent {
  id: string;
  title: string;
  subtitle?: string | null;
  image?: string | null;
}

export interface PlaceSimilar {
  id: string;
  title: string;
  image: string;
  rating?: number;
  distance: string;
  destinationId?: string | null;
  destinationName?: string | null;
  isSaved: boolean;
}

export interface PlaceAmenity {
  id: number;
  label: string;
  icon?: string | null;
}

/** Full place detail page data model. Aligns with future API payload. */
export interface PlaceDetail {
  id: string;
  title: string;
  location: string;
  locationId?: string | null;
  locationName?: string | null;
  latitude?: number | null;
  longitude?: number | null;
  heroImage: string;
  heroImagePosition: string;
  heroImageSize: string;
  images: EntityImage[];
  tags: PlaceTag[];
  rating?: number;
  reviewCount?: string;
  distance: string;
  openStatus: string;
  openingHours?: string | null;
  openingHoursSchedule?: OpeningHoursDay[] | null;
  website?: string | null;
  about: string;
  reviews: PlaceReview[];
  relatedEvents: PlaceRelatedEvent[];
  similar: PlaceSimilar[];
  similarInDestination: PlaceSimilar[];
  similarInTerritory: PlaceSimilar[];
  isSaved: boolean;
  isOwnable: boolean;
  isOwnedByCurrentUser: boolean;
  hasPendingOwnershipClaim: boolean;
  ownerContributionId?: number | null;
  contentType: PlaceContentType;
  category: PlaceCategory;
  contactEmail?: string | null;
  phoneNumber?: string | null;
  contactPhone?: string | null;
  admissionPriceRange?: string | null;
  bookingExternalLink?: string | null;
  amenities: PlaceAmenity[];
  bestTimeToVisit?: string | null;
  visitorTips?: string | null;
  accessNotes?: string | null;
  accessibilityNotes?: string | null;
}