import { Injectable } from '@angular/core';
import { Observable, of } from 'rxjs';
import { delay } from 'rxjs/operators';
import {
  NationalAnalyticsData,
  NationalKpi,
  TopRegionHighlight,
  FastestGrowingRegion,
  TopEntityItem,
  PageViewSeries,
  TopDestination,
  TopEvent,
  ContentQualityPoint,
  RegionAnalyticRow,
} from '../../models/national-analytics.model';

// ─────────────────────────────────────────────────────────────────────────────
// MOCK DATA
// Pattern:  return this.http.get<T>(`${this.base}/...`);
// ─────────────────────────────────────────────────────────────────────────────

const MOCK_KPIS: NationalKpi[] = [
  { label: 'Total Page Views',       value: '2.4M',  change: '+12.5%', up: true,  icon: 'eye'      },
  { label: 'Unique Visitors',        value: '1.1M',  change: '+8.2%',  up: true,  icon: 'users'    },
  { label: 'External Link Clicks',   value: '320K',  change: '+15.3%', up: true,  icon: 'link'     },
  { label: 'Total Saves / Favorites',value: '450K',  change: '+5.1%',  up: true,  icon: 'bookmark' },
  { label: 'Total Reviews',          value: '120K',  change: '0%',     up: true,  icon: 'chat'     },
  { label: 'Average Rating',         value: '4.5/5', change: '+0.2',   up: true,  icon: 'star'     },
];

const MOCK_TOP_REGION: TopRegionHighlight = {
  name:  'Šumadija',
  score: 94,
  note:  'Leading in overall engagement and unique visitors this quarter.',
};

const MOCK_FASTEST_GROWING: FastestGrowingRegion = {
  name:   'Šumadija',
  change: '+24% visitor growth',
};

const MOCK_TOP_ENTITIES: TopEntityItem[] = [
  { name: 'Oplenac Complex',    sub: 'Place • Šumadija › Topola',      rank: 1, icon: 'place'       },
  { name: 'Oplenac Harvest Fest',sub: 'Event • Šumadija › Topola',     rank: 1, icon: 'event'       },
  { name: 'Šumadija Wine Route', sub: 'Destination • Šumadija',        rank: 2, icon: 'destination' },
];

const MOCK_PAGE_VIEWS_DATA: PageViewSeries[] = [
  { label: 'Šumadija',   color: '#2563eb', points: [30, 42, 38, 55, 48, 62, 58, 74, 68, 82, 79, 92] },
  { label: 'Banat',      color: '#16a34a', points: [20, 28, 25, 35, 32, 40, 38, 48, 44, 55, 52, 60] },
  { label: 'Pomoravlje', color: '#d97706', points: [15, 20, 18, 24, 22, 30, 28, 36, 33, 42, 40, 48] },
];

const MOCK_TOP_DESTINATIONS: TopDestination[] = [
  { name: 'Đavolja Varoš', region: 'Jug i Istok',  views: 98 },
  { name: 'Tara',           region: 'Šumadija',     views: 87 },
  { name: 'Fruška Gora',    region: 'Vojvodina',    views: 74 },
  { name: 'Uvac kanjon',    region: 'Raška',        views: 61 },
  { name: 'Zlatibor',       region: 'Zlatiborski',  views: 58 },
];

const MOCK_TOP_EVENTS: TopEvent[] = [
  { name: 'Exit Festival',      region: 'Vojvodina', saves: 42 },
  { name: 'Guča Sabor',         region: 'Šumadija',  saves: 38 },
  { name: 'Nišville Jazz',      region: 'Nišavski',  saves: 29 },
  { name: 'Oplenac Harvest',    region: 'Šumadija',  saves: 24 },
  { name: 'Belgrade Beer Fest', region: 'Beograd',   saves: 21 },
];

const MOCK_CONTENT_QUALITY: ContentQualityPoint[] = [
  { region: 'Šumadija',   reviews: 450, avgRating: 4.8 },
  { region: 'Banat',      reviews: 320, avgRating: 4.5 },
  { region: 'Raška',      reviews: 580, avgRating: 4.7 },
  { region: 'Pomoravlje', reviews: 250, avgRating: 4.0 },
  { region: 'Mačva',      reviews: 290, avgRating: 4.2 },
  { region: 'Podunavlje', reviews: 180, avgRating: 4.3 },
];

