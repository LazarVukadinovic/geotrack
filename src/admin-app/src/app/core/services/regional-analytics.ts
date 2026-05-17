import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import {
  ActionableInsight,
  AutoInsight,
  CategoryFilter,
  DistributionSlice,
  EntityRow,
  MetricCard,
  RegionalAnalyticsActionableInsights,
  RegionalAnalyticsDistributionItem,
  RegionalAnalyticsEntityDetail,
  RegionalAnalyticsEntityDetailParams,
  RegionalAnalyticsEntityMetric,
  RegionalMonthlyVisits,
  RegionalMonthlyVisitsParams,
  RegionalTagViewsAnalyticsResponse,
  RegionalTagViewsParams,
  RegionalAnalyticsOverview,
  RegionalAnalyticsOverviewParams,
  RegionalAnalyticsTrendPoint,
} from '../models/regional-analytics.model';

@Injectable({ providedIn: 'root' })
export class RegionalAnalyticsService {
  private readonly http = inject(HttpClient);
  private readonly palette = ['#1252A0', '#2E7EC0', '#5ab0e8', '#f59e0b', '#10b981', '#8b5cf6'];

  getOverview(params: RegionalAnalyticsOverviewParams): Observable<RegionalAnalyticsOverview> {
    return this.http.get<RegionalAnalyticsOverview>('/api/admin/regional/analytics/overview', {
      params: this.toHttpParams(params),
    });
  }

  getMonthlyVisits(params: RegionalMonthlyVisitsParams): Observable<RegionalMonthlyVisits> {
    return this.http.get<RegionalMonthlyVisits>('/api/admin/regional/analytics/monthly-visits', {
      params: this.toMonthlyVisitHttpParams(params),
    });
  }

  getTagViews(params: RegionalTagViewsParams): Observable<RegionalTagViewsAnalyticsResponse> {
    return this.http.get<RegionalTagViewsAnalyticsResponse>('/api/admin/regional/analytics/tag-views', {
      params: this.toTagViewsHttpParams(params),
    });
  }

  getEntityDetail(params: RegionalAnalyticsEntityDetailParams): Observable<RegionalAnalyticsEntityDetail> {
    return this.http.get<RegionalAnalyticsEntityDetail>(
      `/api/admin/regional/analytics/entities/${encodeURIComponent(params.entityType)}/${params.entityId}/details`,
      {
        params: new HttpParams()
          .set('regionId', String(params.regionId))
          .set('period', params.period ?? '30d'),
      },
    );
  }

  exportCsv(params: RegionalAnalyticsOverviewParams): Observable<Blob> {
    return this.http.get('/api/admin/regional/analytics/export', {
      params: this.toHttpParams(params, false),
      responseType: 'blob',
    });
  }

  buildMetricCards(overview: RegionalAnalyticsOverview): MetricCard[] {
    const totals = overview.totals;
    const comparison = overview.comparison;

    return [
      {
        label: 'Page Views',
        value: this.formatNumber(totals.pageViews),
        delta: comparison.pageViewsChangePercent,
        deltaLabel: 'vs previous period',
        icon: 'trend',
      },
      {
        label: 'Unique Visitors',
        value: this.formatNumber(totals.uniqueVisitors),
        delta: comparison.uniqueVisitorsChangePercent,
        deltaLabel: 'vs previous period',
        icon: 'users',
      },
      {
        label: 'Saves',
        value: this.formatNumber(totals.saves),
        delta: comparison.savesChangePercent,
        deltaLabel: 'vs previous period',
        icon: 'bookmark',
      },
      {
        label: 'Average Rating',
        value: totals.averageRating.toFixed(1),
        delta: comparison.averageRatingChangePercent,
        deltaLabel: 'vs previous period',
        icon: 'star',
      },
      {
        label: 'Ext. Clicks',
        value: this.formatNumber(totals.externalClicks),
        delta: comparison.externalClicksChangePercent,
        deltaLabel: 'vs previous period',
        icon: 'link',
      },
      {
        label: 'Total Reviews',
        value: this.formatNumber(totals.totalReviews),
        delta: comparison.totalReviewsChangePercent,
        deltaLabel: 'vs previous period',
        icon: 'chat',
      },
    ];
  }

  mapEntityRows(rows: RegionalAnalyticsEntityMetric[]): EntityRow[] {
    return rows.map((row) => ({
      entityType: row.entityType,
      entityId: row.entityId,
      name: row.name,
      type: this.toUiType(row.entityType),
      location: row.regionDisplayName,
      views: row.pageViews,
      visitors: row.uniqueVisitors,
      saves: row.saves,
      actions: row.externalClicks,
      reviewAvg: row.averageRating,
      reviewCount: row.reviewCount,
      trend: row.pageViews > 0 || row.saves > 0 || row.externalClicks > 0 ? 'up' : 'stable',
      imageBg: row.imageUrl ? `url("${row.imageUrl}") center/cover` : this.fallbackGradient(row.entityType, row.entityId),
      imageUrl: row.imageUrl,
      status: row.status,
      detailRoute: row.detailRoute,
    }));
  }

  mapDistribution(items: RegionalAnalyticsDistributionItem[]): DistributionSlice[] {
    return items.map((item, index) => ({
      label: item.label,
      count: item.count,
      color: this.palette[index % this.palette.length],
    }));
  }

  mapTrend(points: RegionalAnalyticsTrendPoint[]): number[] {
    return points.map((point) => point.views);
  }

