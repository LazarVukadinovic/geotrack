export interface MetricCard {
  label: string;
  value: string;
  delta: number;
  deltaLabel: string;
  icon: string;
}

export interface CategoryAggregate {
  name: string;
  count: number;
  visits: number;
  maxVisits: number;
  color: string;
}

export interface EntityRow {
  name: string;
  type: 'PLACE' | 'DESTINATION' | 'EVENT' | 'LOCATION';
  location: string;
  views: number;
  visitors: number;
  saves: number;
  actions: number;
  reviewAvg: number;
  reviewCount: number;
  trend: 'up' | 'stable' | 'down';
  imageBg: string;
}

export interface AutoInsight {
  type: 'success' | 'warning' | 'danger' | 'info';
  title: string;
  detail: string;
}

export interface ActionableInsight {
  icon: string;
  label: string;
  title: string;
  subtitle: string;
  highlight?: string;
  highlightClass?: string;
}

export type CategoryFilter = 'all' | 'locations' | 'destinations' | 'events' | 'places';

export type EntitySortField = 'views' | 'visitors' | 'saves' | 'reviewAvg' | 'name';

export interface DistributionSlice {
  label: string;
  count: number;
  color: string;
}

export interface RegionalAnalyticsData {
  metrics: Record<CategoryFilter, MetricCard[]>;
  categories: CategoryAggregate[];
  entityRows: EntityRow[];
  actionableInsights: ActionableInsight[];
  insights: AutoInsight[];
  distribution: Record<CategoryFilter, DistributionSlice[]>;
  pageViewsTrend: Record<CategoryFilter, number[]>;
}
