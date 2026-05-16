import { RegionalAssignedRegion } from './dashboard.model';

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
  entityType: RegionalAnalyticsEntityType;
  entityId: number;
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
  imageUrl?: string | null;
  status: string;
  detailRoute?: string | null;
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

export type RegionalAnalyticsEntityType = 'destination' | 'location' | 'place' | 'event';

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

export interface RegionalAnalyticsOverviewParams {
  regionId: number;
  period?: string;
  entityType?: RegionalAnalyticsEntityType | 'all';
  search?: string;
  skip?: number;
  take?: number;
  sort?: string;
}

export type RegionalMonthlyVisitYearsBack = 1 | 2 | 3;

export interface RegionalMonthlyVisitsParams {
  regionId: number;
  selectedYearsBack: RegionalMonthlyVisitYearsBack;
  entityType?: RegionalAnalyticsEntityType | 'all';
  search?: string;
}

export interface RegionalAnalyticsOverview {
  activeRegion: RegionalAssignedRegion;
  period: RegionalAnalyticsPeriod;
  totals: RegionalAnalyticsTotals;
  comparison: RegionalAnalyticsComparison;
  pageViewsTrend: RegionalAnalyticsTrendPoint[];
  topPerformingEntities: RegionalAnalyticsEntityMetric[];
  entityTypeDistribution: RegionalAnalyticsDistributionItem[];
  engagementFunnel: RegionalAnalyticsEngagementFunnel;
  actionableInsights: RegionalAnalyticsActionableInsights;
  visitorBehavior: RegionalVisitorBehaviorAnalytics;
  entityRows: RegionalAnalyticsEntityMetric[];
  pagination: RegionalAnalyticsPagination;
  pendingRequestsCount: number;
}

export interface RegionalMonthlyVisits {
  activeRegion: RegionalAssignedRegion;
  selectedYearsBack: RegionalMonthlyVisitYearsBack;
  monthLabels: string[];
  series: RegionalMonthlyVisitsSeries[];
  totalVisits: number;
  visitMetric: string;
  visitEventTypes: string[];
  timeZone: string;
}

export interface RegionalMonthlyVisitsSeries {
  yearLabel: string;
  year: number;
  startDate: string;
  endDate: string;
  monthlyVisitCounts: number[];
  totalVisits: number;
}

export interface RegionalAnalyticsPeriod {
  key: string;
  startDate?: string | null;
  endDate: string;
}

export interface RegionalAnalyticsTotals {
  pageViews: number;
  uniqueVisitors: number;
  saves: number;
  averageRating: number;
  externalClicks: number;
  totalReviews: number;
}

export interface RegionalAnalyticsComparison {
  pageViewsChangePercent: number;
  uniqueVisitorsChangePercent: number;
  savesChangePercent: number;
  averageRatingChangePercent: number;
  externalClicksChangePercent: number;
  totalReviewsChangePercent: number;
}

export interface RegionalAnalyticsTrendPoint {
  date: string;
  views: number;
  uniqueVisitors: number;
}

export interface RegionalAnalyticsEntityMetric {
  entityType: RegionalAnalyticsEntityType;
  entityId: number;
  name: string;
  regionDisplayName: string;
  imageUrl?: string | null;
  categoryLabel?: string | null;
  pageViews: number;
  uniqueVisitors: number;
  saves: number;
  externalClicks: number;
  averageRating: number;
  reviewCount: number;
  status: string;
  detailRoute?: string | null;
}

export interface RegionalAnalyticsDistributionItem {
  entityType: string;
  label: string;
  count: number;
}

export interface RegionalAnalyticsEngagementFunnel {
  pageViews: number;
  uniqueVisitors: number;
  saves: number;
  externalClicks: number;
  reviews: number;
}

export interface RegionalAnalyticsActionableInsights {
  topViewedLocation?: RegionalAnalyticsEntityMetric | null;
  mostSaved?: RegionalAnalyticsEntityMetric | null;
  highestPerformingEvent?: RegionalAnalyticsEntityMetric | null;
  messages: string[];
}

export interface RegionalVisitorBehaviorAnalytics {
  loggedInVisitors: number;
  anonymousVisitors: number;
  returningVisitors: number;
  returningVisitorRatePercent: number;
  topTimeSpentEntity?: RegionalTimeSpentEntity | null;
  topTimeSpentPages: RegionalTimeSpentEntity[];
  hasTimeSpentData: boolean;
  notes: string[];
}

export interface RegionalTimeSpentEntity {
  pageOrEntityName: string;
  entityType: RegionalAnalyticsEntityType | string;
  entityId?: number | null;
  routeOrPage?: string | null;
  averageTimeSpentSeconds: number;
  totalTimeSpentSeconds: number;
  visitCount: number;
}

export interface RegionalAnalyticsPagination {
  totalCount: number;
  skip: number;
  take: number;
  hasMore: boolean;
}

export interface RegionalSidebarSummary {
  assignedRegions: RegionalAssignedRegion[];
  activeRegion?: RegionalAssignedRegion | null;
  pendingRequestsCount: number;
}
