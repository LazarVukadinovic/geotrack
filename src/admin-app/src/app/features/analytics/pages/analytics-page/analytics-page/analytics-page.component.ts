import { Component, signal, computed, inject, OnInit, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SidebarComponent } from '../../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../../shared/components/topbar/topbar.component';
import {MetricCard, CategoryAggregate, EntityRow, AutoInsight, ActionableInsight, CategoryFilter, DistributionSlice, EntitySortField } from '../../../../../core/models/regional-analytics.model';
import { RegionalAnalyticsService } from '../../../../../core/services/regional-analytics';

export type { MetricCard, CategoryAggregate, EntityRow, AutoInsight, ActionableInsight, CategoryFilter };

@Component({
  selector: 'app-analytics-page',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './analytics-page.component.html',
})
export class AnalyticsPageComponent implements OnInit {

  private readonly analyticsService = inject(RegionalAnalyticsService);

  protected Math = Math;

  region   = 'Šumadija, Srbija';
  userName = 'Sarah Jenkins';
  userRole = 'Regional Manager';

  // ── Period filter ────────────────────────────────────────────────────
  selectedPeriod         = 'Last 30 Days';
  private previousPeriod = 'Last 30 Days';
  private selectedPeriodSig = signal('Last 30 Days');

  get customDays(): number {
    if (!this.customDateFrom || !this.customDateTo) return 30;
    const [d1, m1, y1] = this.customDateFrom.split('.').map(Number);
    const [d2, m2, y2] = this.customDateTo.split('.').map(Number);
    const from = new Date(y1, m1 - 1, d1);
    const to   = new Date(y2, m2 - 1, d2);
    return Math.max(1, Math.round((to.getTime() - from.getTime()) / 86_400_000) + 1);
  }
  showCustomDateModal = false;
  customDateFrom = '';
  customDateTo   = '';
  fromDay = ''; fromMonth = ''; fromYear = '';
  toDay   = ''; toMonth   = ''; toYear   = '';

  readonly periodOptions = ['Last 7 Days', 'Last 15 Days', 'Last 30 Days', 'Last 6 Months', 'Last 12 Months', 'Custom'];

  get canApply(): boolean {
    return this.fromDay.length === 2 && this.fromMonth.length === 2 && this.fromYear.length === 4
        && this.toDay.length === 2   && this.toMonth.length === 2   && this.toYear.length === 4;
  }

  // ── Type / category filter ───────────────────────────────────────────
  activeFilter = signal<CategoryFilter>('all');

  filters: { key: CategoryFilter; label: string }[] = [
    { key: 'all',          label: 'All Categories' },
    { key: 'locations',    label: 'Locations'      },
    { key: 'destinations', label: 'Destinations'   },
    { key: 'events',       label: 'Events'         },
    { key: 'places',       label: 'Places'         },
  ];

  readonly typeOptions = ['All Entities', 'Destination', 'Location', 'Place', 'Event'];

  private readonly filterToTypeMap: Record<CategoryFilter, string> = {
    all: 'All Entities', locations: 'Location', destinations: 'Destination', events: 'Event', places: 'Place',
  };
  private readonly typeToFilterMap: Record<string, CategoryFilter> = {
    'All Entities': 'all', 'Location': 'locations', 'Destination': 'destinations', 'Event': 'events', 'Place': 'places',
  };

  get selectedType(): string { return this.filterToTypeMap[this.activeFilter()]; }
  set selectedType(v: string) { this.activeFilter.set(this.typeToFilterMap[v] ?? 'all'); }

  setFilter(f: CategoryFilter): void { this.activeFilter.set(f); }

  // ── Search ───────────────────────────────────────────────────────────
  searchQuery = signal('');

  // ── Sort ─────────────────────────────────────────────────────────────
  sortOpen      = false;
  sortFieldSig  = signal<EntitySortField>('views');
  sortDirSig    = signal<'asc' | 'desc'>('desc');

  readonly sortOptions: { label: string; field: EntitySortField; dir: 'asc' | 'desc' }[] = [
    { label: 'Most Views',    field: 'views',     dir: 'desc' },
    { label: 'Most Visitors', field: 'visitors',  dir: 'desc' },
    { label: 'Most Saves',    field: 'saves',     dir: 'desc' },
    { label: 'Best Rating',   field: 'reviewAvg', dir: 'desc' },
    { label: 'A–Z',           field: 'name',      dir: 'asc'  },
    { label: 'Z–A',           field: 'name',      dir: 'desc' },
  ];

