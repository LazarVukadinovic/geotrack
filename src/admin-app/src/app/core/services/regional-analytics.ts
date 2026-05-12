import { Injectable, inject } from '@angular/core';
import { Observable, of } from 'rxjs';
import { RegionalAnalyticsData, MetricCard, EntityRow, ActionableInsight, AutoInsight, CategoryFilter, DistributionSlice, CategoryAggregate } from '../models/regional-analytics.model';
import { LocationsListService, LocationListItem } from './locations-list.service';
import { DestinationsListService, DestinationListItem } from './destinations-list.service';
import { EventsListService, EventListItem } from './events-list.service';
import { PlacesListService, PlaceListItem } from './places-list.service';

@Injectable({ providedIn: 'root' })
export class RegionalAnalyticsService {

  private readonly locationsSvc    = inject(LocationsListService);
  private readonly destinationsSvc = inject(DestinationsListService);
  private readonly eventsSvc       = inject(EventsListService);
  private readonly placesSvc       = inject(PlacesListService);

  getData$(): Observable<RegionalAnalyticsData> {
    const locations    = this.locationsSvc.getAllItems();
    const destinations = this.destinationsSvc.getAllItems();
    const events       = this.eventsSvc.getAllItems();
    const places       = this.placesSvc.getAllItems();

    const entityRows: EntityRow[] = [
      ...locations.map(i    => this.locationToRow(i)),
      ...destinations.map(i => this.destinationToRow(i)),
      ...events.map(i       => this.eventToRow(i)),
      ...places.map(i       => this.placeToRow(i)),
    ];

    return of({
      metrics:            this.buildMetrics(locations, destinations, events, places),
      categories:         this.buildCategories(locations, destinations, events, places),
      entityRows,
      distribution:       this.buildDistribution(locations, destinations, events, places),
      pageViewsTrend:     this.buildPageViewsTrend(locations, destinations, events, places),
      actionableInsights: this.buildActionableInsights(entityRows),
      insights:           this.buildAutoInsights(entityRows),
    });
  }

  // ── Row mappers ──────────────────────────────────────────────────────────────

  private locationToRow(i: LocationListItem): EntityRow {
    return {
      name: i.name, type: 'LOCATION', location: i.region, imageBg: i.imageBg,
      views: i.analyticsViews ?? 0, visitors: i.analyticsVisitors ?? 0,
      saves: i.analyticsSaves ?? 0, actions: i.analyticsActions ?? 0,
      reviewAvg: i.analyticsReviewAvg ?? 0, reviewCount: i.analyticsReviewCount ?? 0,
      trend: i.analyticsTrend ?? 'stable',
    };
  }

  private destinationToRow(i: DestinationListItem): EntityRow {
    return {
      name: i.name, type: 'DESTINATION', location: i.region, imageBg: i.imageBg,
      views: i.analyticsViews ?? 0, visitors: i.analyticsVisitors ?? 0,
      saves: i.analyticsSaves ?? 0, actions: i.analyticsActions ?? 0,
      reviewAvg: i.analyticsReviewAvg ?? 0, reviewCount: i.analyticsReviewCount ?? 0,
      trend: i.analyticsTrend ?? 'stable',
    };
  }

  private eventToRow(i: EventListItem): EntityRow {
    return {
      name: i.name, type: 'EVENT', location: i.region, imageBg: i.imageBg,
      views: i.analyticsViews ?? 0, visitors: i.analyticsVisitors ?? 0,
      saves: i.analyticsSaves ?? 0, actions: i.analyticsActions ?? 0,
      reviewAvg: i.analyticsReviewAvg ?? 0, reviewCount: i.analyticsReviewCount ?? 0,
      trend: i.analyticsTrend ?? 'stable',
    };
  }

  private placeToRow(i: PlaceListItem): EntityRow {
    return {
      name: i.name, type: 'PLACE', location: i.region, imageBg: i.imageBg,
      views: i.analyticsViews ?? 0, visitors: i.analyticsVisitors ?? 0,
      saves: i.analyticsSaves ?? 0, actions: i.analyticsActions ?? 0,
      reviewAvg: i.analyticsReviewAvg ?? 0, reviewCount: i.analyticsReviewCount ?? 0,
      trend: i.analyticsTrend ?? 'stable',
    };
  }

  // ── Metrics builder ──────────────────────────────────────────────────────────

