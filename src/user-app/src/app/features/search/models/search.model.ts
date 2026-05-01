export type SearchDomain = 'destinations' | 'places' | 'stays' | 'events' | 'activities';

export type SearchEntityType = 'destination' | 'place' | 'stay' | 'event' | 'activity';

export interface SearchFilterOption {
  id: string;
  label: string;
  domain?: SearchDomain | null;
  count?: number | null;
}

export interface SearchFilterGroup {
  key: string;
  label: string;
  options: SearchFilterOption[];
}

export interface SearchIntentDestination {
  id: number;
  name: string;
  latitude: number;
  longitude: number;
}

export interface SearchIntent {
  destination?: SearchIntentDestination | null;
  activities: string[];
  contentTypes: string[];
  usesCurrentLocation: boolean;
}

export interface SearchResultItem {
  id: number;
  entityType: SearchEntityType;
  title: string;
  subtitle?: string | null;
  description?: string | null;
  image?: string | null;
  route?: string | null;
  destinationId?: number | null;
  destinationName?: string | null;
  latitude?: number | null;
  longitude?: number | null;
  categoryLabel?: string | null;
  tags: string[];
  rating?: number | null;
  reviewCount?: string | null;
  distance?: string | null;
  canOpenInExplore: boolean;
  score: number;
  matchedTerms: string[];
}

export interface SearchResultGroup {
  key: SearchDomain;
  label: string;
  items: SearchResultItem[];
}

export interface GlobalSearchResponse {
  query: string;
  language: string;
  intent: SearchIntent;
  filterGroups: SearchFilterGroup[];
  topResult?: SearchResultItem | null;
  groups: SearchResultGroup[];
  totalCount: number;
}

export interface SearchMetadata {
  language: string;
  filterGroups: SearchFilterGroup[];
}

export const SEARCH_DOMAINS: { id: SearchDomain; label: string }[] = [
  { id: 'destinations', label: 'Destinations' },
  { id: 'places', label: 'Places' },
  { id: 'stays', label: 'Stays' },
  { id: 'events', label: 'Events' },
  { id: 'activities', label: 'Activities' },
];
