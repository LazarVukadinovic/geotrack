export type ExploreCategory = 'attraction' | 'restaurant' | 'hotel' | 'activity';

/** Domain model for explore / discover content; aligns with future API payload. */
export interface ExploreItem {
  id: string;
  title: string;
  subtitle: string;
  description: string;
  category: ExploreCategory;
  type: string;
  location: string;
  image: string;
  rating?: number;
  reviewCount?: string;
  isSaved: boolean;
  openingHours?: string;
  isRecommended?: boolean;
}