const MOCK_REGION_ROWS: RegionAnalyticRow[] = [
  { name: 'Šumadija',   refId: 'REG-001', pageViews: '1.2M', visitors: '450K', saves: '85K',  avgRating: 4.8, entities: '45 / 315 / 18', status: 'Active / Fast',     qualityScore: 92 },
  { name: 'Banat',      refId: 'REG-002', pageViews: '850K', visitors: '320K', saves: '62K',  avgRating: 4.5, entities: '28 / 190 / 5',  status: 'Processing Delays', qualityScore: 78 },
  { name: 'Raška',      refId: 'REG-003', pageViews: '1.5M', visitors: '580K', saves: '110K', avgRating: 4.7, entities: '65 / 480 / 32', status: 'Active / Normal',   qualityScore: 88 },
  { name: 'Pomoravlje', refId: 'REG-004', pageViews: '650K', visitors: '250K', saves: '45K',  avgRating: 4.0, entities: '40 / 275 / 21', status: 'Processing Delays', qualityScore: 71 },
  { name: 'Mačva',      refId: 'REG-005', pageViews: '720K', visitors: '290K', saves: '51K',  avgRating: 4.2, entities: '35 / 210 / 8',  status: 'Active / Fast',     qualityScore: 81 },
  { name: 'Podunavlje', refId: 'REG-006', pageViews: '540K', visitors: '210K', saves: '38K',  avgRating: 4.3, entities: '22 / 160 / 6',  status: 'Active / Normal',   qualityScore: 75 },
  { name: 'Bačka',      refId: 'REG-007', pageViews: '480K', visitors: '185K', saves: '31K',  avgRating: 3.9, entities: '19 / 140 / 4',  status: 'Inactive',          qualityScore: 62 },
];

// ─────────────────────────────────────────────────────────────────────────────

@Injectable({ providedIn: 'root' })
export class NationalAnalyticsService {

  private readonly MOCK_DELAY_MS = 300;

  // ── Full payload 
  // TODO: replace with → return this.http.get<NationalAnalyticsData>(`${this.base}/analytics/national`);
  getData$(): Observable<NationalAnalyticsData> {
    return of({
      kpis:            MOCK_KPIS,
      topRegion:       MOCK_TOP_REGION,
      fastestGrowing:  MOCK_FASTEST_GROWING,
      topEntities:     MOCK_TOP_ENTITIES,
      pageViewsData:   MOCK_PAGE_VIEWS_DATA,
      topDestinations: MOCK_TOP_DESTINATIONS,
      topEvents:       MOCK_TOP_EVENTS,
      contentQuality:  MOCK_CONTENT_QUALITY,
      regionRows:      MOCK_REGION_ROWS,
    }).pipe(delay(this.MOCK_DELAY_MS));
  }

  // TODO: replace each `of(...)` with the corresponding HTTP call.

  getKpis$(): Observable<NationalKpi[]> {
    // return this.http.get<NationalKpi[]>(`${this.base}/analytics/national/kpis`);
    return of(MOCK_KPIS).pipe(delay(this.MOCK_DELAY_MS));
  }

  getTopRegion$(): Observable<TopRegionHighlight> {
    // return this.http.get<TopRegionHighlight>(`${this.base}/analytics/national/top-region`);
    return of(MOCK_TOP_REGION).pipe(delay(this.MOCK_DELAY_MS));
  }

  getFastestGrowing$(): Observable<FastestGrowingRegion> {
    // return this.http.get<FastestGrowingRegion>(`${this.base}/analytics/national/fastest-growing`);
    return of(MOCK_FASTEST_GROWING).pipe(delay(this.MOCK_DELAY_MS));
  }

  getTopEntities$(): Observable<TopEntityItem[]> {
    // return this.http.get<TopEntityItem[]>(`${this.base}/analytics/national/top-entities`);
    return of(MOCK_TOP_ENTITIES).pipe(delay(this.MOCK_DELAY_MS));
  }

  getPageViewsData$(): Observable<PageViewSeries[]> {
    // return this.http.get<PageViewSeries[]>(`${this.base}/analytics/national/page-views`);
    return of(MOCK_PAGE_VIEWS_DATA).pipe(delay(this.MOCK_DELAY_MS));
  }

  getTopDestinations$(): Observable<TopDestination[]> {
    // return this.http.get<TopDestination[]>(`${this.base}/analytics/national/top-destinations`);
    return of(MOCK_TOP_DESTINATIONS).pipe(delay(this.MOCK_DELAY_MS));
  }

  getTopEvents$(): Observable<TopEvent[]> {
    // return this.http.get<TopEvent[]>(`${this.base}/analytics/national/top-events`);
    return of(MOCK_TOP_EVENTS).pipe(delay(this.MOCK_DELAY_MS));
  }

  getContentQuality$(): Observable<ContentQualityPoint[]> {
    // return this.http.get<ContentQualityPoint[]>(`${this.base}/analytics/national/content-quality`);
    return of(MOCK_CONTENT_QUALITY).pipe(delay(this.MOCK_DELAY_MS));
  }

  getRegionRows$(): Observable<RegionAnalyticRow[]> {
    // return this.http.get<RegionAnalyticRow[]>(`${this.base}/analytics/national/regions`);
    return of(MOCK_REGION_ROWS).pipe(delay(this.MOCK_DELAY_MS));
  }
}