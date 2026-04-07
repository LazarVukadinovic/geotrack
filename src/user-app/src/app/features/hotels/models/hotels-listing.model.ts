export interface HotelListItem {
  id: string;
  title: string;
  location: string;
  image: string;
  rating: number;
  reviewCount?: string;
  distance: string;
  pricePerNight: number;
  description: string;
  tags: string[];
  variant: 'large' | 'compact';
  isSaved: boolean;
}

export interface HotelFilterOption {
  id: string;
  label: string;
}

export const HOTEL_FILTERS: HotelFilterOption[] = [
  { id: 'all', label: 'All' },
  { id: 'Luxury', label: 'Luxury' },
  { id: 'Budget', label: 'Budget' },
  { id: 'Family Friendly', label: 'Family' },
  { id: 'Spa', label: 'Spa' },
  { id: 'Pool', label: 'Pool' },
  { id: 'Breakfast', label: 'Breakfast' },
  { id: 'Sea View', label: 'Sea View' },
];