  buildActionableInsights(insights: RegionalAnalyticsActionableInsights): ActionableInsight[] {
    const cards: ActionableInsight[] = [];

    if (insights.topViewedLocation) {
      cards.push({
        icon: 'pin',
        label: 'TOP VIEWED LOCATION',
        title: insights.topViewedLocation.name,
        subtitle: `${this.formatNumber(insights.topViewedLocation.pageViews)} Views`,
        highlightClass: 'text-blue-600',
      });
    }

    if (insights.mostSaved) {
      cards.push({
        icon: 'bookmark',
        label: 'MOST SAVED',
        title: insights.mostSaved.name,
        subtitle: `${this.formatNumber(insights.mostSaved.saves)} Saves`,
        highlightClass: 'text-gray-700',
      });
    }

    if (insights.highestPerformingEvent) {
      cards.push({
        icon: 'calendar',
        label: 'HIGHEST PERFORMING EVENT',
        title: insights.highestPerformingEvent.name,
        subtitle: `${this.formatNumber(insights.highestPerformingEvent.pageViews)} Views`,
        highlightClass: 'text-gray-700',
      });
    }

    return cards;
  }

  buildAutoInsights(messages: string[]): AutoInsight[] {
    return messages.map((detail) => ({
      type: 'info',
      title: 'Analytics notice',
      detail,
    }));
  }

  asBucket<T>(filter: CategoryFilter, value: T, empty: T): Record<CategoryFilter, T> {
    return {
      all: filter === 'all' ? value : empty,
      locations: filter === 'locations' ? value : empty,
      destinations: filter === 'destinations' ? value : empty,
      events: filter === 'events' ? value : empty,
      places: filter === 'places' ? value : empty,
    };
  }

  downloadCsv(blob: Blob, activeRegionName: string, periodKey: string): void {
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    const safeRegion = activeRegionName.trim().replace(/[^a-z0-9]+/gi, '-').replace(/^-|-$/g, '') || 'region';
    anchor.href = url;
    anchor.download = `regional-analytics-${safeRegion}-${periodKey}.csv`;
    anchor.click();
    URL.revokeObjectURL(url);
  }

  getData$(params: RegionalAnalyticsOverviewParams) {
    return this.getOverview(params).pipe(
      map((overview) => ({
        metrics: this.asBucket('all', this.buildMetricCards(overview), [] as MetricCard[]),
        categories: [],
        entityRows: this.mapEntityRows(overview.entityRows),
        distribution: this.asBucket('all', this.mapDistribution(overview.entityTypeDistribution), [] as DistributionSlice[]),
        pageViewsTrend: this.asBucket('all', this.mapTrend(overview.pageViewsTrend), [] as number[]),
        actionableInsights: this.buildActionableInsights(overview.actionableInsights),
        insights: this.buildAutoInsights(overview.actionableInsights.messages ?? []),
      })),
    );
  }

  private toHttpParams(params: RegionalAnalyticsOverviewParams, includePaging = true): HttpParams {
    let httpParams = new HttpParams()
      .set('regionId', String(params.regionId))
      .set('period', params.period ?? '30d')
      .set('entityType', params.entityType ?? 'all');

    if (params.search?.trim()) {
      httpParams = httpParams.set('search', params.search.trim());
    }

    if (includePaging) {
      httpParams = httpParams
        .set('skip', String(params.skip ?? 0))
        .set('take', String(params.take ?? 25));
    }

    if (params.sort) {
      httpParams = httpParams.set('sort', params.sort);
    }

    return httpParams;
  }

  private toMonthlyVisitHttpParams(params: RegionalMonthlyVisitsParams): HttpParams {
    let httpParams = new HttpParams()
      .set('regionId', String(params.regionId))
      .set('selectedYearsBack', String(params.selectedYearsBack))
      .set('entityType', params.entityType ?? 'all');

    if (params.search?.trim()) {
      httpParams = httpParams.set('search', params.search.trim());
    }

    return httpParams;
  }

  private toTagViewsHttpParams(params: RegionalTagViewsParams): HttpParams {
    let httpParams = new HttpParams()
      .set('regionId', String(params.regionId))
      .set('period', params.period)
      .set('take', String(params.take ?? 15))
      .set('entityType', params.entityType ?? 'all');

    for (const tagId of params.tagIds ?? []) {
      httpParams = httpParams.append('tagIds', String(tagId));
    }

    return httpParams;
  }

  private toUiType(type: RegionalAnalyticsEntityMetric['entityType']): EntityRow['type'] {
    switch (type) {
      case 'destination':
        return 'DESTINATION';
      case 'location':
        return 'LOCATION';
      case 'event':
        return 'EVENT';
      case 'place':
      default:
        return 'PLACE';
    }
  }

  private formatNumber(value: number): string {
    if (value >= 1_000_000) return `${(value / 1_000_000).toFixed(1).replace(/\.0$/, '')}M`;
    if (value >= 1000) return `${(value / 1000).toFixed(1).replace(/\.0$/, '')}K`;
    return String(value);
  }

  private fallbackGradient(entityType: string, entityId: number): string {
    const seed = Math.abs(entityId + entityType.split('').reduce((sum, char) => sum + char.charCodeAt(0), 0));
    const colors = ['#0ea5e9', '#f97316', '#10b981', '#8b5cf6', '#e11d48', '#0891b2', '#2563eb'];
    const a = colors[seed % colors.length];
    const b = colors[(seed + 3) % colors.length];
    return `linear-gradient(135deg, ${a} 0%, ${b} 100%)`;
  }
}
