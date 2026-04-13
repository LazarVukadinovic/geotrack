export interface NearbyStatus {
  label: string;
  value: string;
}

export interface ExplorePromoCard {
  title: string;
  message: string;
  ctaLabel: string;
}

export interface ExploreEvent {
  id: string;
  title: string;
  subtitle: string;
  dateLabel: string;
  image: string;
}

export interface ExploreNearbyItem {
  id: string;
  title: string;
  categoryLabel: string;
  image: string;
  rating: number;
  reviewCount: string;
  distance: string;
  isSaved: boolean;
}

export interface ExploreStay {
  id: string;
  title: string;
  image: string;
  rating: number;
  reviewCount?: string;
  distance: string;
  pricePerNight: number;
  isSaved: boolean;
}

export interface ExploreAttraction {
  id: string;
  title: string;
  type: string;
  image: string;
}

export interface ExploreLandmarkCategory {
  id: string;
  title: string;
  image: string;
  categoryKey: string;
}

export interface ExploreCuratedItem {
  id: string;
  title: string;
  subtitle: string;
  categoryLabel: string;
  image: string;
  isSaved: boolean;
}
