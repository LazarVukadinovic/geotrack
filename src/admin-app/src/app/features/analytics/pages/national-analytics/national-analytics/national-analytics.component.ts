import { Component, OnInit, OnDestroy, ChangeDetectionStrategy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { SidebarComponent } from '../../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../../shared/components/topbar/topbar.component';
import { AuthService } from '../../../../../core/services/auth.service';
import { NationalAnalyticsService } from '../../../../../core/services/national/national-analytics.service';
import { NationalKpi, TopRegionHighlight, FastestGrowingRegion, TopEntityItem, PageViewSeries, TopDestination, TopEvent, TopPlace, TopLocation, ContentQualityPoint, RegionAnalyticRow, RegionStatus, OperationalAlert } from '../../../../../core/models/national-analytics.model';

@Component({
  selector: 'app-national-analytics',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './national-analytics.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NationalAnalyticsComponent implements OnInit, OnDestroy {

  private readonly destroy$ = new Subject<void>();

  protected Math = Math;

  constructor(
    private readonly cdr:       ChangeDetectorRef,
    private readonly auth:      AuthService,
    private readonly analytics: NationalAnalyticsService,
  ) {}

  // ── Auth ─────────────────────────────────────────────────────────────────
  get userName(): string { return this.auth.fullName ?? 'Sarah Jenkins'; }
  get userRole():  string { return this.auth.getUser()?.role ?? 'National Manager'; }
  get region():    string { return this.auth.getUser()?.region ?? 'Republika Srbija'; }

  // ── Loading state ────────────────────────────────────────────────────────
  isLoading = true;

  // ── Filters ──────────────────────────────────────────────────────────────
  selectedPeriod = 'Last 6 Months';
  selectedEntity = 'All';
  selectedStatus = 'Active';

  customDateFrom      = '';
  customDateTo        = '';
  showCustomDateModal = false;
  private previousPeriod = 'Last 6 Months';

  fromDay   = '';
  fromMonth = '';
  fromYear  = '';
  toDay     = '';
  toMonth   = '';
  toYear    = '';

  get canApply(): boolean {
    return this.fromDay.length === 2 && this.fromMonth.length === 2 && this.fromYear.length === 4
        && this.toDay.length === 2 && this.toMonth.length === 2 && this.toYear.length === 4;
  }

  get customDays(): number {
    if (!this.customDateFrom || !this.customDateTo) return 30;
    const [d1, m1, y1] = this.customDateFrom.split('.').map(Number);
    const [d2, m2, y2] = this.customDateTo.split('.').map(Number);
    const from = new Date(y1, m1 - 1, d1);
    const to   = new Date(y2, m2 - 1, d2);
    return Math.max(1, Math.round((to.getTime() - from.getTime()) / 86_400_000) + 1);
  }

  readonly periodOptions = ['Last 7 Days', 'Last 30 Days', 'Last 6 Months', 'Last 12 Months', 'Custom'];
  readonly entityOptions = ['All', 'Destinations', 'Locations', 'Places', 'Events'];
  readonly statusOptions = ['Active', 'Inactive', 'All'];

  // ── Multi-region selection ────────────────────────────────────────────────
  regionNames: string[] = [];          // populated from data
  selectedRegions: string[] = [];      // empty = All Regions
  regionDropdownOpen = false;

  get isAllRegions(): boolean { return this.selectedRegions.length === 0; }

  get regionDisplayLabel(): string {
    if (this.isAllRegions) return 'All Regions';
    if (this.selectedRegions.length === 1) return this.selectedRegions[0];
    return `${this.selectedRegions.length} Regions`;
  }

  isRegionSelected(name: string): boolean {
    return this.selectedRegions.includes(name);
  }

  toggleRegionDropdown(): void {
    this.regionDropdownOpen = !this.regionDropdownOpen;
    this.cdr.markForCheck();
  }

  toggleRegion(name: string): void {
    if (this.selectedRegions.includes(name)) {
      this.selectedRegions = this.selectedRegions.filter(r => r !== name);
    } else {
      this.selectedRegions = [...this.selectedRegions, name];
    }
    this.tableVisibleCount = 5;
    this.cdr.markForCheck();
  }

  selectAllRegions(): void {
    this.selectedRegions = [];
    this.tableVisibleCount = 5;
    this.cdr.markForCheck();
  }

  // ── Dynamic titles ────────────────────────────────────────────────────────
  get topSectionSuffix(): string {
    if (this.isAllRegions) return ' Nationwide';
    if (this.selectedRegions.length === 1) return ` – ${this.selectedRegions[0]}`;
    return '';
  }

  get tableTitle(): string {
    if (this.isAllRegions) return 'Regional Performance Comparison';
    if (this.selectedRegions.length === 1) return `${this.selectedRegions[0]} Performance`;
    return 'Regional Performance Comparison';
  }

  get contentQualityTitle(): string {
    if (this.isAllRegions) return 'Content Quality by Region';
    if (this.selectedRegions.length === 1) return `Content Quality – ${this.selectedRegions[0]}`;
    return 'Content Quality – Selected Regions';
  }

  // ── Filtered data ─────────────────────────────────────────────────────────
  get filteredRegionRows(): RegionAnalyticRow[] {
    if (this.isAllRegions) return this.allRows;
    return this.allRows.filter(r => this.selectedRegions.includes(r.name));
  }

  get filteredTopDestinations(): TopDestination[] {
    if (this.isAllRegions) return this.topDestinations;
    return this.topDestinations.filter(d => this.selectedRegions.includes(d.region));
  }

  get filteredTopEvents(): TopEvent[] {
    if (this.isAllRegions) return this.topEvents;
    return this.topEvents.filter(e => this.selectedRegions.includes(e.region));
  }

  get filteredTopPlaces(): TopPlace[] {
    if (this.isAllRegions) return this.topPlaces;
    return this.topPlaces.filter(p => this.selectedRegions.includes(p.region));
  }

  get filteredTopLocations(): TopLocation[] {
    if (this.isAllRegions) return this.topLocations;
    return this.topLocations.filter(l => this.selectedRegions.includes(l.region));
  }

  get filteredContentQuality(): ContentQualityPoint[] {
    if (this.isAllRegions) return this.contentQuality;
    return this.contentQuality.filter(q => this.selectedRegions.includes(q.region));
  }

  // ── Dynamic KPI cards ────────────────────────────────────────────────────
  get dynamicKpis(): NationalKpi[] {
    const rows = this.filteredRegionRows;
    if (this.isAllRegions || rows.length === 0) return this.kpis;

    const pv  = rows.reduce((s, r) => s + this.parseFormattedValue(r.pageViews), 0);
    const vis = rows.reduce((s, r) => s + this.parseFormattedValue(r.visitors),  0);
    const sv  = rows.reduce((s, r) => s + this.parseFormattedValue(r.saves),     0);
    const ec  = rows.reduce((s, r) => s + this.parseFormattedValue(r.extClicks), 0);
    const rv  = rows.reduce((s, r) => s + this.parseFormattedValue(r.totalReviews), 0);
    const avg = rows.reduce((s, r) => s + r.avgRating, 0) / rows.length;

    const fmt = (n: number) =>
      n >= 1_000_000 ? (n / 1_000_000).toFixed(1) + 'M' :
      n >= 1_000     ? (n / 1_000).toFixed(1) + 'K'      : String(Math.round(n));

    return [
      { label: 'Total Page Views',        value: fmt(pv),  change: '+12.5%', up: true, icon: 'eye'      },
      { label: 'Unique Visitors',         value: fmt(vis), change: '+8.2%',  up: true, icon: 'users'    },
      { label: 'External Link Clicks',    value: fmt(ec),  change: '+15.3%', up: true, icon: 'link'     },
      { label: 'Total Saves / Favorites', value: fmt(sv),  change: '+5.1%',  up: true, icon: 'bookmark' },
      { label: 'Total Reviews',           value: fmt(rv),  change: '0%',     up: true, icon: 'chat'     },
      { label: 'Average Rating',          value: avg.toFixed(1) + '/5', change: '+0.2', up: true, icon: 'star' },
    ];
  }

  // ── Data from service ────────────────────────────────────────────────────
  kpis:            NationalKpi[]         = [];
  topRegion:       TopRegionHighlight    = { name: '', score: 0, note: '' };
  fastestGrowing:  FastestGrowingRegion  = { name: '', change: '' };
  topEntities:     TopEntityItem[]       = [];
  pageViewsData:   PageViewSeries[]      = [];
  topDestinations: TopDestination[]      = [];
  topEvents:       TopEvent[]            = [];
  topPlaces:       TopPlace[]            = [];
  topLocations:    TopLocation[]         = [];
  contentQuality:  ContentQualityPoint[] = [];
  allRows:         RegionAnalyticRow[]   = [];
  alerts:          OperationalAlert[]    = [];

  // ── Table: sort ──────────────────────────────────────────────────────────
  tableSortOpen  = false;
  tableSortField = 'pageViews';
  tableSortDir: 'asc' | 'desc' = 'desc';

  readonly tableSortOptions: { label: string; field: string; dir: 'asc' | 'desc' }[] = [
    { label: 'Most Page Views',  field: 'pageViews',    dir: 'desc' },
    { label: 'Most Visitors',    field: 'visitors',     dir: 'desc' },
    { label: 'Most Saves',       field: 'saves',        dir: 'desc' },
    { label: 'Best Rating',      field: 'avgRating',    dir: 'desc' },
    { label: 'Best Quality',     field: 'qualityScore', dir: 'desc' },
    { label: 'A–Z',              field: 'name',         dir: 'asc'  },
    { label: 'Z–A',              field: 'name',         dir: 'desc' },
  ];

  get currentTableSortLabel(): string {
    return this.tableSortOptions.find(o => o.field === this.tableSortField && o.dir === this.tableSortDir)?.label ?? 'Most Page Views';
  }

  parseFormattedValue(s: string): number {
    if (typeof s !== 'string') return s as unknown as number;
    if (s.endsWith('M')) return parseFloat(s) * 1_000_000;
    if (s.endsWith('K')) return parseFloat(s) * 1_000;
    return parseFloat(s) || 0;
  }

  parseEntities(s: string): [number, number, number, number] {
    const parts = s.split('/').map(p => parseInt(p.trim(), 10));
    return [parts[0] ?? 0, parts[1] ?? 0, parts[2] ?? 0, parts[3] ?? 0];
  }

  get sortedAllRows(): RegionAnalyticRow[] {
    const field = this.tableSortField;
    const mult  = this.tableSortDir === 'desc' ? -1 : 1;
    return [...this.filteredRegionRows].sort((a, b) => {
      const av = (a as unknown as Record<string, unknown>)[field];
      const bv = (b as unknown as Record<string, unknown>)[field];
      if (typeof av === 'number' && typeof bv === 'number') return (av - bv) * mult;
      if (typeof av === 'boolean' && typeof bv === 'boolean') return (Number(av) - Number(bv)) * mult;
      if (typeof av === 'string' && typeof bv === 'string') {
        const an = this.parseFormattedValue(av);
        const bn = this.parseFormattedValue(bv);
        if (!isNaN(an) && !isNaN(bn) && an !== 0 && bn !== 0) return (an - bn) * mult;
        return av.localeCompare(bv) * mult;
      }
      return 0;
    });
  }

  // ── Table: show more ─────────────────────────────────────────────────────
  tableVisibleCount = 5;

  get visibleRows(): RegionAnalyticRow[] {
    return this.sortedAllRows.slice(0, this.tableVisibleCount);
  }

  get tableHasMore(): boolean {
    return this.tableVisibleCount < this.sortedAllRows.length;
  }

  showMoreTable(): void {
    this.tableVisibleCount += 5;
    this.cdr.markForCheck();
  }

  applyTableSort(opt: { field: string; dir: 'asc' | 'desc' }): void {
    this.tableSortField = opt.field;
    this.tableSortDir   = opt.dir;
    this.tableSortOpen  = false;
    this.cdr.markForCheck();
  }

  toggleTableSort(): void {
    this.tableSortOpen = !this.tableSortOpen;
    this.cdr.markForCheck();
  }

  onDocumentClick(): void {
    let changed = false;
    if (this.tableSortOpen)    { this.tableSortOpen    = false; changed = true; }
    if (this.regionDropdownOpen) { this.regionDropdownOpen = false; changed = true; }
    if (changed) this.cdr.markForCheck();
  }

  // ── Chart computation ────────────────────────────────────────────────────
  periodToMonths(): number {
    switch (this.selectedPeriod) {
      case 'Last 7 Days':    return 1;
      case 'Last 30 Days':   return 2;
      case 'Last 6 Months':  return 6;
      case 'Last 12 Months': return 12;
      case 'Custom':         return Math.max(1, Math.min(12, Math.ceil(this.customDays / 30)));
      default: return 6;
    }
  }

  get chartSubtitleText(): string {
    switch (this.selectedPeriod) {
      case 'Last 7 Days':    return 'Last 7 days – view engagement';
      case 'Last 30 Days':   return 'Last 30 days – view engagement';
      case 'Last 6 Months':  return 'Monthly engagement – last 6 months';
      case 'Last 12 Months': return 'Monthly engagement – last 12 months';
      case 'Custom': {
        const d = this.customDays;
        return `Custom period – ${d} day${d !== 1 ? 's' : ''}`;
      }
      default: return 'Monthly engagement';
    }
  }

  get chartXLabels(): { x: number; text: string }[] {
    const n    = this.periodToMonths();
    const abbr = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];
    const now  = new Date();
    return Array.from({ length: n }, (_, i) => {
      const monthIdx = ((now.getMonth() - (n - 1 - i)) % 12 + 12) % 12;
      const x = n > 1 ? 40 + (i / (n - 1)) * 620 : 350;
      return { x, text: abbr[monthIdx] };
    });
  }

  get computedChartSeries(): { label: string; color: string; linePath: string; areaPath: string; lastPt: { x: number; y: number } | null }[] {
    const all    = this.pageViewsData;
    const series = this.isAllRegions
      ? all
      : all.filter(s => this.selectedRegions.includes(s.label));

    const n = this.periodToMonths();

    return series.map(s => {
      const raw = s.points.slice(-n);
      const cnt = raw.length;
      const pts = raw.map((v, i) => ({
        x: cnt > 1 ? 40 + (i / (cnt - 1)) * 620 : 350,
        y: 140 - (v / 100) * 120,
      }));
      const line = this.smoothPath(pts);
      const area = pts.length >= 2
        ? `${line} L${pts[cnt - 1].x.toFixed(1)},140 L${pts[0].x.toFixed(1)},140 Z`
        : '';
      return { label: s.label, color: s.color, linePath: line, areaPath: area, lastPt: pts[cnt - 1] ?? null };
    });
  }

  private smoothPath(pts: { x: number; y: number }[]): string {
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

  // ── Content Quality chart data ────────────────────────────────────────────
  get contentQualityChartData(): {
    bars:     { x: number; y: number; height: number }[];
    dots:     { x: number; y: number }[];
    labels:   { x: number; text: string }[];
    polyline: string;
    isEmpty:  boolean;
  } {
    const data = this.filteredContentQuality;
    const n    = data.length;
    if (n === 0) return { bars: [], dots: [], labels: [], polyline: '', isEmpty: true };

    const chartW    = 570; // x=60 to x=630
    const step      = chartW / n;
    const maxReviews = Math.max(...data.map(d => d.reviews), 600);

    const bars = data.map((d, i) => {
      const cx     = 60 + step * i + step / 2;
      const barH   = (d.reviews / maxReviews) * 120;
      return { x: cx - 20, y: 130 - barH, height: barH };
    });

    const dots = data.map((d, i) => {
      const cx = 60 + step * i + step / 2;
      const y  = 10 + (5.0 - d.avgRating) / 2.0 * 120;
      return { x: cx, y };
    });

    const labels = data.map((d, i) => {
      const cx = 60 + step * i + step / 2;
      return { x: cx, text: d.region };
    });

    const polyline = dots.map(d => `${d.x.toFixed(1)},${d.y.toFixed(1)}`).join(' ');

    return { bars, dots, labels, polyline, isEmpty: false };
  }

  // ── Lifecycle ────────────────────────────────────────────────────────────
  ngOnInit(): void {
    this.analytics.getData$()
      .pipe(takeUntil(this.destroy$))
      .subscribe(data => {
        this.kpis            = data.kpis;
        this.topRegion       = data.topRegion;
        this.fastestGrowing  = data.fastestGrowing;
        this.topEntities     = data.topEntities;
        this.pageViewsData   = data.pageViewsData;
        this.topDestinations = data.topDestinations;
        this.topEvents       = data.topEvents;
        this.topPlaces       = data.topPlaces;
        this.topLocations    = data.topLocations;
        this.contentQuality  = data.contentQuality;
        this.allRows         = data.regionRows;
        this.alerts          = data.alerts;
        this.isLoading       = false;
        this.regionNames     = data.regionRows.map(r => r.name);
        this.cdr.markForCheck();
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  // ── Helpers ──────────────────────────────────────────────────────────────
  statusClass(status: RegionStatus): string {
    if (status.startsWith('Active'))    return 'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200';
    if (status === 'Processing Delays') return 'bg-red-50 text-red-600 ring-1 ring-red-200';
    return 'bg-gray-100 text-gray-500 ring-1 ring-gray-200';
  }

  scoreColor(score: number): string {
    if (score >= 85) return '#2563eb';
    if (score >= 70) return '#d97706';
    return '#dc2626';
  }

  alertIcon(type: OperationalAlert['type']): string {
    if (type === 'no-manager')       return '#dc2626';
    if (type === 'processing-delay') return '#d97706';
    return '#d97706';
  }

  onPeriodChange(value: string): void {
    if (value === 'Custom') {
      if (this.customDateFrom) {
        const p = this.customDateFrom.split('.');
        this.fromDay = p[0] ?? ''; this.fromMonth = p[1] ?? ''; this.fromYear = p[2] ?? '';
      }
      if (this.customDateTo) {
        const p = this.customDateTo.split('.');
        this.toDay = p[0] ?? ''; this.toMonth = p[1] ?? ''; this.toYear = p[2] ?? '';
      }
      this.showCustomDateModal = true;
    } else {
      this.previousPeriod = value;
      this.customDateFrom = '';
      this.customDateTo   = '';
      this.fromDay = this.fromMonth = this.fromYear = '';
      this.toDay   = this.toMonth   = this.toYear   = '';
    }
    this.cdr.markForCheck();
  }

  applyCustomRange(): void {
    if (!this.canApply) return;
    this.customDateFrom      = `${this.fromDay}.${this.fromMonth}.${this.fromYear}`;
    this.customDateTo        = `${this.toDay}.${this.toMonth}.${this.toYear}`;
    this.showCustomDateModal = false;
    this.previousPeriod      = 'Custom';
    this.cdr.markForCheck();
  }

  cancelCustomRange(): void {
    this.selectedPeriod      = this.previousPeriod;
    this.showCustomDateModal = false;
    this.cdr.markForCheck();
  }

  onPartInput(event: Event, nextEl: HTMLInputElement): void {
    const input = event.target as HTMLInputElement;
    if (input.value.length >= input.maxLength) { nextEl.focus(); nextEl.select(); }
  }

  exportReport(): void {}
}
