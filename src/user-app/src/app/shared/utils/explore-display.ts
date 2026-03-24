import type { ExploreCategory } from '../../core/models/explore-item.model';

export function categoryBadgeLabel(category: ExploreCategory): string {
  const labels: Record<ExploreCategory, string> = {
    attraction: 'Attraction',
    restaurant: 'Restaurant',
    hotel: 'Hotel',
    activity: 'Activity',
  };
  return labels[category];
}
