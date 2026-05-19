import type { EntityImage } from '../../../core/models/entity-image.model';

export interface HotelTag {
  label: string;
}

export interface HotelReview {
  id: string;
  authorName: string;
  avatarUrl: string;
  rating: number;
  timeAgo: string;
  body: string;
  helpfulCount: number;
  reportedByCurrentUser: boolean;
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
  | 'ac';

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
  contactEmail?: string | null;
  phoneNumber?: string | null;
  bookingPartners: HotelBookingPartner[];
  amenities: HotelAmenity[];
  reviews: HotelReview[];
  similar: HotelSimilar[];
  isSaved: boolean;
}