  private buildMetrics(
    locations: LocationListItem[], destinations: DestinationListItem[],
    events: EventListItem[], places: PlaceListItem[],
  ): Record<CategoryFilter, MetricCard[]> {
    const mk = (items: { analyticsViews?: number; analyticsVisitors?: number; analyticsSaves?: number; analyticsActions?: number; analyticsReviewAvg?: number; analyticsReviewCount?: number }[], label: string): MetricCard[] => {
      const views    = items.reduce((s, i) => s + (i.analyticsViews    ?? 0), 0);
      const visitors = items.reduce((s, i) => s + (i.analyticsVisitors ?? 0), 0);
      const saves    = items.reduce((s, i) => s + (i.analyticsSaves    ?? 0), 0);
      const actions  = items.reduce((s, i) => s + (i.analyticsActions  ?? 0), 0);
      const reviews  = items.reduce((s, i) => s + (i.analyticsReviewCount ?? 0), 0);
      const rated    = items.filter(i => (i.analyticsReviewAvg ?? 0) > 0);
      const avgRating = rated.length > 0
        ? (rated.reduce((s, i) => s + (i.analyticsReviewAvg ?? 0), 0) / rated.length)
        : 0;
      const fmt = (n: number) => n >= 1_000_000 ? (n / 1_000_000).toFixed(1) + 'M' : n >= 1000 ? (n / 1000).toFixed(1) + 'K' : String(n);
      const suffix = label ? ` – ${label}` : '';
      return [
        { label: `Page Views${suffix}`,    value: fmt(views),    delta: 12.5, deltaLabel: 'vs last month', icon: 'trend'    },
        { label: `Unique Visitors${suffix}`, value: fmt(visitors), delta:  8.2, deltaLabel: 'vs last month', icon: 'users'    },
        { label: `Saves${suffix}`,         value: fmt(saves),    delta: 15.4, deltaLabel: 'vs last month', icon: 'bookmark' },
        { label: 'Average Rating',         value: avgRating.toFixed(1), delta: 0.5, deltaLabel: 'vs last month', icon: 'star'  },
        { label: `Ext. Clicks${suffix}`,   value: fmt(actions),  delta: -2.1, deltaLabel: 'vs last month', icon: 'link'     },
        { label: `Total Reviews${suffix}`, value: reviews.toLocaleString(), delta: 5.8, deltaLabel: 'vs last month', icon: 'chat' },
      ];
    };
    return {
      all:          mk([...locations, ...destinations, ...events, ...places], ''),
      locations:    mk(locations,    'Locations'),
      destinations: mk(destinations, 'Destinations'),
      events:       mk(events,       'Events'),
      places:       mk(places,       'Places'),
    };
  }

  // ── Category aggregates ──────────────────────────────────────────────────────

  private buildCategories(
    locations: LocationListItem[], destinations: DestinationListItem[],
    events: EventListItem[], places: PlaceListItem[],
  ): CategoryAggregate[] {
    const sum = (items: { analyticsViews?: number }[]) => items.reduce((s, i) => s + (i.analyticsViews ?? 0), 0);
    const locViews  = sum(locations);
    const destViews = sum(destinations);
    const evtViews  = sum(events);
    const plcViews  = sum(places);
    const maxVisits = Math.max(locViews, destViews, evtViews, plcViews);
    return [
      { name: 'Locations',    count: locations.length,    visits: locViews,  maxVisits, color: '#1a4db5' },
      { name: 'Destinations', count: destinations.length, visits: destViews, maxVisits, color: '#1e6bb8' },
      { name: 'Places',       count: places.length,       visits: plcViews,  maxVisits, color: '#2d8dd6' },
      { name: 'Events',       count: events.length,       visits: evtViews,  maxVisits, color: '#5ab0e8' },
    ];
  }

  // ── Distribution builder ─────────────────────────────────────────────────────

  private buildDistribution(
    locations: LocationListItem[], destinations: DestinationListItem[],
    events: EventListItem[], places: PlaceListItem[],
  ): Record<CategoryFilter, DistributionSlice[]> {
    const palette = ['#1252A0', '#2E7EC0', '#5ab0e8', '#f59e0b', '#10b981', '#8b5cf6'];
    const byCat = (items: { category: string }[]): DistributionSlice[] => {
      const counts: Record<string, number> = {};
      for (const i of items) counts[i.category] = (counts[i.category] ?? 0) + 1;
      return Object.entries(counts).sort((a, b) => b[1] - a[1])
        .map(([label, count], idx) => ({ label, count, color: palette[idx % palette.length] }));
    };
    return {
      all: [
        { label: 'Places',       count: places.length,       color: palette[0] },
        { label: 'Locations',    count: locations.length,    color: palette[1] },
        { label: 'Destinations', count: destinations.length, color: palette[2] },
        { label: 'Events',       count: events.length,       color: palette[3] },
      ],
      locations:    byCat(locations),
      destinations: byCat(destinations),
      events:       byCat(events),
      places:       byCat(places),
    };
  }

  // ── Page views trend ────────────────────────────────────────────────────────

