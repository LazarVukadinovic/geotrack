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

export interface PlaceReview {
  id: string;
  authorName: string;
  avatarUrl: string;
  rating: number;
  timeAgo: string;
  body: string;
  helpfulCount: number;
}

export interface PlaceSimilar {
  id: string;
  title: string;
  image: string;
  rating: number;
  distance: string;
  isSaved: boolean;
}

/** Full place detail page data model. Aligns with future API payload. */
export interface PlaceDetail {
  id: string;
  title: string;
  location: string;
  heroImage: string;
  tags: PlaceTag[];
  rating: number;
  reviewCount: string;
  distance: string;
  openStatus: string;
  about: string;
  reviews: PlaceReview[];
  similar: PlaceSimilar[];
  isSaved: boolean;
  contentType: PlaceContentType;
  category: PlaceCategory;
}
