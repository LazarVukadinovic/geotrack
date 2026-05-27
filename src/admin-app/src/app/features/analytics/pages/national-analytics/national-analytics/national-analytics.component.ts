import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  HostListener,
  OnDestroy,
  OnInit,
  ViewChild,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subject, finalize, takeUntil } from 'rxjs';
import { SidebarComponent } from '../../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../../shared/components/topbar/topbar.component';
import {
  AnalyticsTooltipContent,
  AnalyticsTooltipDirective,
} from '../../../../../shared/directives/analytics-tooltip.directive';
import { AuthService } from '../../../../../core/services/auth.service';
import { TerritoryDashboardService } from '../../../../../core/services/territory-dashboard.service';
import { NationalAnalyticsService } from '../../../../../core/services/national/national-analytics.service';
import {
  TerritoryAnalyticsContentQuality,
  TerritoryAnalyticsEntityType,
  TerritoryAnalyticsMetricCard,
  TerritoryAnalyticsOverview,
  TerritoryAnalyticsPeriod,
  TerritoryAnalyticsRegionalPerformanceItem,
  TerritoryAnalyticsStatus,
  TerritoryAnalyticsTopContent,
  TerritoryAnalyticsTopEntity,
  TerritoryAnalyticsTopEvent,
} from '../../../../../core/models/national-analytics.model';
import { RegionalAssignedRegion, TerritoryAssignedTerritory } from '../../../../../core/models/dashboard.model';

interface RegionOption {
  id: number;
  name: string;
}

interface ChartLabel {
  x: number;
  text: string;
}

interface PageViewsChartPoint {
  index: number;
  x: number;
  y: number;
  value: number;
  label: string;
  seriesLabel: string;
  color: string;
}

interface PageViewsHoverTarget {
  index: number;
  x: number;
  hoverX: number;
  hoverWidth: number;
  label: string;
  rows: { label: string; value: string }[];
  points: { x: number; y: number; color: string }[];
}

interface ChartSeries {
  regionId: number;
  label: string;
  color: string;
  linePath: string;
  areaPath: string;
  points: PageViewsChartPoint[];
  lastPt: PageViewsChartPoint | null;
}

