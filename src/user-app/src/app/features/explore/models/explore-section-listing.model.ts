import type { ExploreSectionItem } from './explore-extras.model';

export interface ExploreSectionListingResponse {
  isKnownSection: boolean;
  selectedDestination?: {
    id: number;
    name: string;
    description?: string;
    latitude: number;
    longitude: number;
  };
  key: string;
  title: string;
  subtitle?: string;
  cardType: string;
  items: ExploreSectionItem[];
  skip: number;
  take: number;
  nextSkip: number;
  hasMore: boolean;
  totalCount: number;
  emptyReason?: string;
}
