import {
  ChangeDetectorRef,
  Component,
  HostListener,
  OnDestroy,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { finalize, Subscription } from 'rxjs';
import { SidebarComponent } from '../../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../../shared/components/topbar/topbar.component';
import { AuthService } from '../../../../../core/services/auth.service';
import { DashboardService } from '../../../../../core/services/dashboard.service';
import { RegionalAnalyticsService } from '../../../../../core/services/regional-analytics';
import { RegionalAssignedRegion } from '../../../../../core/models/dashboard.model';
import {
  ActionableInsight,
  AutoInsight,
  CategoryFilter,
  DistributionSlice,
  EntityRow,
  EntitySortField,
  MetricCard,
  RegionalAnalyticsEngagementFunnel,
  RegionalAnalyticsEntityType,
  RegionalAnalyticsPagination,
  RegionalMonthlyVisits,
  RegionalMonthlyVisitYearsBack,
  RegionalVisitorBehaviorAnalytics,
} from '../../../../../core/models/regional-analytics.model';

export type { MetricCard, EntityRow, AutoInsight, ActionableInsight, CategoryFilter };

interface MonthlyVisitChart {
  bars: MonthlyVisitChartBar[];
  legend: { label: string; color: string; total: number }[];
  yLabels: { y: number; text: string; value: number }[];
  xLabels: { x: number; text: string }[];
  isEmpty: boolean;
  totalVisits: number;
}

interface MonthlyVisitChartBar {
  x: number;
  y: number;
  width: number;
  height: number;
  color: string;
  value: number;
  monthLabel: string;
  yearLabel: string;
}

@Component({
  selector: 'app-analytics-page',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './analytics-page.component.html',
})
export class AnalyticsPageComponent implements OnInit, OnDestroy {
  private static readonly activeRegionStorageKey = 'geotrack_admin_active_region_id';
  private static readonly searchDebounceMs = 300;
  private static readonly initialTake = 25;

  private readonly analyticsService = inject(RegionalAnalyticsService);
  private readonly dashboardService = inject(DashboardService);
  private readonly auth = inject(AuthService);
  private readonly cdr = inject(ChangeDetectorRef);

  protected Math = Math;

  assignedRegions: RegionalAssignedRegion[] = [];
  activeRegionId: number | null = null;
  pendingRequestsCount: number | null = null;
  regionLoading = false;
  analyticsLoading = false;
  monthlyVisitsLoading = false;
  exporting = false;
  errorMessage: string | null = null;
  monthlyVisitsErrorMessage: string | null = null;
  authRequired = false;

  private assignedRegionsSubscription: Subscription | null = null;
  private sidebarSummarySubscription: Subscription | null = null;
  private analyticsSubscription: Subscription | null = null;
  private monthlyVisitsSubscription: Subscription | null = null;
  private exportSubscription: Subscription | null = null;
  private searchHandle: ReturnType<typeof setTimeout> | null = null;
  private tableTake = AnalyticsPageComponent.initialTake;

  selectedPeriod = 'Last 30 Days';
  private previousPeriod = 'Last 30 Days';
  private selectedPeriodSig = signal('Last 30 Days');
  readonly periodOptions = ['Last 7 Days', 'Last 30 Days', 'Last 90 Days', 'All Time'];
  selectedMonthlyVisitYears: RegionalMonthlyVisitYearsBack = 1;
  readonly monthlyVisitYearOptions: { value: RegionalMonthlyVisitYearsBack; label: string }[] = [
    { value: 1, label: '1 year' },
    { value: 2, label: '2 years' },
    { value: 3, label: '3 years' },
  ];

  showCustomDateModal = false;
  customDateFrom = '';
  customDateTo = '';
  fromDay = '';
  fromMonth = '';
  fromYear = '';
  toDay = '';
  toMonth = '';
  toYear = '';

  readonly activeFilter = signal<CategoryFilter>('all');

  readonly filters: { key: CategoryFilter; label: string }[] = [
    { key: 'all', label: 'All Categories' },
    { key: 'locations', label: 'Locations' },
    { key: 'destinations', label: 'Destinations' },
    { key: 'events', label: 'Events' },
    { key: 'places', label: 'Places' },
  ];

  readonly typeOptions = ['All Entities', 'Destination', 'Location', 'Place', 'Event'];

  private readonly filterToTypeMap: Record<CategoryFilter, string> = {
    all: 'All Entities',
    locations: 'Location',
    destinations: 'Destination',
    events: 'Event',
    places: 'Place',
  };

  private readonly typeToFilterMap: Record<string, CategoryFilter> = {
    'All Entities': 'all',
    Location: 'locations',
    Destination: 'destinations',
    Event: 'events',
    Place: 'places',
  };

  get selectedType(): string {
    return this.filterToTypeMap[this.activeFilter()];
  }

  set selectedType(value: string) {
    this.setFilter(this.typeToFilterMap[value] ?? 'all');
  }

  readonly searchQuery = signal('');

  sortOpen = false;
  readonly sortFieldSig = signal<EntitySortField>('views');
  readonly sortDirSig = signal<'asc' | 'desc'>('desc');

  readonly sortOptions: { label: string; field: EntitySortField; dir: 'asc' | 'desc' }[] = [
    { label: 'Most Views', field: 'views', dir: 'desc' },
    { label: 'Most Visitors', field: 'visitors', dir: 'desc' },
    { label: 'Most Saves', field: 'saves', dir: 'desc' },
    { label: 'Best Rating', field: 'reviewAvg', dir: 'desc' },
    { label: 'A-Z', field: 'name', dir: 'asc' },
    { label: 'Z-A', field: 'name', dir: 'desc' },
  ];

  readonly currentSortLabel = computed(() => {
    const field = this.sortFieldSig();
    const dir = this.sortDirSig();
    return this.sortOptions.find(option => option.field === field && option.dir === dir)?.label ?? 'Most Views';
  });

  private readonly pageViewsTrendSig = signal<Record<CategoryFilter, number[]>>({
    all: [],
    locations: [],
    destinations: [],
    events: [],
    places: [],
  });

  readonly trendChart = computed(() => this.buildTrendChart(
    this.pageViewsTrendSig()[this.activeFilter()] ?? [],
    this.selectedPeriodSig(),
  ));

  private readonly metricsByFilterSig = signal<Record<CategoryFilter, MetricCard[]>>({
    all: [],
    locations: [],
    destinations: [],
    events: [],
    places: [],
  });

  private readonly entityRowsSig = signal<EntityRow[]>([]);
  private readonly topPerformersSig = signal<EntityRow[]>([]);
  private readonly distributionSig = signal<Record<CategoryFilter, DistributionSlice[]>>({
    all: [],
    locations: [],
    destinations: [],
    events: [],
    places: [],
  });
  private readonly funnelSig = signal<RegionalAnalyticsEngagementFunnel>({
    pageViews: 0,
    uniqueVisitors: 0,
    saves: 0,
    externalClicks: 0,
    reviews: 0,
  });
  private readonly monthlyVisitsSig = signal<RegionalMonthlyVisits>(this.emptyMonthlyVisits(1));
  readonly monthlyVisitChart = computed(() => this.buildMonthlyVisitChart(this.monthlyVisitsSig()));
  private readonly paginationSig = signal<RegionalAnalyticsPagination>({
    totalCount: 0,
    skip: 0,
    take: AnalyticsPageComponent.initialTake,
    hasMore: false,
  });
  readonly visitorBehaviorSig = signal<RegionalVisitorBehaviorAnalytics>(this.emptyVisitorBehavior());

  readonly actionableInsightsSig = signal<ActionableInsight[]>([]);
  readonly insightsSig = signal<AutoInsight[]>([]);
  readonly visibleCount = signal(AnalyticsPageComponent.initialTake);

  readonly filteredMetrics = computed(() => this.metricsByFilterSig()[this.activeFilter()] ?? []);
  readonly sortedRows = computed(() => this.entityRowsSig());
  readonly tableHasMore = computed(() => this.paginationSig().hasMore);
  readonly totalRows = computed(() => this.paginationSig().totalCount);
  readonly remainingRows = computed(() => Math.max(0, this.paginationSig().totalCount - this.sortedRows().length));
  readonly topPerformers = computed(() => this.topPerformersSig());

  private readonly CIRCUMFERENCE = 2 * Math.PI * 64;

  readonly donutArcs = computed(() => {
    const slices = this.distributionSig()[this.activeFilter()] ?? [];
    const total = slices.reduce((sum, item) => sum + item.count, 0);
    let offset = 0;
    return slices.map(slice => {
      const arc = total > 0 ? (slice.count / total) * this.CIRCUMFERENCE : 0;
      const item = {
        ...slice,
        dasharray: `${arc.toFixed(1)} ${(this.CIRCUMFERENCE - arc).toFixed(1)}`,
        dashoffset: `-${offset.toFixed(1)}`,
      };
      offset += arc;
      return item;
    });
  });

  readonly donutTotal = computed(() =>
    (this.distributionSig()[this.activeFilter()] ?? []).reduce((sum, item) => sum + item.count, 0),
  );

  readonly donutTotalDisplay = computed(() => {
    const value = this.donutTotal();
    return value > 999 ? `${(value / 1000).toFixed(1)}k` : String(value);
  });

  readonly distributionChartTitle = computed(() => {
    const labels: Record<CategoryFilter, string> = {
      all: 'Entity Type Distribution',
      places: 'Place Type Distribution',
      destinations: 'Destination Type Distribution',
      events: 'Event Type Distribution',
      locations: 'Location Type Distribution',
    };
    return labels[this.activeFilter()];
  });

  readonly distributionChartSubtitle = computed(() => {
    const labels: Record<CategoryFilter, string> = {
      all: 'Inventory breakdown',
      places: 'Distribution by place type',
      destinations: 'Distribution by destination type',
      events: 'Distribution by event type',
      locations: 'Distribution by location type',
    };
    return labels[this.activeFilter()];
  });

  readonly topPerformingTitle = computed(() => {
    const labels: Record<CategoryFilter, string> = {
      all: 'Top Performing Entities',
      places: 'Top Performing Places',
      destinations: 'Top Performing Destinations',
      events: 'Top Performing Events',
      locations: 'Top Performing Locations',
    };
    return labels[this.activeFilter()];
  });

  readonly entityDetailsTitle = computed(() => {
    const labels: Record<CategoryFilter, string> = {
      all: 'Entity Performance Details',
      places: 'Place Performance Details',
      destinations: 'Destination Performance Details',
      events: 'Event Performance Details',
      locations: 'Location Performance Details',
    };
    return labels[this.activeFilter()];
  });

  readonly entityTypeWord = computed(() => {
    const labels: Record<CategoryFilter, string> = {
      all: 'entities',
      places: 'places',
      destinations: 'destinations',
      events: 'events',
      locations: 'locations',
    };
    return labels[this.activeFilter()];
  });

  readonly searchPlaceholder = computed(() => {
    const labels: Record<CategoryFilter, string> = {
      all: 'Search entities...',
      places: 'Search places...',
      destinations: 'Search destinations...',
      events: 'Search events...',
      locations: 'Search locations...',
    };
    return labels[this.activeFilter()];
  });

  readonly funnelData = computed(() => {
    const funnel = this.funnelSig();
    const base = funnel.pageViews || 1;
    const pct = (value: number) => Math.min(100, Math.round((value / base) * 100));
    return [
      { label: 'Page Views', value: this.formatNum(funnel.pageViews), pct: 100, color: '#185FA5' },
      { label: 'Unique Visitors', value: this.formatNum(funnel.uniqueVisitors), pct: pct(funnel.uniqueVisitors), color: '#2d8dd6' },
      { label: 'Saved', value: this.formatNum(funnel.saves), pct: pct(funnel.saves), color: '#38bdf8' },
      { label: 'Ext. Clicks', value: this.formatNum(funnel.externalClicks), pct: pct(funnel.externalClicks), color: '#60c4f5' },
      { label: 'Reviews', value: this.formatNum(funnel.reviews), pct: pct(funnel.reviews), color: '#f59e0b' },
    ];
  });

  ngOnInit(): void {
    this.loadAssignedRegions();
  }

  ngOnDestroy(): void {
    this.assignedRegionsSubscription?.unsubscribe();
    this.sidebarSummarySubscription?.unsubscribe();
    this.analyticsSubscription?.unsubscribe();
    this.monthlyVisitsSubscription?.unsubscribe();
    this.exportSubscription?.unsubscribe();

    if (this.searchHandle) {
      clearTimeout(this.searchHandle);
    }
  }

  get userName(): string {
    return this.auth.fullName;
  }

  get userRole(): string {
    return this.auth.getUser()?.role ?? '';
  }

  get activeRegion(): RegionalAssignedRegion | null {
    return this.assignedRegions.find(region => region.id === this.activeRegionId) ?? null;
  }

  get regionDisplayName(): string {
    return this.activeRegion?.displayName ?? '';
  }

  get canApply(): boolean {
    return this.fromDay.length === 2 &&
      this.fromMonth.length === 2 &&
      this.fromYear.length === 4 &&
      this.toDay.length === 2 &&
      this.toMonth.length === 2 &&
      this.toYear.length === 4;
  }

  get customDays(): number {
    if (!this.customDateFrom || !this.customDateTo) return 30;
    const [fromDay, fromMonth, fromYear] = this.customDateFrom.split('.').map(Number);
    const [toDay, toMonth, toYear] = this.customDateTo.split('.').map(Number);
    const from = new Date(fromYear, fromMonth - 1, fromDay);
    const to = new Date(toYear, toMonth - 1, toDay);
    return Math.max(1, Math.round((to.getTime() - from.getTime()) / 86_400_000) + 1);
  }

  @HostListener('document:click')
  onDocumentClick(): void {
    if (this.sortOpen) {
      this.sortOpen = false;
    }
  }

  loadAssignedRegions(): void {
    this.regionLoading = true;
    this.errorMessage = null;
    this.authRequired = false;
    this.notifyStateChanged();

    this.assignedRegionsSubscription?.unsubscribe();
    this.assignedRegionsSubscription = this.dashboardService
      .getAssignedRegions()
      .pipe(
        finalize(() => {
          this.regionLoading = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: regions => {
          this.assignedRegions = regions ?? [];

          if (this.assignedRegions.length === 0) {
            this.activeRegionId = null;
            this.pendingRequestsCount = null;
            this.clearPersistedRegion();
            this.clearAnalyticsData();
            return;
          }

          const persistedRegionId = this.readPersistedRegionId();
          const selectedRegion =
            this.assignedRegions.find(region => region.id === persistedRegionId) ??
            this.assignedRegions[0];

          this.setActiveRegion(selectedRegion.id, false);
          this.loadSidebarSummary(selectedRegion.id);
          this.loadAnalytics();
          this.loadMonthlyVisits();
        },
        error: error => {
          this.clearAnalyticsData();
          this.handleLoadError(error, 'Unable to load assigned regions.');
        },
      });
  }

  onRegionSelected(regionId: number): void {
    if (!this.assignedRegions.some(region => region.id === regionId) || regionId === this.activeRegionId) {
      return;
    }

    this.setActiveRegion(regionId, true);
    this.tableTake = AnalyticsPageComponent.initialTake;
    this.visibleCount.set(this.tableTake);
    this.loadSidebarSummary(regionId);
    this.loadAnalytics();
    this.loadMonthlyVisits();
  }

  setFilter(filter: CategoryFilter): void {
    if (filter === this.activeFilter()) {
      return;
    }

    this.activeFilter.set(filter);
    this.tableTake = AnalyticsPageComponent.initialTake;
    this.visibleCount.set(this.tableTake);
    this.loadAnalytics();
    this.loadMonthlyVisits();
  }

  onSearchChange(value: string): void {
    this.searchQuery.set(value);

    if (this.searchHandle) {
      clearTimeout(this.searchHandle);
    }

    this.searchHandle = setTimeout(() => {
      this.tableTake = AnalyticsPageComponent.initialTake;
      this.visibleCount.set(this.tableTake);
      this.loadAnalytics();
      this.loadMonthlyVisits();
    }, AnalyticsPageComponent.searchDebounceMs);
  }

  clearSearch(): void {
    this.onSearchChange('');
  }

  onPeriodChange(value: string): void {
    if (value === 'Custom') {
      this.showCustomDateModal = true;
      return;
    }

    this.previousPeriod = value;
    this.selectedPeriod = value;
    this.selectedPeriodSig.set(value);
    this.customDateFrom = this.customDateTo = '';
    this.fromDay = this.fromMonth = this.fromYear = this.toDay = this.toMonth = this.toYear = '';
    this.tableTake = AnalyticsPageComponent.initialTake;
    this.visibleCount.set(this.tableTake);
    this.loadAnalytics();
  }

  onMonthlyVisitYearsChange(value: RegionalMonthlyVisitYearsBack | string | number): void {
    const selected = Number(value);
    if (!this.isMonthlyVisitYearsBack(selected) || selected === this.selectedMonthlyVisitYears) {
      return;
    }

    this.selectedMonthlyVisitYears = selected;
    this.loadMonthlyVisits();
  }

  applyCustomRange(): void {
    if (!this.canApply) return;
    this.customDateFrom = `${this.fromDay}.${this.fromMonth}.${this.fromYear}`;
    this.customDateTo = `${this.toDay}.${this.toMonth}.${this.toYear}`;
    this.showCustomDateModal = false;
    this.previousPeriod = 'Custom';
    this.selectedPeriodSig.set('Custom');
  }

  cancelCustomRange(): void {
    this.selectedPeriod = this.previousPeriod;
    this.selectedPeriodSig.set(this.previousPeriod);
    this.showCustomDateModal = false;
  }

  onPartInput(event: Event, nextEl: HTMLInputElement): void {
    const element = event.target as HTMLInputElement;
    if (element.value.length >= element.maxLength) {
      nextEl.focus();
      nextEl.select();
    }
  }

  applySortOption(option: { field: EntitySortField; dir: 'asc' | 'desc' }): void {
    this.sortFieldSig.set(option.field);
    this.sortDirSig.set(option.dir);
    this.sortOpen = false;
    this.tableTake = AnalyticsPageComponent.initialTake;
    this.visibleCount.set(this.tableTake);
    this.loadAnalytics();
  }

  showMore(): void {
    if (!this.tableHasMore()) return;
    this.tableTake += AnalyticsPageComponent.initialTake;
    this.visibleCount.set(this.tableTake);
    this.loadAnalytics();
  }

  exportData(): void {
    if (!this.activeRegionId || this.exporting) return;

    this.exporting = true;
    this.errorMessage = null;
    this.notifyStateChanged();

    this.exportSubscription?.unsubscribe();
    this.exportSubscription = this.analyticsService
      .exportCsv(this.currentAnalyticsParams(false))
      .pipe(
        finalize(() => {
          this.exporting = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: blob => this.analyticsService.downloadCsv(blob, this.regionDisplayName, this.periodKey()),
        error: error => this.handleLoadError(error, 'Unable to export analytics data.'),
      });
  }

  topPerformerBarPct(entity: EntityRow): number {
    const top = this.topPerformers()[0];
    return top?.views ? Math.round((entity.views / top.views) * 100) : 0;
  }

  formatNum(value: number): string {
    if (value >= 1_000_000) return `${(value / 1_000_000).toFixed(1).replace(/\.0$/, '')}M`;
    if (value >= 1000) return `${(value / 1000).toFixed(1).replace(/\.0$/, '')}K`;
    return String(value);
  }

  formatDurationSeconds(value: number | null | undefined): string {
    const seconds = Math.max(0, Math.round(value ?? 0));
    const minutes = Math.floor(seconds / 60);
    const remainder = seconds % 60;
    if (minutes <= 0) return `${remainder}s`;
    return `${minutes}m ${remainder.toString().padStart(2, '0')}s`;
  }

  trendLabel(trend: EntityRow['trend']): string {
    return trend === 'up' ? 'Has Activity' : trend === 'down' ? 'Needs Review' : 'No Activity';
  }

  trendClass(trend: EntityRow['trend']): string {
    const classes: Record<EntityRow['trend'], string> = {
      up: 'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200',
      down: 'bg-red-50 text-red-600 ring-1 ring-red-200',
      stable: 'bg-gray-100 text-gray-600 ring-1 ring-gray-200',
    };
    return classes[trend];
  }

  entityStatusClass(status: string | null | undefined): string {
    const normalized = (status ?? '').replace(/\s+/g, '').toLowerCase();
    if (normalized.includes('publish') || normalized.includes('active') || normalized.includes('approved')) {
      return 'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200';
    }
    if (normalized.includes('pending') || normalized.includes('draft')) {
      return 'bg-amber-50 text-amber-700 ring-1 ring-amber-200';
    }
    if (normalized.includes('reject') || normalized.includes('archiv') || normalized.includes('inactive')) {
      return 'bg-red-50 text-red-600 ring-1 ring-red-200';
    }
    return 'bg-gray-100 text-gray-600 ring-1 ring-gray-200';
  }

  typeClass(type: EntityRow['type']): string {
    const classes: Record<EntityRow['type'], string> = {
      PLACE: 'bg-violet-100 text-violet-700',
      DESTINATION: 'bg-blue-100 text-blue-700',
      EVENT: 'bg-emerald-100 text-emerald-700',
      LOCATION: 'bg-amber-100 text-amber-700',
    };
    return classes[type];
  }

  typeLabel(type: EntityRow['type']): string {
    const labels: Record<EntityRow['type'], string> = {
      PLACE: 'Place',
      DESTINATION: 'Destination',
      EVENT: 'Event',
      LOCATION: 'Location',
    };
    return labels[type];
  }

  reviewMinClass(value: number): string {
    if (value < 2.5) return 'text-red-600 font-semibold';
    if (value < 3.5) return 'text-amber-600';
    return 'text-gray-500';
  }

  reviewAvgClass(value: number): string {
    if (value >= 4.5) return 'text-emerald-700 font-semibold';
    if (value >= 3.5) return 'text-gray-800 font-semibold';
    if (value <= 0) return 'text-gray-400 font-semibold';
    return 'text-red-600 font-semibold';
  }

  insightCardBg(highlightClass?: string): string {
    if (highlightClass === 'text-blue-600') return 'bg-blue-50 border-blue-100';
    if (highlightClass === 'text-red-600') return 'bg-red-50 border-red-100';
    return 'bg-gray-50 border-gray-100';
  }

  insightIconBg(type: AutoInsight['type']): string {
    const colors: Record<AutoInsight['type'], string> = {
      success: '#E1F5EE',
      warning: '#FAEEDA',
      danger: '#FCEBEB',
      info: '#E6F1FB',
    };
    return colors[type];
  }

  insightIconColor(type: AutoInsight['type']): string {
    const colors: Record<AutoInsight['type'], string> = {
      success: '#0F6E56',
      warning: '#854F0B',
      danger: '#A32D2D',
      info: '#185FA5',
    };
    return colors[type];
  }

  insightIcon(type: AutoInsight['type']): string {
    const labels: Record<AutoInsight['type'], string> = {
      success: '+',
      warning: '!',
      danger: '!',
      info: 'i',
    };
    return labels[type];
  }

  private loadSidebarSummary(regionId: number): void {
    this.sidebarSummarySubscription?.unsubscribe();
    this.sidebarSummarySubscription = this.dashboardService
      .getSidebarSummary(regionId)
      .subscribe({
        next: summary => {
          this.pendingRequestsCount = summary.pendingRequestsCount;
          if (summary.assignedRegions?.length) {
            this.assignedRegions = summary.assignedRegions;
          }
          if (summary.activeRegion) {
            this.upsertAssignedRegion(summary.activeRegion);
            this.setActiveRegion(summary.activeRegion.id, false);
          }
          this.notifyStateChanged();
        },
        error: () => {
          this.pendingRequestsCount = null;
          this.notifyStateChanged();
        },
      });
  }

  private loadMonthlyVisits(): void {
    if (!this.activeRegionId) {
      this.clearMonthlyVisitData();
      return;
    }

    this.monthlyVisitsLoading = true;
    this.monthlyVisitsErrorMessage = null;
    this.notifyStateChanged();

    this.monthlyVisitsSubscription?.unsubscribe();
    this.monthlyVisitsSubscription = this.analyticsService
      .getMonthlyVisits(this.currentMonthlyVisitParams())
      .pipe(
        finalize(() => {
          this.monthlyVisitsLoading = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: monthlyVisits => {
          this.upsertAssignedRegion(monthlyVisits.activeRegion);
          this.setActiveRegion(monthlyVisits.activeRegion.id, false);
          this.monthlyVisitsSig.set(monthlyVisits);
          this.notifyStateChanged();
        },
        error: error => {
          this.monthlyVisitsSig.set(this.emptyMonthlyVisits(this.selectedMonthlyVisitYears));
          this.monthlyVisitsErrorMessage = this.monthlyVisitsError(error, 'Unable to load monthly visits for the active region.');
          this.notifyStateChanged();
        },
      });
  }

  private loadAnalytics(): void {
    if (!this.activeRegionId) {
      this.clearAnalyticsData();
      return;
    }

    this.analyticsLoading = true;
    this.errorMessage = null;
    this.authRequired = false;
    this.notifyStateChanged();

    this.analyticsSubscription?.unsubscribe();
    this.analyticsSubscription = this.analyticsService
      .getOverview(this.currentAnalyticsParams())
      .pipe(
        finalize(() => {
          this.analyticsLoading = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: overview => {
          const filter = this.activeFilter();
          this.upsertAssignedRegion(overview.activeRegion);
          this.setActiveRegion(overview.activeRegion.id, false);
          this.pendingRequestsCount = overview.pendingRequestsCount;
          this.metricsByFilterSig.set(this.analyticsService.asBucket(filter, this.analyticsService.buildMetricCards(overview), [] as MetricCard[]));
          this.entityRowsSig.set(this.analyticsService.mapEntityRows(overview.entityRows));
          this.topPerformersSig.set(this.analyticsService.mapEntityRows(overview.topPerformingEntities));
          this.distributionSig.set(this.analyticsService.asBucket(filter, this.analyticsService.mapDistribution(overview.entityTypeDistribution), [] as DistributionSlice[]));
          this.pageViewsTrendSig.set(this.analyticsService.asBucket(filter, this.analyticsService.mapTrend(overview.pageViewsTrend), [] as number[]));
          this.actionableInsightsSig.set(this.analyticsService.buildActionableInsights(overview.actionableInsights));
          this.insightsSig.set(this.analyticsService.buildAutoInsights(overview.actionableInsights.messages ?? []));
          this.visitorBehaviorSig.set(overview.visitorBehavior ?? this.emptyVisitorBehavior());
          this.funnelSig.set(overview.engagementFunnel);
          this.paginationSig.set(overview.pagination);
          this.visibleCount.set(overview.pagination.take);
          this.notifyStateChanged();
        },
        error: error => {
          this.clearAnalyticsData();
          this.handleLoadError(error, 'Unable to load analytics for the active region.');
        },
      });
  }

  private currentMonthlyVisitParams() {
    return {
      regionId: this.activeRegionId ?? 0,
      selectedYearsBack: this.selectedMonthlyVisitYears,
      entityType: this.entityTypeKey(),
      search: this.searchQuery(),
    };
  }

  private currentAnalyticsParams(includePaging = true) {
    return {
      regionId: this.activeRegionId ?? 0,
      period: this.periodKey(),
      entityType: this.entityTypeKey(),
      search: this.searchQuery(),
      skip: 0,
      take: includePaging ? this.tableTake : undefined,
      sort: this.sortKey(),
    };
  }

  private periodKey(): string {
    switch (this.selectedPeriod) {
      case 'Last 7 Days':
        return '7d';
      case 'Last 90 Days':
        return '90d';
      case 'All Time':
        return 'all';
      case 'Last 30 Days':
      default:
        return '30d';
    }
  }

  private entityTypeKey(): RegionalAnalyticsEntityType | 'all' {
    switch (this.activeFilter()) {
      case 'destinations':
        return 'destination';
      case 'locations':
        return 'location';
      case 'places':
        return 'place';
      case 'events':
        return 'event';
      case 'all':
      default:
        return 'all';
    }
  }

  private sortKey(): string {
    const field = this.sortFieldSig();
    const direction = this.sortDirSig();

    switch (field) {
      case 'visitors':
        return `visitors_${direction}`;
      case 'saves':
        return `saves_${direction}`;
      case 'reviewAvg':
        return `rating_${direction}`;
      case 'name':
        return `name_${direction}`;
      case 'views':
      default:
        return `views_${direction}`;
    }
  }

  private setActiveRegion(regionId: number, persist: boolean): void {
    this.activeRegionId = regionId;
    if (persist) {
      this.persistRegion(regionId);
    }
  }

  private clearAnalyticsData(): void {
    this.metricsByFilterSig.set(this.emptyBuckets<MetricCard[]>());
    this.entityRowsSig.set([]);
    this.topPerformersSig.set([]);
    this.distributionSig.set(this.emptyBuckets<DistributionSlice[]>());
    this.pageViewsTrendSig.set(this.emptyBuckets<number[]>());
    this.actionableInsightsSig.set([]);
    this.insightsSig.set([]);
    this.visitorBehaviorSig.set(this.emptyVisitorBehavior());
    this.funnelSig.set({ pageViews: 0, uniqueVisitors: 0, saves: 0, externalClicks: 0, reviews: 0 });
    this.paginationSig.set({ totalCount: 0, skip: 0, take: AnalyticsPageComponent.initialTake, hasMore: false });
    this.visibleCount.set(AnalyticsPageComponent.initialTake);
    this.clearMonthlyVisitData();
  }

  private clearMonthlyVisitData(): void {
    this.monthlyVisitsErrorMessage = null;
    this.monthlyVisitsSig.set(this.emptyMonthlyVisits(this.selectedMonthlyVisitYears));
  }

  private emptyVisitorBehavior(): RegionalVisitorBehaviorAnalytics {
    return {
      loggedInVisitors: 0,
      anonymousVisitors: 0,
      returningVisitors: 0,
      returningVisitorRatePercent: 0,
      topTimeSpentEntity: null,
      topTimeSpentPages: [],
      hasTimeSpentData: false,
      notes: [],
    };
  }

  private emptyMonthlyVisits(selectedYearsBack: RegionalMonthlyVisitYearsBack): RegionalMonthlyVisits {
    return {
      activeRegion: this.activeRegion ?? {
        id: this.activeRegionId ?? 0,
        name: '',
        displayName: this.regionDisplayName,
        territoryName: '',
      },
      selectedYearsBack,
      monthLabels: this.buildTrailingMonthLabels(),
      series: Array.from({ length: selectedYearsBack }, (_, index) => ({
        yearLabel: selectedYearsBack === 1 ? 'Last 12 months' : `Year ${index + 1}`,
        year: 0,
        startDate: '',
        endDate: '',
        monthlyVisitCounts: Array.from({ length: 12 }, () => 0),
        totalVisits: 0,
      })),
      totalVisits: 0,
      visitMetric: 'entity_view',
      visitEventTypes: ['detail_view', 'page_view'],
      timeZone: 'UTC',
    };
  }

  private buildTrailingMonthLabels(): string[] {
    const labels = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    const now = new Date();
    const currentMonthIndex = now.getUTCMonth();
    return Array.from({ length: 12 }, (_, index) => {
      const monthIndex = (currentMonthIndex - 11 + index + 12) % 12;
      return labels[monthIndex];
    });
  }

  private emptyBuckets<T>(): Record<CategoryFilter, T> {
    return {
      all: [] as T,
      locations: [] as T,
      destinations: [] as T,
      events: [] as T,
      places: [] as T,
    };
  }

  private upsertAssignedRegion(region: RegionalAssignedRegion): void {
    const existingIndex = this.assignedRegions.findIndex(item => item.id === region.id);
    if (existingIndex >= 0) {
      this.assignedRegions = this.assignedRegions.map(item => item.id === region.id ? region : item);
      return;
    }

    this.assignedRegions = [region, ...this.assignedRegions];
  }

  private readPersistedRegionId(): number | null {
    try {
      const value = sessionStorage.getItem(AnalyticsPageComponent.activeRegionStorageKey);
      const parsed = value ? Number(value) : NaN;
      return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
    } catch {
      return null;
    }
  }

  private persistRegion(regionId: number): void {
    try {
      sessionStorage.setItem(AnalyticsPageComponent.activeRegionStorageKey, String(regionId));
    } catch {
      // Active region persistence is only a session convenience.
    }
  }

  private clearPersistedRegion(): void {
    try {
      sessionStorage.removeItem(AnalyticsPageComponent.activeRegionStorageKey);
    } catch {
      // Active region persistence is only a session convenience.
    }
  }

  private handleLoadError(error: unknown, fallback: string): void {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 401) {
        this.authRequired = true;
        this.errorMessage = 'Sign in with an authenticated Regional Manager account to view analytics.';
      } else if (error.status === 403) {
        this.errorMessage = 'You do not have access to analytics for this assigned region.';
      } else {
        this.errorMessage = error.error?.message ?? fallback;
      }
    } else {
      this.errorMessage = fallback;
    }

    this.notifyStateChanged();
  }

  private monthlyVisitsError(error: unknown, fallback: string): string {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 401) {
        return 'Sign in with an authenticated Regional Manager account to view monthly visits.';
      }
      if (error.status === 403) {
        return 'You do not have access to monthly visits for this assigned region.';
      }
      return error.error?.message ?? fallback;
    }

    return fallback;
  }

  private notifyStateChanged(): void {
    this.cdr.markForCheck();
  }

  private isMonthlyVisitYearsBack(value: number): value is RegionalMonthlyVisitYearsBack {
    return value === 1 || value === 2 || value === 3;
  }

  private buildMonthlyVisitChart(data: RegionalMonthlyVisits): MonthlyVisitChart {
    const chartWidth = 720;
    const plotLeft = 48;
    const plotRight = 18;
    const plotTop = 18;
    const plotHeight = 166;
    const plotBottom = plotTop + plotHeight;
    const monthLabels = data.monthLabels.length === 12 ? data.monthLabels : this.buildTrailingMonthLabels();
    const series = data.series.length > 0 ? data.series : this.emptyMonthlyVisits(this.selectedMonthlyVisitYears).series;
    const values = series.flatMap(item => item.monthlyVisitCounts);
    const maxValue = Math.max(0, ...values);
    const step = this.niceStep(Math.max(1, maxValue));
    const yMax = Math.max(step * 4, 1);
    const plotWidth = chartWidth - plotLeft - plotRight;
    const groupWidth = plotWidth / 12;
    const seriesCount = Math.max(1, series.length);
    const innerPadding = seriesCount === 1 ? 16 : 10;
    const barGap = 2;
    const barWidth = Math.max(
      4,
      Math.min(13, (groupWidth - innerPadding * 2 - barGap * (seriesCount - 1)) / seriesCount),
    );
    const totalBarWidth = barWidth * seriesCount + barGap * (seriesCount - 1);
    const colors = ['#185FA5', '#38bdf8', '#f59e0b'];

    const bars: MonthlyVisitChartBar[] = [];
    monthLabels.forEach((monthLabel, monthIndex) => {
      const groupX = plotLeft + monthIndex * groupWidth;
      series.forEach((item, seriesIndex) => {
        const value = Math.max(0, item.monthlyVisitCounts[monthIndex] ?? 0);
        const height = (value / yMax) * plotHeight;
        bars.push({
          x: +(groupX + (groupWidth - totalBarWidth) / 2 + seriesIndex * (barWidth + barGap)).toFixed(1),
          y: +(plotBottom - height).toFixed(1),
          width: +barWidth.toFixed(1),
          height: +height.toFixed(1),
          color: colors[seriesIndex % colors.length],
          value,
          monthLabel,
          yearLabel: item.yearLabel,
        });
      });
    });

    return {
      bars,
      legend: series.map((item, index) => ({
        label: item.yearLabel,
        color: colors[index % colors.length],
        total: item.totalVisits,
      })),
      yLabels: [4, 3, 2, 1, 0].map(multiplier => ({
        y: +(plotBottom - (multiplier / 4) * plotHeight).toFixed(1),
        text: this.fmtK(step * multiplier),
        value: step * multiplier,
      })),
      xLabels: monthLabels.map((label, index) => ({
        x: +(plotLeft + index * groupWidth + groupWidth / 2).toFixed(1),
        text: label,
      })),
      isEmpty: maxValue === 0,
      totalVisits: data.totalVisits,
    };
  }

  private buildTrendChart(raw: number[], period: string): {
    linePath: string;
    areaPath: string;
    yLabels: { y: number; text: string }[];
    xLabels: { x: number; text: string }[];
    subtitle: string;
  } {
    const { points, xLabels, subtitle } = this.getPeriodSlice(raw, period);
    const fallback = {
      linePath: '',
      areaPath: '',
      yLabels: [
        { y: 18, text: '8K' },
        { y: 58, text: '6K' },
        { y: 98, text: '4K' },
        { y: 138, text: '2K' },
      ],
      xLabels,
      subtitle,
    };

    if (points.length === 0 || points.every(value => value === 0)) {
      return fallback;
    }

    const maxVal = Math.max(...points);
    const step = this.niceStep(maxVal);
    const count = points.length;
    const toX = (index: number) => 24 + (count > 1 ? index / (count - 1) : 0) * 292;
    const toY = (value: number) => Math.min(148, 134 - ((value - step) / (step * 3)) * 120);
    const pts = points.map((value, index) => ({ x: toX(index), y: toY(value) }));
    const linePath = this.smoothSVGPath(pts);
    const areaPath = `${linePath} L${pts[count - 1].x.toFixed(1)},148 L${pts[0].x.toFixed(1)},148 Z`;

    return {
      linePath,
      areaPath,
      yLabels: [
        { y: 18, text: this.fmtK(step * 4) },
        { y: 58, text: this.fmtK(step * 3) },
        { y: 98, text: this.fmtK(step * 2) },
        { y: 138, text: this.fmtK(step) },
      ],
      xLabels,
      subtitle,
    };
  }

  private getPeriodSlice(raw: number[], period: string): {
    points: number[];
    xLabels: { x: number; text: string }[];
    subtitle: string;
  } {
    const px = (index: number, count: number) => +(24 + (count > 1 ? index / (count - 1) : 0) * 292).toFixed(1);

    if (period === 'Last 7 Days') {
      const points = raw.slice(-7);
      return {
        points,
        xLabels: points.map((_, index) => ({ x: px(index, points.length), text: String(index + 1) })),
        subtitle: 'Rolling 7-day view engagement',
      };
    }

    if (period === 'Last 90 Days') {
      const source = raw.length >= 90 ? raw.slice(-90) : raw;
      const bucketSize = Math.max(1, Math.ceil(source.length / 6));
      const points = Array.from({ length: Math.ceil(source.length / bucketSize) }, (_, index) =>
        source.slice(index * bucketSize, (index + 1) * bucketSize).reduce((sum, value) => sum + value, 0),
      );
      return {
        points,
        xLabels: points.map((_, index) => ({ x: px(index, points.length), text: String(index + 1) })),
        subtitle: 'Grouped view engagement - last 90 days',
      };
    }

    if (period === 'All Time') {
      const source = raw.length > 0 ? raw : [];
      const bucketSize = Math.max(1, Math.ceil(source.length / 12));
      const points = Array.from({ length: Math.ceil(source.length / bucketSize) }, (_, index) =>
        source.slice(index * bucketSize, (index + 1) * bucketSize).reduce((sum, value) => sum + value, 0),
      );
      return {
        points,
        xLabels: points.map((_, index) => ({ x: px(index, points.length), text: String(index + 1) })),
        subtitle: 'View engagement across available analytics history',
      };
    }

    const points = raw.slice(-30);
    const count = points.length;
    return {
      points,
      xLabels: ([[0, '1'], [4, '5'], [9, '10'], [14, '15'], [19, '20'], [count - 1, '30']] as [number, string][])
        .filter(([index]) => index >= 0 && index < count)
        .map(([index, text]) => ({ x: px(index, count), text })),
      subtitle: 'Rolling 30-day view engagement',
    };
  }

  private niceStep(maxVal: number): number {
    if (maxVal <= 0) return 100;
    const minStep = maxVal / 4;
    const magnitude = Math.pow(10, Math.floor(Math.log10(minStep)));
    const normalized = minStep / magnitude;
    const base = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 2.5 ? 2.5 : normalized <= 5 ? 5 : 10;
    return base * magnitude;
  }

  private fmtK(value: number): string {
    if (value >= 1_000_000) return `${(value / 1_000_000).toFixed(1).replace(/\.0$/, '')}M`;
    if (value >= 1000) return `${(value / 1000).toFixed(1).replace(/\.0$/, '')}K`;
    return Math.round(value).toString();
  }

  private smoothSVGPath(points: { x: number; y: number }[]): string {
    if (points.length === 0) return '';
    if (points.length === 1) return `M${points[0].x.toFixed(1)},${points[0].y.toFixed(1)}`;

    let path = `M${points[0].x.toFixed(1)},${points[0].y.toFixed(1)}`;
    for (let i = 1; i < points.length; i++) {
      const p0 = points[i - 2] ?? points[i - 1];
      const p1 = points[i - 1];
      const p2 = points[i];
      const p3 = points[i + 1] ?? points[i];
      const cp1 = { x: p1.x + (p2.x - p0.x) / 6, y: p1.y + (p2.y - p0.y) / 6 };
      const cp2 = { x: p2.x - (p3.x - p1.x) / 6, y: p2.y - (p3.y - p1.y) / 6 };
      path += ` C${cp1.x.toFixed(1)},${cp1.y.toFixed(1)} ${cp2.x.toFixed(1)},${cp2.y.toFixed(1)} ${p2.x.toFixed(1)},${p2.y.toFixed(1)}`;
    }

    return path;
  }
}
