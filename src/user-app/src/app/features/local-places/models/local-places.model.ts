export interface LocalPlacesCategory {
  id: string;
  title: string;
  image: string;
  categoryKey: string;
  subtitle: string;
}

export interface PlaceCategoryItem {
  id: string;
  title: string;
  subtitle: string;
  description: string;
  location: string;
  image: string;
  rating?: number;
  reviewCount?: string;
  openingHours?: string;
  variant: 'large' | 'compact';
  isSaved: boolean;
}

export interface CategoryMeta {
  title: string;
  subtitle: string;
}
