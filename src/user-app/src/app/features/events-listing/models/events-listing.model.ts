export interface EventListingItem {
  id: string;
  title: string;
  subtitle: string;
  description: string;
  location: string;
  image: string;
  dateLabel: string;
  tags: string[];
  variant: 'large' | 'compact';
  isSaved: boolean;
}

export interface EventsFeatured {
  title: string;
  description: string;
  dateRange: string;
  locationLine: string;
}

export interface EventsListingFilterOption {
  id: string;
  label: string;
}

export const EVENTS_LISTING_FILTERS: EventsListingFilterOption[] = [
  { id: 'all', label: 'All' },
  { id: 'Festivals', label: 'Festivals' },
  { id: 'Live Music', label: 'Live Music' },
  { id: 'Cultural', label: 'Cultural' },
  { id: 'Outdoor', label: 'Outdoor' },
  { id: 'Family', label: 'Family' },
  { id: 'Food & Market', label: 'Food & Market' },
  { id: 'Nightlife', label: 'Nightlife' },
];