@Component({
  selector: 'app-national-analytics',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent, AnalyticsTooltipDirective],
  templateUrl: './national-analytics.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NationalAnalyticsComponent implements OnInit, OnDestroy {
  private static readonly pageViewsChartWidth = 680;
  private static readonly pageViewsPlotLeft = 40;
  private static readonly pageViewsPlotRight = 660;
  private static readonly pageViewsPlotTop = 20;
  private static readonly pageViewsPlotBottom = 140;
  private static readonly pageViewsPlotHeight = 120;

  private readonly destroy$ = new Subject<void>();
  protected readonly Math = Math;

  private pageViewsChartFrame: ElementRef<HTMLElement> | null = null;
  private pageViewsResizeObserver: ResizeObserver | null = null;
  private pageViewsTouchClearHandle: ReturnType<typeof setTimeout> | null = null;
  private pageViewsChartPixelWidth = NationalAnalyticsComponent.pageViewsChartWidth;
  private activePageViewsPointIndex: number | null = null;

  @ViewChild('pageViewsChartFrame')
  set pageViewsChartFrameRef(ref: ElementRef<HTMLElement> | undefined) {
    this.pageViewsChartFrame = ref ?? null;
    this.observePageViewsChartFrame();
  }

  readonly periodOptions: { label: string; value: TerritoryAnalyticsPeriod }[] = [
    { label: 'Last 24 Hours', value: '24h' },
    { label: 'Last 7 Days', value: '7d' },
    { label: 'Last 1 Month', value: '1m' },
    { label: 'Last 3 Months', value: '3m' },
    { label: 'Last 6 Months', value: '6m' },
    { label: 'Last 1 Year', value: '1y' },
  ];
  readonly statusOptions: { label: string; value: TerritoryAnalyticsStatus }[] = [
    { label: 'Active', value: 'active' },
    { label: 'Inactive', value: 'inactive' },
    { label: 'All', value: 'all' },
  ];
  readonly tableSortOptions = [
    { label: 'Most Page Views', value: 'views_desc' },
    { label: 'Most Visitors', value: 'visitors_desc' },
    { label: 'Most Saves', value: 'saves_desc' },
    { label: 'Best Rating', value: 'rating_desc' },
    { label: 'Best Health', value: 'health_desc' },
    { label: 'A-Z', value: 'name_asc' },
    { label: 'Z-A', value: 'name_desc' },
  ];

  selectedPeriod: TerritoryAnalyticsPeriod = '1m';
  selectedStatus: TerritoryAnalyticsStatus = 'active';
  selectedRegionIds: number[] = [];
  selectedSort = 'views_desc';
  tableTake = 5;

  isLoading = false;
  isExporting = false;
  regionDropdownOpen = false;
  tableSortOpen = false;
  errorMessage = '';

  assignedTerritories: TerritoryAssignedTerritory[] = [];
  sidebarTerritories: RegionalAssignedRegion[] = [];
  activeTerritory: TerritoryAssignedTerritory | null = null;
  availableRegions: RegionOption[] = [];
  overview: TerritoryAnalyticsOverview | null = null;
  metricCards: TerritoryAnalyticsMetricCard[] = [];

  private readonly chartPalette = ['#2563eb', '#7c3aed', '#16a34a', '#d97706', '#dc2626', '#0891b2', '#be185d', '#0f766e'];

  constructor(
    private readonly cdr: ChangeDetectorRef,
    private readonly auth: AuthService,
    private readonly territories: TerritoryDashboardService,
    private readonly analytics: NationalAnalyticsService,
    private readonly router: Router,
  ) {}

  get userName(): string {
    return this.auth.fullName || this.auth.getUser()?.email || 'Territory user';
  }

  get userRole(): string {
    return this.auth.getUser()?.role ?? 'Territory Manager';
  }

  get territoryLabel(): string {
    return this.activeTerritory?.displayName || this.activeTerritory?.name || '';
  }

  get regionDisplayLabel(): string {
    if (this.selectedRegionIds.length === 0) return 'All Regions';
    if (this.selectedRegionIds.length === 1) {
      return this.availableRegions.find(region => region.id === this.selectedRegionIds[0])?.name ?? '1 Region';
    }
    return `${this.selectedRegionIds.length} Regions`;
  }

  get selectedPeriodLabel(): string {
    return this.periodOptions.find(option => option.value === this.selectedPeriod)?.label ?? 'Selected Period';
  }

  get chartSubtitleText(): string {
    return `${this.selectedPeriodLabel} page engagement`;
  }

  get topSectionSuffix(): string {
    return this.selectedRegionIds.length === 0 ? ' Territory-wide' : '';
  }

  get tableTitle(): string {
    return this.selectedRegionIds.length === 1
      ? `${this.regionDisplayLabel} Performance`
      : 'Regional Performance Comparison';
  }

  get contentQualityTitle(): string {
    return this.selectedRegionIds.length === 1
      ? `Content Quality - ${this.regionDisplayLabel}`
      : 'Content Quality by Region';
  }

  get currentTableSortLabel(): string {
    return this.tableSortOptions.find(option => option.value === this.selectedSort)?.label ?? 'Most Page Views';
  }

  get topRegion() {
    return this.overview?.topRegion ?? null;
  }

  get fastestGrowing() {
    return this.overview?.fastestGrowing ?? null;
  }

  get topEntities(): TerritoryAnalyticsTopEntity[] {
    return this.overview?.topEntities ?? [];
  }

  get topDestinations(): TerritoryAnalyticsTopContent[] {
    return this.overview?.topDestinations ?? [];
  }

  get topLocations(): TerritoryAnalyticsTopContent[] {
    return this.overview?.topLocations ?? [];
  }

  get topPlaces(): TerritoryAnalyticsTopContent[] {
    return this.overview?.topPlaces ?? [];
  }

  get topEvents(): TerritoryAnalyticsTopEvent[] {
    return this.overview?.topEvents ?? [];
  }

  get contentQuality(): TerritoryAnalyticsContentQuality[] {
    return this.overview?.contentQualityByRegion ?? [];
  }

  get visibleRows(): TerritoryAnalyticsRegionalPerformanceItem[] {
    return this.overview?.regionalPerformance.items ?? [];
  }

  get tableHasMore(): boolean {
    return this.overview?.regionalPerformance.hasMore ?? false;
  }

  get tableTotalCount(): number {
    return this.overview?.regionalPerformance.totalCount ?? 0;
  }

  get hasAnyData(): boolean {
    const totals = this.overview?.totals;
    return !!totals && (
      totals.totalPageViews > 0 ||
      totals.uniqueVisitors > 0 ||
      totals.externalLinkClicks > 0 ||
      totals.totalSaves > 0 ||
      totals.totalReviews > 0 ||
      totals.averageRating > 0 ||
      this.visibleRows.length > 0
    );
  }

  ngOnInit(): void {
    this.loadAssignedTerritories();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
    this.pageViewsResizeObserver?.disconnect();
    this.clearPageViewsTouchTimer();
  }

  @HostListener('window:resize')
  onWindowResize(): void {
    this.updatePageViewsChartWidth();
  }

  onDocumentClick(): void {
    let changed = false;
    if (this.regionDropdownOpen) {
      this.regionDropdownOpen = false;
      changed = true;
    }
    if (this.tableSortOpen) {
      this.tableSortOpen = false;
      changed = true;
    }
    if (changed) this.cdr.markForCheck();
  }

  onTerritoryChange(territoryId: number): void {
    const territory = this.assignedTerritories.find(item => item.id === territoryId);
    if (!territory) return;

    this.activeTerritory = territory;
    this.territories.setActiveTerritoryId(territory.id);
    this.selectedRegionIds = [];
    this.tableTake = 5;
    this.clearActivePageViewsPoint();
    this.loadRegions(territory.id);
    this.loadOverview();
  }

  onPeriodChange(): void {
    this.tableTake = 5;
    this.clearActivePageViewsPoint();
    this.loadOverview();
  }

  onStatusChange(): void {
    this.tableTake = 5;
    this.clearActivePageViewsPoint();
    this.loadOverview();
  }

  toggleRegionDropdown(): void {
    this.regionDropdownOpen = !this.regionDropdownOpen;
    this.cdr.markForCheck();
  }

  isRegionSelected(regionId: number): boolean {
    return this.selectedRegionIds.includes(regionId);
  }

  toggleRegion(regionId: number): void {
    this.selectedRegionIds = this.selectedRegionIds.includes(regionId)
      ? this.selectedRegionIds.filter(id => id !== regionId)
      : [...this.selectedRegionIds, regionId];
    this.tableTake = 5;
    this.clearActivePageViewsPoint();
    this.loadOverview();
  }

  selectAllRegions(): void {
    this.selectedRegionIds = [];
    this.tableTake = 5;
    this.clearActivePageViewsPoint();
    this.loadOverview();
  }

  toggleTableSort(): void {
    this.tableSortOpen = !this.tableSortOpen;
    this.cdr.markForCheck();
  }

  applyTableSort(sort: string): void {
    this.selectedSort = sort;
    this.tableSortOpen = false;
    this.tableTake = 5;
    this.loadOverview();
  }

  showMoreTable(): void {
    this.tableTake += 5;
    this.loadOverview();
  }

  exportReport(): void {
    if (!this.activeTerritory || this.isExporting) return;

    this.isExporting = true;
    this.errorMessage = '';
    this.analytics.exportCsv(this.currentRequest(false))
      .pipe(
        finalize(() => {
          this.isExporting = false;
          this.cdr.markForCheck();
        }),
        takeUntil(this.destroy$),
      )
      .subscribe({
        next: blob => this.analytics.downloadCsv(blob, this.territoryLabel, this.selectedPeriod),
        error: () => {
          this.errorMessage = 'Could not export the current analytics report.';
          this.cdr.markForCheck();
        },
      });
  }

  navigateToDirectory(): void {
    this.router.navigate(['/territory/content']);
  }

  statusClass(status: string): string {
    if (status.toLowerCase().startsWith('active')) {
      return 'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200';
    }
    return 'bg-gray-100 text-gray-500 ring-1 ring-gray-200';
  }

  scoreColor(score: number): string {
    if (score >= 85) return '#2563eb';
    if (score >= 70) return '#d97706';
    return '#dc2626';
  }

  entityLabel(entityType: string): string {
    switch (entityType) {
      case 'destination': return 'Destination';
      case 'location': return 'Location';
      case 'place': return 'Place';
      case 'event': return 'Event';
      default: return 'Entity';
    }
  }

  entityIcon(entityType: string): TerritoryAnalyticsEntityType {
    return entityType === 'destination' || entityType === 'location' || entityType === 'place' || entityType === 'event'
      ? entityType
      : 'place';
  }

  formatNumber(value: number): string {
    return this.analytics.formatNumber(value);
  }

  barWidth(value: number, items: Array<{ views: number }>): string {
    const max = Math.max(...items.map(item => item.views), 1);
    return `${Math.max(3, Math.round((value / max) * 100))}%`;
  }

  eventMetricValue(event: TerritoryAnalyticsTopEvent): number {
    return event.views || event.saves || 0;
  }

  eventMetricLabel(event: TerritoryAnalyticsTopEvent): string {
    if (event.views > 0) return `${this.formatNumber(event.views)} views`;
    return `${this.formatNumber(event.saves ?? 0)} saves`;
  }

  eventBarWidth(event: TerritoryAnalyticsTopEvent): string {
    const max = Math.max(...this.topEvents.map(item => this.eventMetricValue(item)), 1);
    return `${Math.max(3, Math.round((this.eventMetricValue(event) / max) * 100))}%`;
  }

  setActivePageViewsPoint(index: number): void {
    this.clearPageViewsTouchTimer();
    this.activePageViewsPointIndex = index >= 0 && index < this.pageViewsPointCount() ? index : null;
  }

  setActivePageViewsPointFromTouch(index: number): void {
    this.setActivePageViewsPoint(index);
    this.pageViewsTouchClearHandle = setTimeout(() => this.clearActivePageViewsPoint(index), 3500);
  }

  clearActivePageViewsPoint(index?: number): void {
    if (index === undefined || this.activePageViewsPointIndex === index) {
      this.clearPageViewsTouchTimer();
      this.activePageViewsPointIndex = null;
    }
  }

  pageViewsTooltip(target: PageViewsHoverTarget): AnalyticsTooltipContent {
    return {
      title: target.label,
      rows: target.rows.map(row => ({
        label: row.label,
        value: row.value,
      })),
    };
  }

  pageViewsAriaLabel(target: PageViewsHoverTarget): string {
    const values = target.rows
      .map(row => `${row.label}: ${row.value} page views`)
      .join(', ');
    return `${target.label}: ${values}`;
  }

  get chartYLabels(): { y: number; text: string }[] {
    const max = this.chartMax();
    return [max, Math.round(max * 0.67), Math.round(max * 0.33), 0].map((value, index) => ({
      y: 24 + index * 40,
      text: this.formatNumber(value),
    }));
  }

  get chartXLabels(): ChartLabel[] {
    const labels = this.overview?.pageViewsOverTime.monthLabels ?? [];
    const count = labels.length;
    if (count === 0) return [];

    return this.visibleTickIndexes(count, this.maxXAxisTickCount(count)).map(index => ({
      x: this.pageViewsX(index, count),
      text: labels[index],
    }));
  }

  get computedChartSeries(): ChartSeries[] {
    const labels = this.overview?.pageViewsOverTime.monthLabels ?? [];
    const series = this.overview?.pageViewsOverTime.series ?? [];
    const max = this.chartMax();
    const pointCount = this.pageViewsPointCount();

    return series.map((item, seriesIndex) => {
      const color = this.chartPalette[seriesIndex % this.chartPalette.length];
      const points = item.values.map((rawValue, index) => ({
        index,
        x: this.pageViewsX(index, pointCount),
        y: this.pageViewsY(this.safePageViewValue(rawValue), max),
        value: this.safePageViewValue(rawValue),
        label: labels[index] ?? `Point ${index + 1}`,
        seriesLabel: item.regionName,
        color,
      }));
      const linePath = this.linePath(points);
      const areaPath = points.length >= 2
        ? `${linePath} L${points[points.length - 1].x.toFixed(1)},${NationalAnalyticsComponent.pageViewsPlotBottom} L${points[0].x.toFixed(1)},${NationalAnalyticsComponent.pageViewsPlotBottom} Z`
        : '';

      return {
        regionId: item.regionId,
        label: item.regionName,
        color,
        linePath,
        areaPath,
        points,
        lastPt: points[points.length - 1] ?? null,
      };
    });
  }

  get chartHoverTargets(): PageViewsHoverTarget[] {
    const labels = this.overview?.pageViewsOverTime.monthLabels ?? [];
    const series = this.computedChartSeries;
    const count = this.pageViewsPointCount();
    if (count === 0 || series.length === 0) return [];

    return Array.from({ length: count }, (_, index) => {
      const x = this.pageViewsX(index, count);
      const previousX = index > 0 ? this.pageViewsX(index - 1, count) : NationalAnalyticsComponent.pageViewsPlotLeft;
      const nextX = index < count - 1 ? this.pageViewsX(index + 1, count) : NationalAnalyticsComponent.pageViewsPlotRight;
      const hoverX = index === 0 ? NationalAnalyticsComponent.pageViewsPlotLeft : (previousX + x) / 2;
      const hoverEnd = index === count - 1 ? NationalAnalyticsComponent.pageViewsPlotRight : (x + nextX) / 2;
      const points = series
        .map(item => item.points[index])
        .filter((point): point is PageViewsChartPoint => !!point);

      return {
        index,
        x,
        hoverX: +hoverX.toFixed(1),
        hoverWidth: +(hoverEnd - hoverX).toFixed(1),
        label: labels[index] ?? points[0]?.label ?? `Point ${index + 1}`,
        rows: points.map(point => ({
          label: point.seriesLabel,
          value: this.formatNumber(point.value),
        })),
        points: points.map(point => ({
          x: point.x,
          y: point.y,
          color: point.color,
        })),
      };
    }).filter(target => target.rows.length > 0);
  }

  get activePageViewsPoints(): { x: number; y: number; color: string }[] {
    if (this.activePageViewsPointIndex === null) return [];
    return this.chartHoverTargets.find(target => target.index === this.activePageViewsPointIndex)?.points ?? [];
  }

  get contentQualityChartData(): {
    bars: { x: number; y: number; height: number; label: string; value: number }[];
    dots: { x: number; y: number; rating: number }[];
    labels: ChartLabel[];
    polyline: string;
    yLabels: { y: number; text: string }[];
    isEmpty: boolean;
  } {
    const data = this.contentQuality;
    if (data.length === 0) {
      return { bars: [], dots: [], labels: [], polyline: '', yLabels: [], isEmpty: true };
    }

    const chartW = 570;
    const step = chartW / data.length;
    const maxReviews = Math.max(...data.map(item => item.totalReviews), 1);

    const bars = data.map((item, index) => {
      const cx = 60 + step * index + step / 2;
      const barH = (item.totalReviews / maxReviews) * 120;
      return { x: cx - 20, y: 130 - barH, height: barH, label: item.regionName, value: item.totalReviews };
    });
    const dots = data.map((item, index) => {
      const cx = 60 + step * index + step / 2;
      const y = 130 - (Math.max(0, Math.min(5, item.averageRating)) / 5) * 120;
      return { x: cx, y, rating: item.averageRating };
    });
    const labels = data.map((item, index) => ({
      x: 60 + step * index + step / 2,
      text: item.regionName,
    }));
    const yLabels = [maxReviews, Math.round(maxReviews * 0.67), Math.round(maxReviews * 0.33), 0].map((value, index) => ({
      y: 14 + index * 40,
      text: this.formatNumber(value),
    }));

    return {
      bars,
      dots,
      labels,
      polyline: dots.map(dot => `${dot.x.toFixed(1)},${dot.y.toFixed(1)}`).join(' '),
      yLabels,
      isEmpty: false,
    };
  }

  private loadAssignedTerritories(): void {
    this.isLoading = true;
    this.errorMessage = '';
    this.territories.getAssignedTerritories()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: territories => {
          this.assignedTerritories = territories;
          this.sidebarTerritories = territories.map(territory => ({
            id: territory.id,
            name: territory.name,
            displayName: territory.displayName,
            statusLabel: territory.statusLabel,
          }));
          const storedId = this.territories.getActiveTerritoryId();
          this.activeTerritory = storedId
            ? territories.find(territory => territory.id === storedId) ?? territories[0] ?? null
            : territories[0] ?? null;
          if (this.activeTerritory) {
            this.territories.setActiveTerritoryId(this.activeTerritory.id);
            this.loadRegions(this.activeTerritory.id);
            this.loadOverview();
          } else {
            this.isLoading = false;
            this.overview = null;
          }
          this.cdr.markForCheck();
        },
        error: () => {
          this.isLoading = false;
          this.errorMessage = 'Could not load assigned territory context.';
          this.cdr.markForCheck();
        },
      });
  }

  private loadRegions(territoryId: number): void {
    this.territories.getRegions({ territoryId, skip: 0, take: 100, sort: 'name' })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: response => {
          this.availableRegions = response.items.map(item => ({
            id: item.regionId,
            name: item.displayName || item.regionName || item.name,
          }));
          this.cdr.markForCheck();
        },
        error: () => {
          this.availableRegions = [];
          this.cdr.markForCheck();
        },
      });
  }

  private loadOverview(): void {
    if (!this.activeTerritory) {
      this.isLoading = false;
      this.cdr.markForCheck();
      return;
    }

    this.isLoading = true;
    this.errorMessage = '';
    this.analytics.getOverview(this.currentRequest(true))
      .pipe(
        finalize(() => {
          this.isLoading = false;
          this.cdr.markForCheck();
        }),
        takeUntil(this.destroy$),
      )
      .subscribe({
        next: overview => {
          this.overview = overview;
          this.metricCards = this.analytics.buildMetricCards(overview);
          if (overview.activeTerritory) {
            this.activeTerritory = overview.activeTerritory;
          }
        },
        error: () => {
          this.overview = null;
          this.metricCards = [];
          this.errorMessage = 'Could not load territory analytics for the selected filters.';
        },
      });
  }

  private currentRequest(includePaging: boolean) {
    return {
      territoryId: this.activeTerritory?.id ?? null,
      period: this.selectedPeriod,
      regionIds: this.selectedRegionIds,
      status: this.selectedStatus,
      skip: 0,
      take: includePaging ? this.tableTake : undefined,
      sort: this.selectedSort,
    };
  }

  private chartMax(): number {
    const values = this.overview?.pageViewsOverTime.series
      .flatMap(series => series.values.map(value => this.safePageViewValue(value))) ?? [];
    return Math.max(1, ...values);
  }

  private linePath(points: { x: number; y: number }[]): string {
    if (points.length === 0) return '';
    if (points.length === 1) return `M${points[0].x.toFixed(1)},${points[0].y.toFixed(1)}`;

    return points
      .map((point, index) => `${index === 0 ? 'M' : 'L'}${point.x.toFixed(1)},${point.y.toFixed(1)}`)
      .join(' ');
  }

  private pageViewsPointCount(): number {
    const labels = this.overview?.pageViewsOverTime.monthLabels.length ?? 0;
    const seriesCounts = this.overview?.pageViewsOverTime.series.map(series => series.values.length) ?? [];
    return Math.max(labels, ...seriesCounts, 0);
  }

  private maxXAxisTickCount(labelCount: number): number {
    if (labelCount <= 2) return labelCount;

    const width = Math.min(
      Math.max(this.pageViewsChartPixelWidth, 280),
      NationalAnalyticsComponent.pageViewsChartWidth,
    );
    const availableWidth = Math.max(180, width - 60);
    const minLabelSpacing = this.selectedPeriod === '24h'
      ? 56
      : this.selectedPeriod === '7d'
        ? 64
        : 86;
    const widthBasedMax = Math.max(2, Math.floor(availableWidth / minLabelSpacing) + 1);
    const periodCap = this.selectedPeriod === '7d' ? 7 : 8;

    return Math.min(labelCount, widthBasedMax, periodCap);
  }

  private visibleTickIndexes(count: number, maxTicks: number): number[] {
    if (count <= 0) return [];
    if (maxTicks <= 1) return [0];
    if (count <= maxTicks) return Array.from({ length: count }, (_, index) => index);

    const indexes = new Set<number>([0, count - 1]);
    for (let tick = 1; tick < maxTicks - 1; tick += 1) {
      indexes.add(Math.round((tick * (count - 1)) / (maxTicks - 1)));
    }

    return [...indexes].sort((a, b) => a - b);
  }

  private pageViewsX(index: number, count: number): number {
    if (count <= 1) return 350;

    const plotWidth = NationalAnalyticsComponent.pageViewsPlotRight - NationalAnalyticsComponent.pageViewsPlotLeft;
    return +(
      NationalAnalyticsComponent.pageViewsPlotLeft +
      (index / (count - 1)) * plotWidth
    ).toFixed(1);
  }

  private pageViewsY(value: number, max: number): number {
    const ratio = max > 0 ? this.safePageViewValue(value) / max : 0;
    return this.clamp(
      NationalAnalyticsComponent.pageViewsPlotBottom - ratio * NationalAnalyticsComponent.pageViewsPlotHeight,
      NationalAnalyticsComponent.pageViewsPlotTop,
      NationalAnalyticsComponent.pageViewsPlotBottom,
    );
  }

  private safePageViewValue(value: number): number {
    return Number.isFinite(value) ? Math.max(0, value) : 0;
  }

  private observePageViewsChartFrame(): void {
    this.pageViewsResizeObserver?.disconnect();
    this.pageViewsResizeObserver = null;

    if (!this.pageViewsChartFrame) return;

    this.updatePageViewsChartWidth();
    if (typeof ResizeObserver === 'undefined') return;

    this.pageViewsResizeObserver = new ResizeObserver(entries => {
      this.setPageViewsChartWidth(entries[0]?.contentRect.width ?? 0);
    });
    this.pageViewsResizeObserver.observe(this.pageViewsChartFrame.nativeElement);
  }

  private updatePageViewsChartWidth(): void {
    const width = this.pageViewsChartFrame?.nativeElement.getBoundingClientRect().width ?? 0;
    this.setPageViewsChartWidth(width);
  }

  private setPageViewsChartWidth(width: number): void {
    if (!Number.isFinite(width) || width <= 0) return;

    const nextWidth = Math.round(width);
    if (Math.abs(nextWidth - this.pageViewsChartPixelWidth) < 1) return;

    this.pageViewsChartPixelWidth = nextWidth;
    this.cdr.markForCheck();
  }

  private clearPageViewsTouchTimer(): void {
    if (!this.pageViewsTouchClearHandle) return;

    clearTimeout(this.pageViewsTouchClearHandle);
    this.pageViewsTouchClearHandle = null;
  }

  private clamp(value: number, min: number, max: number): number {
    return +Math.min(max, Math.max(min, value)).toFixed(1);
  }
}
