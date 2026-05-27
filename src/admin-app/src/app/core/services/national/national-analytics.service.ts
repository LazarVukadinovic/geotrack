import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  TerritoryAnalyticsMetricCard,
  TerritoryAnalyticsOverview,
  TerritoryAnalyticsOverviewParams,
} from '../../models/national-analytics.model';

@Injectable({ providedIn: 'root' })
export class NationalAnalyticsService {
  private readonly http = inject(HttpClient);

  getOverview(params: TerritoryAnalyticsOverviewParams): Observable<TerritoryAnalyticsOverview> {
    return this.http.get<TerritoryAnalyticsOverview>('/api/admin/territory/analytics/overview', {
      params: this.toHttpParams(params),
    });
  }

  exportCsv(params: TerritoryAnalyticsOverviewParams): Observable<Blob> {
    return this.http.get('/api/admin/territory/analytics/export', {
      params: this.toHttpParams(params, false),
      responseType: 'blob',
    });
  }

  buildMetricCards(overview: TerritoryAnalyticsOverview): TerritoryAnalyticsMetricCard[] {
    const totals = overview.totals;
    const comparison = overview.comparison;

    return [
      {
        label: 'Total Page Views',
        value: this.formatNumber(totals.totalPageViews),
        change: this.formatPercent(comparison.pageViewsChangePercent),
        up: comparison.pageViewsChangePercent >= 0,
        icon: 'eye',
      },
      {
        label: 'Unique Visitors',
        value: this.formatNumber(totals.uniqueVisitors),
        change: this.formatPercent(comparison.uniqueVisitorsChangePercent),
        up: comparison.uniqueVisitorsChangePercent >= 0,
        icon: 'users',
      },
      {
        label: 'External Link Clicks',
        value: this.formatNumber(totals.externalLinkClicks),
        change: this.formatPercent(comparison.externalClicksChangePercent),
        up: comparison.externalClicksChangePercent >= 0,
        icon: 'link',
      },
      {
        label: 'Total Saves / Favorites',
        value: this.formatNumber(totals.totalSaves),
        change: this.formatPercent(comparison.savesChangePercent),
        up: comparison.savesChangePercent >= 0,
        icon: 'bookmark',
      },
      {
        label: 'Total Reviews',
        value: this.formatNumber(totals.totalReviews),
        change: this.formatPercent(comparison.reviewsChangePercent),
        up: comparison.reviewsChangePercent >= 0,
        icon: 'chat',
      },
      {
        label: 'Average Rating',
        value: `${Number(totals.averageRating || 0).toFixed(1)}/5`,
        change: this.formatRatingChange(comparison.averageRatingChange),
        up: comparison.averageRatingChange >= 0,
        icon: 'star',
      },
    ];
  }

  downloadCsv(blob: Blob, activeTerritoryName: string, periodKey: string): void {
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    const safeTerritory = activeTerritoryName.trim().replace(/[^a-z0-9]+/gi, '-').replace(/^-|-$/g, '') || 'territory';
    anchor.href = url;
    anchor.download = `territory-analytics-${safeTerritory}-${periodKey}.csv`;
    anchor.click();
    URL.revokeObjectURL(url);
  }

  formatNumber(value: number): string {
    return new Intl.NumberFormat('en', {
      notation: 'compact',
      maximumFractionDigits: value >= 1_000 ? 1 : 0,
    }).format(Math.max(0, Number(value) || 0));
  }

  private formatPercent(value: number): string {
    const numeric = Number(value) || 0;
    const prefix = numeric > 0 ? '+' : '';
    return `${prefix}${numeric.toFixed(Math.abs(numeric) % 1 === 0 ? 0 : 1)}%`;
  }

  private formatRatingChange(value: number): string {
    const numeric = Number(value) || 0;
    const prefix = numeric > 0 ? '+' : '';
    return `${prefix}${numeric.toFixed(1)}`;
  }

  private toHttpParams(params: TerritoryAnalyticsOverviewParams, includePaging = true): HttpParams {
    let httpParams = new HttpParams()
      .set('period', params.period ?? '1m')
      .set('status', params.status ?? 'active');

    if (params.territoryId && params.territoryId > 0) {
      httpParams = httpParams.set('territoryId', String(params.territoryId));
    }

    for (const regionId of params.regionIds ?? []) {
      if (regionId > 0) {
        httpParams = httpParams.append('regionIds', String(regionId));
      }
    }

    if (params.sort?.trim()) {
      httpParams = httpParams.set('sort', params.sort.trim());
    }

    if (includePaging) {
      httpParams = httpParams
        .set('skip', String(Math.max(0, params.skip ?? 0)))
        .set('take', String(Math.max(1, params.take ?? 5)));
    }

    return httpParams;
  }
}
