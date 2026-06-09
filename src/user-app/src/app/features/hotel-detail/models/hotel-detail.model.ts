import type { EntityImage } from '../../../core/models/entity-image.model';

export interface HotelTag {
  label: string;
}

export interface HotelReview {
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

export interface HotelSimilar {
  id: string;
  title: string;
  image: string;
  rating?: number;
  distance: string;
  isSaved: boolean;
}

export type HotelAmenityIcon =
  | 'pool'
  | 'wifi'
  | 'breakfast'
  | 'parking'
  | 'spa'
  | 'gym'
  | 'restaurant'
  | 'ac'
  | 'outdoor'
  | 'sea'
  | 'family';

export interface HotelAmenity {
  id: string;
  label: string;
  icon: HotelAmenityIcon;
}

export type BookingPartnerVariant = 'dark' | 'brand' | 'muted';

export interface HotelBookingPartner {
  id: string;
  name: string;
  variant: BookingPartnerVariant;
  url?: string | null;
}

export interface HotelDetail {
  id: string;
  title: string;
  location: string;
  locationId?: string | null;
  locationName?: string | null;
  latitude?: number | null;
  longitude?: number | null;
  heroImage: string;
  images: EntityImage[];
  tags: HotelTag[];
  rating?: number;
  reviewCount?: string;
  distance: string;
  about: string;
  website?: string | null;
  contactEmail?: string | null;
  phoneNumber?: string | null;
  bookingPartners: HotelBookingPartner[];
  amenities: HotelAmenity[];
  reviews: HotelReview[];
  similar: HotelSimilar[];
  isSaved: boolean;
  isOwnable: boolean;
  isOwnedByCurrentUser: boolean;
  hasPendingOwnershipClaim: boolean;
  ownerContributionId?: number | null;
}
