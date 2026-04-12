export interface FoodMusicItem {
  id: string;
  title: string;
  subtitle: string;
  description: string;
  location: string;
  image: string;
  rating?: number;
  reviewCount?: string;
  openingHours?: string;
  tags: string[];
  variant: 'large' | 'compact';
  isSaved: boolean;
}

export interface FoodMusicFilterOption {
  id: string;
  label: string;
}

export const FOOD_MUSIC_FILTERS: FoodMusicFilterOption[] = [
  { id: 'all', label: 'All' },
  { id: 'Restaurants', label: 'Restaurants' },
  { id: 'Bars', label: 'Bars' },
  { id: 'Live Music', label: 'Live Music' },
  { id: 'Fine Dining', label: 'Fine Dining' },
  { id: 'Nightlife', label: 'Nightlife' },
  { id: 'Beach Bars', label: 'Beach Bars' },
  { id: 'Cafe', label: 'Cafe' },
];
