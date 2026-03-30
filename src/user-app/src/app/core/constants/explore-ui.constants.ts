import type { ExploreCategoryFilter } from '../models/explore-item.model';

export interface CategoryFilterOption {
  id: ExploreCategoryFilter;
  label: string;
}

export const EXPLORE_CATEGORY_FILTERS: CategoryFilterOption[] = [
  { id: 'all', label: 'All' },
  { id: 'attraction', label: 'Attractions' },
  { id: 'restaurant', label: 'Food' },
  { id: 'hotel', label: 'Hotels' },
  { id: 'activity', label: 'Activities' },
];
