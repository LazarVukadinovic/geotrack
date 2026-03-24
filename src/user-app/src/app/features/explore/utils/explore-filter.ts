import type { ExploreCategoryFilter, ExploreItem } from '../../../core/models/explore-item.model';

export function itemMatchesExploreFilters(
  item: ExploreItem,
  query: string,
  category: ExploreCategoryFilter,
): boolean {
  if (category !== 'all' && item.category !== category) {
    return false;
  }
  const q = query.trim().toLowerCase();
  if (!q) {
    return true;
  }
  const haystack = [item.title, item.subtitle, item.description, item.location].join(' ').toLowerCase();
  return haystack.includes(q);
}
