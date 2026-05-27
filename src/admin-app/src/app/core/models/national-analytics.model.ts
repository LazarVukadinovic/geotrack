import { TerritoryAssignedTerritory, TerritoryHealth } from './dashboard.model';

export type TerritoryAnalyticsPeriod = '24h' | '7d' | '1m' | '3m' | '6m' | '1y';
export type TerritoryAnalyticsStatus = 'active' | 'inactive' | 'all';
export type TerritoryAnalyticsEntityType = 'destination' | 'location' | 'place' | 'event';
export type TerritoryAnalyticsIcon = 'eye' | 'users' | 'link' | 'star' | 'bookmark' | 'chat';

export interface TerritoryAnalyticsOverviewParams {
  territoryId?: number | null;
  period?: TerritoryAnalyticsPeriod | string | null;
  regionIds?: number[];
  status?: TerritoryAnalyticsStatus | string | null;
  skip?: number;
  take?: number;
  sort?: string | null;
}

export interface TerritoryAnalyticsFilters {
  period: TerritoryAnalyticsPeriod | string;
  startDate: string;
  endDate: string;
  selectedRegionIds: number[];
  status: TerritoryAnalyticsStatus | string;
}

export interface TerritoryAnalyticsTotals {
  totalPageViews: number;
  uniqueVisitors: number;
  externalLinkClicks: number;
  totalSaves: number;
  totalReviews: number;
  averageRating: number;
}

export interface TerritoryAnalyticsComparison {
  pageViewsChangePercent: number;
  uniqueVisitorsChangePercent: number;
  externalClicksChangePercent: number;
  savesChangePercent: number;
  reviewsChangePercent: number;
  averageRatingChange: number;
}

export interface TerritoryAnalyticsRegionHighlight {
  regionId: number;
  regionName: string;
  score: number;
  healthScore: number;
  statusLabel?: string | null;
  periodLabel?: string | null;
  description: string;
  visits: number;
  uniqueVisitors: number;
}

export interface TerritoryAnalyticsFastestGrowing {
  regionId: number;
  regionName: string;
  growthPercent: number;
  metricLabel: string;
}

export interface TerritoryAnalyticsTopEntity {
  entityType: TerritoryAnalyticsEntityType | string;
  entityId: number;
  name: string;
  regionName: string;
  rank: number;
  metricValue: number;
  metricLabel: string;
  imageUrl?: string | null;
  detailRoute?: string | null;
  publicRoute?: string | null;
}

export interface TerritoryAnalyticsPageViewsSeries {
  regionId: number;
  regionName: string;
  values: number[];
}

export interface TerritoryAnalyticsPageViewsOverTime {
  monthLabels: string[];
  series: TerritoryAnalyticsPageViewsSeries[];
}

export interface TerritoryAnalyticsTopContent {
  id: number;
  name: string;
  regionName: string;
  views: number;
  averageRating?: number | null;
  imageUrl?: string | null;
  detailRoute?: string | null;
}

export interface TerritoryAnalyticsTopEvent extends TerritoryAnalyticsTopContent {
  registrations?: number | null;
  saves?: number | null;
  eventDate?: string | null;
}

export interface TerritoryAnalyticsContentQuality {
  regionId: number;
  regionName: string;
  totalReviews: number;
  averageRating: number;
}

export interface TerritoryAnalyticsRegionalPerformanceItem {
  regionId: number;
  regionName: string;
  regionCode?: string | null;
  pageViews: number;
  visitors: number;
  saves: number;
  averageRating: number;
  destinationsCount: number;
  locationsCount: number;
  placesCount: number;
  eventsCount: number;
  statusLabel: string;
  healthScore: number;
  health?: TerritoryHealth | null;
  qualityScore: number;
  managerStatus: string;
  managerNames: string[];
}

export interface TerritoryAnalyticsRegionalPerformance {
  items: TerritoryAnalyticsRegionalPerformanceItem[];
  totalCount: number;
  skip: number;
  take: number;
  hasMore: boolean;
}

export interface TerritoryAnalyticsOverview {
  activeTerritory?: TerritoryAssignedTerritory | null;
  filters: TerritoryAnalyticsFilters;
  totals: TerritoryAnalyticsTotals;
  comparison: TerritoryAnalyticsComparison;
  topRegion?: TerritoryAnalyticsRegionHighlight | null;
  fastestGrowing?: TerritoryAnalyticsFastestGrowing | null;
  topEntities: TerritoryAnalyticsTopEntity[];
  pageViewsOverTime: TerritoryAnalyticsPageViewsOverTime;
  topDestinations: TerritoryAnalyticsTopContent[];
  topLocations: TerritoryAnalyticsTopContent[];
  topPlaces: TerritoryAnalyticsTopContent[];
  topEvents: TerritoryAnalyticsTopEvent[];
  contentQualityByRegion: TerritoryAnalyticsContentQuality[];
  regionalPerformance: TerritoryAnalyticsRegionalPerformance;
}

export interface TerritoryAnalyticsMetricCard {
  label: string;
  value: string;
  change: string;
  up: boolean;
  icon: TerritoryAnalyticsIcon;
}

export type NationalAnalyticsData = TerritoryAnalyticsOverview;