  currentSortLabel = computed(() => {
    const f = this.sortFieldSig(), d = this.sortDirSig();
    return this.sortOptions.find(o => o.field === f && o.dir === d)?.label ?? 'Most Views';
  });

  // ── Page views trend chart ───────────────────────────────────────────
  private pageViewsTrendSig = signal<Record<CategoryFilter, number[]>>({
    all: [], locations: [], destinations: [], events: [], places: [],
  });

  trendChart = computed(() => this.buildTrendChart(
    this.pageViewsTrendSig()[this.activeFilter()] ?? [],
    this.selectedPeriodSig(),
  ));

  private buildTrendChart(raw: number[], period: string): {
    linePath: string; areaPath: string;
    yLabels: { y: number; text: string }[];
    xLabels: { x: number; text: string }[];
    subtitle: string;
  } {
    const { points, xLabels, subtitle } = this.getPeriodSlice(raw, period);

    const fallback = {
      linePath: '', areaPath: '',
      yLabels: [
        { y: 18, text: '8K' }, { y: 58, text: '6K' },
        { y: 98, text: '4K' }, { y: 138, text: '2K' },
      ],
      xLabels, subtitle,
    };
    if (points.length === 0 || points.every(v => v === 0)) return fallback;

    const maxVal = Math.max(...points);
    const step   = this.niceStep(maxVal);
    const n      = points.length;

    const toX = (i: number) => 24 + (n > 1 ? i / (n - 1) : 0) * 292;
    const toY = (v: number) => Math.min(148, 134 - ((v - step) / (step * 3)) * 120);

    const pts      = points.map((v, i) => ({ x: toX(i), y: toY(v) }));
    const linePath = this.smoothSVGPath(pts);
    const areaPath = `${linePath} L${pts[n - 1].x.toFixed(1)},148 L${pts[0].x.toFixed(1)},148 Z`;

    return {
      linePath, areaPath,
      yLabels: [
        { y: 18,  text: this.fmtK(step * 4) },
        { y: 58,  text: this.fmtK(step * 3) },
        { y: 98,  text: this.fmtK(step * 2) },
        { y: 138, text: this.fmtK(step) },
      ],
      xLabels, subtitle,
    };
  }

  private getPeriodSlice(raw: number[], period: string): {
    points: number[];
    xLabels: { x: number; text: string }[];
    subtitle: string;
  } {
    const px = (i: number, n: number) => +(24 + (n > 1 ? i / (n - 1) : 0) * 292).toFixed(1);

    if (period === 'Last 7 Days') {
      const pts = raw.slice(-7);
      return {
        points: pts,
        xLabels: pts.map((_, i) => ({ x: px(i, pts.length), text: String(i + 1) })),
        subtitle: 'Rolling 7-day view engagement',
      };
    }

    if (period === 'Last 15 Days') {
      const pts = raw.slice(-15);
      const n   = pts.length;
      return {
        points: pts,
        xLabels: [0, 4, 9, 14].map(i => ({ x: px(i, n), text: String(i + 1) })),
        subtitle: 'Rolling 15-day view engagement',
      };
    }

    if (period === 'Last 6 Months') {
      const src  = raw.length >= 180 ? raw.slice(-180) : raw;
      const size = Math.floor(src.length / 6);
      const pts  = Array.from({ length: 6 }, (_, i) =>
        src.slice(i * size, (i + 1) * size).reduce((s, v) => s + v, 0)
      );
      const labels = this.getLastNMonthLabels(6);
      return {
        points: pts,
        xLabels: pts.map((_, i) => ({ x: px(i, pts.length), text: labels[i] })),
        subtitle: 'Monthly view engagement – last 6 months',
      };
    }

    if (period === 'Last 12 Months') {
      const pts = Array.from({ length: 12 }, (_, i) => {
        const start = Math.round(i * raw.length / 12);
        const end   = Math.round((i + 1) * raw.length / 12);
        return raw.slice(start, end).reduce((s, v) => s + v, 0);
      });
      const labels = this.getLastNMonthLabels(12);
      return {
        points: pts,
        xLabels: pts.map((_, i) => ({ x: px(i, pts.length), text: labels[i] })),
        subtitle: 'Monthly view engagement – last 12 months',
      };
    }

    if (period === 'Custom') {
      const days = this.customDays;
      const pts  = raw.slice(-Math.min(days, raw.length));
      const n    = pts.length;
      const cnt  = n <= 7 ? n : Math.min(n, 6);
      const xLabels = cnt <= 1
        ? [{ x: px(0, n), text: '1' }]
        : Array.from({ length: cnt }, (_, j) => {
            const i = Math.round(j * (n - 1) / (cnt - 1));
            return { x: px(i, n), text: String(i + 1) };
          });
      return { points: pts, xLabels, subtitle: `Custom period – ${n} day${n !== 1 ? 's' : ''}` };
    }

    // Default: Last 30 Days
    const pts = raw.slice(-30);
    const n   = pts.length;
    return {
      points: pts,
      xLabels: ([[ 0,'1'],[4,'5'],[9,'10'],[14,'15'],[19,'20'],[n-1,'30']] as [number,string][])
        .map(([i, text]) => ({ x: px(i, n), text })),
      subtitle: 'Rolling 30-day view engagement',
    };
  }

