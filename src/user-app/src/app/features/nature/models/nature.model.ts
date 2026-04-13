export interface NatureItem {
  id: string;
  title: string;
  subtitle: string;
  description: string;
  location: string;
  image: string;
  rating?: number;
  reviewCount?: string;
  distance?: string;
  tags: string[];
  variant: 'large' | 'compact';
  isSaved: boolean;
}

export interface NatureFilterOption {
  id: string;
  label: string;
}

export const NATURE_FILTERS: NatureFilterOption[] = [
  { id: 'all', label: 'All' },
  { id: 'Islands', label: 'Islands' },
  { id: 'Parks', label: 'Parks' },
  { id: 'Beaches', label: 'Beaches' },
  { id: 'Mountains', label: 'Mountains' },
  { id: 'Nature Reserves', label: 'Reserves' },
  { id: 'Hiking', label: 'Hiking' },
  { id: 'Scenic Views', label: 'Scenic Views' },
];
