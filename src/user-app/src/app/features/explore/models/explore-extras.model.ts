export interface NearbyStatus {
  label: string;
  value: string;
}

export interface ExplorePromoCard {
  title: string;
  message: string;
  ctaLabel: string;
}

export type ExploreSectionLayout = 'horizontal-cards' | 'grid' | 'compact-list' | string;
export type ExploreSectionCardType = 'place-card' | 'stay-card' | 'event-card' | string;
export type ExploreEntityType = 'place' | 'stay' | 'event' | 'destination' | 'location' | string;

export interface ExploreSectionTag {
  tagId: string;
  name: string;
  slug: string;
}

export interface ExploreItemStatusBadge {
  label: string;
  tone: 'success' | 'warning' | 'muted' | 'neutral' | string;
}

export interface ExploreSectionItem {
  id: string;
  entityType: ExploreEntityType;
  detailRoute: string;

  title: string;
  subtitle?: string;
  description?: string;
  categoryLabel?: string;
  image: string;

  rating?: number;
  reviewCount?: string;
  distance?: string;
  dateLabel?: string;
  openingHours?: string;
  statusBadge?: ExploreItemStatusBadge;
  pricePerNight?: number;

  isSaved: boolean;
  tags: ExploreSectionTag[];
}

export interface ExploreSection {
  tagTypeId?: number;
  key: string;
  title: string;
  subtitle?: string;
  layout: ExploreSectionLayout;
  cardType: ExploreSectionCardType;
  moreRoute?: string;
  priority: number;
  items: ExploreSectionItem[];
}

// Compatibility types kept for pages/services that still import them.
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
  rating?: number;
  reviewCount?: string;
  distance: string;
  isSaved: boolean;
}

export interface ExploreStay {
  id: string;
  title: string;
  image: string;
  rating?: number;
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

