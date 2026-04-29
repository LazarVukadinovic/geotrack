export interface RecommendationsDestination {
  id: number;
  name: string;
  latitude: number;
  longitude: number;
}

export interface RecommendationFilter {
  tagId: number;
  label: string;
  count: number;
}

export interface RecommendationItem {
  id: string;
  title: string;
  reason: string;
  description: string;
  location: string;
  image: string;
  rating?: number;
  reviewCount?: string;
  distance?: string;
  openingHours?: string;
  categoryLabel: string;
  matchedTagId?: number;
  isSaved: boolean;
}

export interface RecommendationsApiItem {
  id: number;
  title: string;
  reason?: string;
  description?: string;
  location?: string;
  image?: string;
  rating?: number;
  reviewCount?: string;
  distance?: string;
  openingHours?: string;
  categoryLabel?: string;
  matchedTagId?: number;
  isSaved: boolean;
}

export interface RecommendationsApiResponse {
  selectedDestination?: RecommendationsDestination;
  filters: RecommendationFilter[];
  items: RecommendationsApiItem[];
  hasSignals: boolean;
  emptyReason?: string;
  skip: number;
  take: number;
  nextSkip: number;
  hasMore: boolean;
}
