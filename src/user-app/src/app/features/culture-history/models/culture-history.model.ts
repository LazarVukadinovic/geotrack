export interface CultureHistoryItem {
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

export interface CultureHistoryFilterOption {
  id: string;
  label: string;
}

export const CULTURE_HISTORY_FILTERS: CultureHistoryFilterOption[] = [
  { id: 'all', label: 'All' },
  { id: 'Museums', label: 'Museums' },
  { id: 'Landmarks', label: 'Landmarks' },
  { id: 'Districts', label: 'Districts' },
  { id: 'Galleries', label: 'Galleries' },
  { id: 'Exhibitions', label: 'Exhibitions' },
  { id: 'Historic Sites', label: 'Historic Sites' },
];