  private buildPageViewsTrend(
    locations: LocationListItem[], destinations: DestinationListItem[],
    events: EventListItem[], places: PlaceListItem[],
  ): Record<import('../models/regional-analytics.model').CategoryFilter, number[]> {
    const gen = (items: { analyticsViews?: number; analyticsTrend?: string }[]): number[] => {
      const total = items.reduce((s, i) => s + (i.analyticsViews ?? 0), 0);
      if (total === 0) return Array(365).fill(0);
      const upCount   = items.filter(i => i.analyticsTrend === 'up').length;
      const downCount = items.filter(i => i.analyticsTrend === 'down').length;
      const dir = upCount > downCount ? 1 : upCount < downCount ? -1 : 0;
      const avg = total / 365;
      return Array.from({ length: 365 }, (_, d) => {
        const t     = d / 364;
        const trend = 1 + dir * 0.5 * t;
        const wave  = 0.18 * Math.sin(t * Math.PI * 6);
        const noise = (this.seededRand(d * 137 + (total % 1000)) - 0.5) * 0.25;
        return Math.max(0, Math.round(avg * (trend + wave + noise)));
      });
    };
    return {
      all:          gen([...locations, ...destinations, ...events, ...places]),
      locations:    gen(locations),
      destinations: gen(destinations),
      events:       gen(events),
      places:       gen(places),
    };
  }

  private seededRand(seed: number): number {
    const x = Math.sin(seed + 1) * 10000;
    return x - Math.floor(x);
  }

  // ── Actionable insights ──────────────────────────────────────────────────────

  private buildActionableInsights(rows: EntityRow[]): ActionableInsight[] {
    const topViewed   = [...rows].sort((a, b) => b.views - a.views)[0];
    const topSaved    = [...rows].sort((a, b) => b.saves - a.saves)[0];
    const topEvent    = [...rows].filter(r => r.type === 'EVENT').sort((a, b) => b.views - a.views)[0];
    const lowestRated = [...rows].filter(r => r.reviewCount > 50).sort((a, b) => a.reviewAvg - b.reviewAvg)[0];
    const trending    = [...rows].filter(r => r.trend === 'up').sort((a, b) => b.visitors - a.visitors)[0];

    const fmt = (n: number) => n >= 1000 ? (n / 1000).toFixed(1) + 'K' : String(n);
    const insights: ActionableInsight[] = [];
    if (topViewed) insights.push({ icon: 'pin', label: `TOP VIEWED ${topViewed.type}`, title: topViewed.name, subtitle: `${fmt(topViewed.views)} Views`, highlightClass: 'text-blue-600' });
    if (topSaved)  insights.push({ icon: 'bookmark', label: 'MOST SAVED', title: topSaved.name, subtitle: `${fmt(topSaved.saves)} Saves (High Intent)`, highlightClass: 'text-gray-700' });
    if (topEvent)  insights.push({ icon: 'calendar', label: 'HIGHEST PERFORMING EVENT', title: topEvent.name, subtitle: `${fmt(topEvent.views)} Views (Trending)`, highlightClass: 'text-gray-700' });
    if (lowestRated) insights.push({ icon: 'warning', label: 'ACTION REQUIRED', title: lowestRated.name, subtitle: `Avg. Rating ${lowestRated.reviewAvg.toFixed(1)}`, highlightClass: 'text-red-600' });
    if (trending)  insights.push({ icon: 'trend', label: 'FASTEST GROWING', title: trending.name, subtitle: `+Trending — ${fmt(trending.visitors)} Visitors`, highlightClass: 'text-gray-700' });
    return insights;
  }

  // ── Auto insights ────────────────────────────────────────────────────────────

  private buildAutoInsights(rows: EntityRow[]): AutoInsight[] {
    const topUp     = [...rows].filter(r => r.trend === 'up').sort((a, b) => b.views - a.views)[0];
    const topDown   = [...rows].filter(r => r.trend === 'down').sort((a, b) => a.reviewAvg - b.reviewAvg)[0];
    const topRated  = [...rows].sort((a, b) => b.reviewAvg - a.reviewAvg)[0];
    const lowRated  = [...rows].filter(r => r.reviewCount > 50).sort((a, b) => a.reviewAvg - b.reviewAvg)[0];
    const insights: AutoInsight[] = [];
    if (topUp)    insights.push({ type: 'success', title: `${topUp.name} — Trending Up`, detail: `${topUp.name} is gaining traction with ${(topUp.views / 1000).toFixed(1)}K views and a ${topUp.reviewAvg.toFixed(1)} avg. rating. Consider featuring it in regional promotions.` });
    if (topDown)  insights.push({ type: 'warning', title: `Engagement Drop — ${topDown.name}`, detail: `${topDown.name} shows declining engagement. Review visitor feedback and consider a content refresh or promotional push.` });
    if (topRated) insights.push({ type: 'info',    title: `Top Rated: ${topRated.name}`, detail: `${topRated.name} holds the highest average rating of ${topRated.reviewAvg.toFixed(1)} (${topRated.reviewCount} reviews). Use this as a benchmark for content quality.` });
    if (lowRated) insights.push({ type: 'danger',  title: `Low Satisfaction — ${lowRated.name}`, detail: `${lowRated.name} has an average rating of ${lowRated.reviewAvg.toFixed(1)} with ${lowRated.reviewCount} reviews. Investigate recurring complaints and prioritise improvements.` });
    return insights;
  }
}