  private getLastNMonthLabels(count: number): string[] {
    const abbr = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];
    const now  = new Date();
    return Array.from({ length: count }, (_, i) => {
      const d = new Date(now.getFullYear(), now.getMonth() - (count - 1 - i), 1);
      return abbr[d.getMonth()];
    });
  }

  private niceStep(maxVal: number): number {
    if (maxVal <= 0) return 100;
    const minStep  = maxVal / 4;
    const mag      = Math.pow(10, Math.floor(Math.log10(minStep)));
    const norm     = minStep / mag;
    const niceBase = norm <= 1 ? 1 : norm <= 2 ? 2 : norm <= 2.5 ? 2.5 : norm <= 5 ? 5 : 10;
    return niceBase * mag;
  }

  private fmtK(n: number): string {
    if (n >= 1_000_000) return (n / 1_000_000).toFixed(1).replace(/\.0$/, '') + 'M';
    if (n >= 1000)      return (n / 1000).toFixed(1).replace(/\.0$/, '') + 'K';
    return Math.round(n).toString();
  }

  private smoothSVGPath(pts: { x: number; y: number }[]): string {
    if (pts.length === 0) return '';
    if (pts.length === 1) return `M${pts[0].x.toFixed(1)},${pts[0].y.toFixed(1)}`;
    let d = `M${pts[0].x.toFixed(1)},${pts[0].y.toFixed(1)}`;
    for (let i = 1; i < pts.length; i++) {
      const p0  = pts[i - 2] ?? pts[i - 1];
      const p1  = pts[i - 1];
      const p2  = pts[i];
      const p3  = pts[i + 1] ?? pts[i];
      const cp1 = { x: p1.x + (p2.x - p0.x) / 6, y: p1.y + (p2.y - p0.y) / 6 };
      const cp2 = { x: p2.x - (p3.x - p1.x) / 6, y: p2.y - (p3.y - p1.y) / 6 };
      d += ` C${cp1.x.toFixed(1)},${cp1.y.toFixed(1)} ${cp2.x.toFixed(1)},${cp2.y.toFixed(1)} ${p2.x.toFixed(1)},${p2.y.toFixed(1)}`;
    }
    return d;
  }

  // ── Data signals ─────────────────────────────────────────────────────
  private metricsByFilterSig = signal<Record<CategoryFilter, MetricCard[]>>({
    all: [], locations: [], destinations: [], events: [], places: [],
  });
  private entityRowsSig      = signal<EntityRow[]>([]);
  private distributionSig    = signal<Record<CategoryFilter, DistributionSlice[]>>({
    all: [], locations: [], destinations: [], events: [], places: [],
  });
  actionableInsightsSig = signal<ActionableInsight[]>([]);
  insightsSig           = signal<AutoInsight[]>([]);

  // ── Computed: metrics ────────────────────────────────────────────────
  filteredMetrics = computed(() => this.metricsByFilterSig()[this.activeFilter()] ?? []);

  // ── Computed: entity rows ────────────────────────────────────────────
  private typeFilteredRows = computed(() => {
    const f = this.activeFilter();
    const q = this.searchQuery().toLowerCase();
    const rows = this.entityRowsSig();
    const typeMap: Record<CategoryFilter, EntityRow['type'][]> = {
      all: [], locations: ['LOCATION'], destinations: ['DESTINATION'], events: ['EVENT'], places: ['PLACE'],
    };
    let result = f === 'all' ? rows : rows.filter(r => typeMap[f].includes(r.type));
    if (q) result = result.filter(r => r.name.toLowerCase().includes(q) || r.location.toLowerCase().includes(q));
    return result;
  });

  sortedRows = computed(() => {
    const rows  = [...this.typeFilteredRows()];
    const field = this.sortFieldSig();
    const mult  = this.sortDirSig() === 'desc' ? -1 : 1;
    return rows.sort((a, b) => {
      const av = (a as unknown as Record<string, unknown>)[field];
      const bv = (b as unknown as Record<string, unknown>)[field];
      if (typeof av === 'number' && typeof bv === 'number') return (av - bv) * mult;
      return String(av).localeCompare(String(bv)) * mult;
    });
  });

  visibleCount  = signal(10);
  tableHasMore  = computed(() => this.visibleCount() < this.sortedRows().length);
  showMore(): void { this.visibleCount.update(n => n + 10); }

  // ── Computed: top performers (bar chart) ─────────────────────────────
  topPerformers = computed(() =>
    [...this.typeFilteredRows()].sort((a, b) => b.views - a.views).slice(0, 5)
  );

  topPerformerBarPct(e: EntityRow): number {
    const top = this.topPerformers()[0];
    return top ? Math.round((e.views / top.views) * 100) : 0;
  }

  // ── Computed: donut chart ────────────────────────────────────────────
  private readonly CIRCUMFERENCE = 2 * Math.PI * 64;

  donutArcs = computed(() => {
    const slices = this.distributionSig()[this.activeFilter()] ?? [];
    const total  = slices.reduce((s, x) => s + x.count, 0);
    let offset   = 0;
    return slices.map(s => {
      const arc = total > 0 ? (s.count / total) * this.CIRCUMFERENCE : 0;
      const item = { ...s, dasharray: `${arc.toFixed(1)} ${(this.CIRCUMFERENCE - arc).toFixed(1)}`, dashoffset: `-${offset.toFixed(1)}` };
      offset += arc;
      return item;
    });
  });

  donutTotal = computed(() =>
    (this.distributionSig()[this.activeFilter()] ?? []).reduce((s, x) => s + x.count, 0)
  );

  donutTotalDisplay = computed(() => {
    const n = this.donutTotal();
    return n > 999 ? (n / 1000).toFixed(1) + 'k' : String(n);
  });

  distributionChartTitle = computed(() => {
    const m: Record<CategoryFilter, string> = {
      all:          'Entity Type Distribution',
      places:       'Place Type Distribution',
      destinations: 'Destination Type Distribution',
      events:       'Event Type Distribution',
      locations:    'Location Type Distribution',
    };
    return m[this.activeFilter()];
  });

  distributionChartSubtitle = computed(() => {
    const m: Record<CategoryFilter, string> = {
      all:          'Inventory breakdown',
      places:       'Distribution by place type',
      destinations: 'Distribution by destination type',
      events:       'Distribution by event type',
      locations:    'Distribution by location type',
    };
    return m[this.activeFilter()];
  });

  topPerformingTitle = computed(() => {
    const m: Record<CategoryFilter, string> = {
      all:          'Top Performing Entities',
      places:       'Top Performing Places',
      destinations: 'Top Performing Destinations',
      events:       'Top Performing Events',
      locations:    'Top Performing Locations',
    };
    return m[this.activeFilter()];
  });

  entityDetailsTitle = computed(() => {
    const m: Record<CategoryFilter, string> = {
      all:          'Entity Performance Details',
      places:       'Place Performance Details',
      destinations: 'Destination Performance Details',
      events:       'Event Performance Details',
      locations:    'Location Performance Details',
    };
    return m[this.activeFilter()];
  });

  entityTypeWord = computed(() => {
    const m: Record<CategoryFilter, string> = {
      all: 'entities', places: 'places', destinations: 'destinations', events: 'events', locations: 'locations',
    };
    return m[this.activeFilter()];
  });

  searchPlaceholder = computed(() => {
    const m: Record<CategoryFilter, string> = {
      all:          'Search entities...',
      places:       'Search places...',
      destinations: 'Search destinations...',
      events:       'Search events...',
      locations:    'Search locations...',
    };
    return m[this.activeFilter()];
  });

  // ── Computed: engagement funnel ──────────────────────────────────────
  funnelData = computed(() => {
    const rows     = this.typeFilteredRows();
    const views    = rows.reduce((s, r) => s + r.views,       0);
    const visitors = rows.reduce((s, r) => s + r.visitors,    0);
    const saves    = rows.reduce((s, r) => s + r.saves,       0);
    const actions  = rows.reduce((s, r) => s + r.actions,     0);
    const reviews  = rows.reduce((s, r) => s + r.reviewCount, 0);
    const base = views || 1;
    const pct  = (v: number) => Math.min(100, Math.round(v / base * 100));
    return [
      { label: 'Page Views',      value: this.formatNum(views),    pct: 100,          color: '#185FA5' },
      { label: 'Unique Visitors', value: this.formatNum(visitors), pct: pct(visitors), color: '#2d8dd6' },
      { label: 'Saved',           value: this.formatNum(saves),    pct: pct(saves),    color: '#38bdf8' },
      { label: 'Ext. Clicks',     value: this.formatNum(actions),  pct: pct(actions),  color: '#60c4f5' },
      { label: 'Reviews',         value: this.formatNum(reviews),  pct: pct(reviews),  color: '#f59e0b' },
    ];
  });

  // ── Lifecycle ────────────────────────────────────────────────────────
  ngOnInit(): void {
    this.analyticsService.getData$().subscribe(data => {
      this.metricsByFilterSig.set(data.metrics);
      this.entityRowsSig.set(data.entityRows);
      this.distributionSig.set(data.distribution);
      this.pageViewsTrendSig.set(data.pageViewsTrend);
      this.actionableInsightsSig.set(data.actionableInsights);
      this.insightsSig.set(data.insights);
    });
  }

  // ── Event handlers ───────────────────────────────────────────────────
  @HostListener('document:click')
  onDocumentClick(): void { if (this.sortOpen) this.sortOpen = false; }

  onPeriodChange(value: string): void {
    if (value === 'Custom') {
      if (this.customDateFrom) { const p = this.customDateFrom.split('.'); this.fromDay = p[0]; this.fromMonth = p[1]; this.fromYear = p[2]; }
      if (this.customDateTo)   { const p = this.customDateTo.split('.');   this.toDay   = p[0]; this.toMonth   = p[1]; this.toYear   = p[2]; }
      this.showCustomDateModal = true;
    } else {
      this.previousPeriod = value;
      this.selectedPeriodSig.set(value);
      this.customDateFrom = this.customDateTo = '';
      this.fromDay = this.fromMonth = this.fromYear = this.toDay = this.toMonth = this.toYear = '';
    }
  }

  applyCustomRange(): void {
    if (!this.canApply) return;
    this.customDateFrom      = `${this.fromDay}.${this.fromMonth}.${this.fromYear}`;
    this.customDateTo        = `${this.toDay}.${this.toMonth}.${this.toYear}`;
    this.showCustomDateModal = false;
    this.previousPeriod      = 'Custom';
    this.selectedPeriodSig.set('Custom');
  }

  cancelCustomRange(): void {
    this.selectedPeriod      = this.previousPeriod;
    this.selectedPeriodSig.set(this.previousPeriod);
    this.showCustomDateModal = false;
  }

  onPartInput(event: Event, nextEl: HTMLInputElement): void {
    const el = event.target as HTMLInputElement;
    if (el.value.length >= el.maxLength) { nextEl.focus(); nextEl.select(); }
  }

  applySortOption(opt: { field: EntitySortField; dir: 'asc' | 'desc' }): void {
    this.sortFieldSig.set(opt.field);
    this.sortDirSig.set(opt.dir);
    this.sortOpen = false;
  }

  exportData(): void { console.log('Export CSV'); }

  // ── Seasonal heatmap ─────────────────────────────────────────────────
  months        = ['J', 'F', 'M', 'A', 'M', 'J', 'J', 'A', 'S', 'O', 'N', 'D'];
  monthIntensity = [2, 2, 4, 6, 8, 9, 10, 10, 8, 6, 2, 1];

  private readonly heatPalette = [
    '#EBF3FC', '#D1E5F7', '#AAD0F0', '#7DB8E8',
    '#4E9BD4', '#2E7EC0', '#185FA5', '#1252A0',
    '#0F4F9C', '#0B3E82', '#082F68', '#052150',
  ];
  heatmapBg(i: number): string { return this.heatPalette[Math.min(i, this.heatPalette.length - 1)]; }
  heatmapFg(i: number): string { return i >= 5 ? '#EBF3FC' : '#1a2f6b'; }

  // ── Display helpers ───────────────────────────────────────────────────
  formatNum(n: number): string { return n >= 1000 ? (n / 1000).toFixed(1) + 'K' : String(n); }

  trendLabel(t: EntityRow['trend']): string {
    return t === 'up' ? 'Trending' : t === 'down' ? 'Low Engagement' : 'Stable';
  }
  trendClass(t: EntityRow['trend']): string {
    const m: Record<EntityRow['trend'], string> = {
      up: 'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200',
      down: 'bg-red-50 text-red-600 ring-1 ring-red-200',
      stable: 'bg-gray-100 text-gray-600 ring-1 ring-gray-200',
    };
    return m[t];
  }
  typeClass(t: EntityRow['type']): string {
    const m: Record<EntityRow['type'], string> = {
      PLACE: 'bg-violet-100 text-violet-700', DESTINATION: 'bg-blue-100 text-blue-700',
      EVENT: 'bg-emerald-100 text-emerald-700', LOCATION: 'bg-amber-100 text-amber-700',
    };
    return m[t];
  }
  typeLabel(t: EntityRow['type']): string {
    const m: Record<EntityRow['type'], string> = { PLACE: 'Place', DESTINATION: 'Destination', EVENT: 'Event', LOCATION: 'Location' };
    return m[t];
  }
  reviewMinClass(v: number): string {
    if (v < 2.5) return 'text-red-600 font-semibold';
    if (v < 3.5) return 'text-amber-600';
    return 'text-gray-500';
  }
  reviewAvgClass(v: number): string {
    if (v >= 4.5) return 'text-emerald-700 font-semibold';
    if (v >= 3.5) return 'text-gray-800 font-semibold';
    return 'text-red-600 font-semibold';
  }

  // ── Actionable insights helpers ───────────────────────────────────────
  insightCardBg(highlightClass?: string): string {
    if (highlightClass === 'text-blue-600') return 'bg-blue-50 border-blue-100';
    if (highlightClass === 'text-red-600')  return 'bg-red-50 border-red-100';
    return 'bg-gray-50 border-gray-100';
  }

  // ── Auto-insights helpers ─────────────────────────────────────────────
  insightIconBg(type: AutoInsight['type']): string {
    const m: Record<AutoInsight['type'], string> = { success: '#E1F5EE', warning: '#FAEEDA', danger: '#FCEBEB', info: '#E6F1FB' };
    return m[type];
  }
  insightIconColor(type: AutoInsight['type']): string {
    const m: Record<AutoInsight['type'], string> = { success: '#0F6E56', warning: '#854F0B', danger: '#A32D2D', info: '#185FA5' };
    return m[type];
  }
  insightIcon(type: AutoInsight['type']): string {
    const m: Record<AutoInsight['type'], string> = { success: '↑', warning: '❄', danger: '⚠', info: '★' };
    return m[type];
  }

  getEntityImage(name: string): string {
    const m: Record<string, string> = {
      'Oplenac Complex': 'https://source.unsplash.com/100x100/?church',
      'Kosmaj Monument': 'https://source.unsplash.com/100x100/?monument',
      'Arandjelovac Spa Hotel': 'https://source.unsplash.com/100x100/?spa',
      'Šumadija Wine Route': 'https://source.unsplash.com/100x100/?vineyard',
    };
    return m[name] || 'https://source.unsplash.com/100x100/?nature';
  }
}
