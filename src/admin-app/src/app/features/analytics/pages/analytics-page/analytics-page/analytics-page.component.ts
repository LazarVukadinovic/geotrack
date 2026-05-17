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
  RegionalAnalyticsEntityDetail,
  RegionalAnalyticsEntityDetailMetrics,
  RegionalAnalyticsExternalPartnerClick,
  RegionalAnalyticsEntityTypeSpecific,
  RegionalAnalyticsEngagementFunnel,
  RegionalAnalyticsEntityType,
  RegionalAnalyticsPagination,
  RegionalPlaceOperatingHour,
  RegionalDestinationRankingItem,
  RegionalMonthlyVisits,
  RegionalMonthlyVisitYearsBack,
  RegionalTagViewsAnalyticsResponse,
  RegionalTagViewsAvailableTag,
  RegionalTagViewsItem,
  RegionalTagViewsPeriod,
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

interface TagViewsChart {
  width: number;
  bars: TagViewsChartBar[];
  yLabels: { y: number; text: string; value: number }[];
  xLabels: { x: number; text: string; fullText: string }[];
  isEmpty: boolean;
  totalViews: number;
}

interface TagViewsChartBar {
  x: number;
  y: number;
  width: number;
  height: number;
  color: string;
  value: number;
  tagName: string;
  percentageOfTotal: number;
}

interface ExternalPartnerClickChart {
  arcs: ExternalPartnerClickArc[];
  items: ExternalPartnerClickLegendItem[];
  totalClicks: number;
  hasClickData: boolean;
}

interface ExternalPartnerClickArc {
  partnerLabel: string;
  clickCount: number;
  sharePercent: number;
  color: string;
  dasharray: string;
  dashoffset: string;
}

interface ExternalPartnerClickLegendItem extends RegionalAnalyticsExternalPartnerClick {
  color: string;
}

interface DetailInfoItem {
  label: string;
  value: string;
  href?: string;
  external?: boolean;
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
  tagViewsLoading = false;
  exporting = false;
  errorMessage: string | null = null;
  monthlyVisitsErrorMessage: string | null = null;
  tagViewsErrorMessage: string | null = null;
  authRequired = false;

  private assignedRegionsSubscription: Subscription | null = null;
  private sidebarSummarySubscription: Subscription | null = null;
  private analyticsSubscription: Subscription | null = null;
  private monthlyVisitsSubscription: Subscription | null = null;
  private tagViewsSubscription: Subscription | null = null;
  private exportSubscription: Subscription | null = null;
  private entityDetailSubscription: Subscription | null = null;
  private previousBodyOverflow: string | null = null;
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
  selectedTagViewsPeriod: RegionalTagViewsPeriod = '1m';
  readonly tagViewPeriodOptions: { value: RegionalTagViewsPeriod; label: string }[] = [
    { value: '24h', label: '24h' },
    { value: '7d', label: '7 days' },
    { value: '1m', label: '1 month' },
    { value: '3m', label: '3 months' },
    { value: '6m', label: '6 months' },
    { value: '1y', label: '1 year' },
  ];
  tagSelectorOpen = false;

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
  private readonly destinationRankingSig = signal<RegionalDestinationRankingItem[]>([]);
  private readonly monthlyVisitsSig = signal<RegionalMonthlyVisits>(this.emptyMonthlyVisits(1));
  readonly monthlyVisitChart = computed(() => this.buildMonthlyVisitChart(this.monthlyVisitsSig()));
  private readonly tagViewsSig = signal<RegionalTagViewsAnalyticsResponse>(this.emptyTagViews());
  private readonly selectedTagIdsSig = signal<number[]>([]);
  readonly tagSearchSig = signal('');
  readonly tagViewsChart = computed(() => this.buildTagViewsChart(this.tagViewsSig()));
  readonly availableTagOptions = computed(() => this.tagViewsSig().availableTags ?? []);
  readonly visibleTagOptions = computed(() => {
    const search = this.tagSearchSig().trim().toLowerCase();
    const tags = this.availableTagOptions();
    if (!search) {
      return tags;
    }

    return tags.filter(tag =>
      tag.name.toLowerCase().includes(search) ||
      tag.entityScopes.some(scope => this.entityScopeLabel(scope).toLowerCase().includes(search)),
    );
  });
  readonly selectedTagControlLabel = computed(() => {
    const selectedCount = this.selectedTagIdsSig().length;
    if (selectedCount === 0) {
      return 'Top tags';
    }

    return selectedCount === 1 ? '1 tag selected' : `${selectedCount} tags selected`;
  });
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
  readonly selectedEntityRowSig = signal<EntityRow | null>(null);
  readonly selectedEntityDetailSig = signal<RegionalAnalyticsEntityDetail | null>(null);
  entityDetailLoading = false;
  entityDetailErrorMessage: string | null = null;
  readonly selectedExternalPartnerClickChart = computed(() => {
    const detail = this.selectedEntityDetailSig();
    if (!detail || !this.isStayDetail(detail)) {
      return null;
    }

    return this.buildExternalPartnerClickChart(detail.typeSpecific.externalPartnerClicks ?? []);
  });

  readonly filteredMetrics = computed(() => this.metricsByFilterSig()[this.activeFilter()] ?? []);
  readonly sortedRows = computed(() => this.entityRowsSig());
  readonly tableHasMore = computed(() => this.paginationSig().hasMore);
  readonly totalRows = computed(() => this.paginationSig().totalCount);
  readonly remainingRows = computed(() => Math.max(0, this.paginationSig().totalCount - this.sortedRows().length));
  readonly topPerformers = computed(() => this.topPerformersSig());
  readonly destinationRanking = computed(() => this.destinationRankingSig());

  private readonly CIRCUMFERENCE = 2 * Math.PI * 64;
  private readonly externalPartnerClickColors = ['#185FA5', '#ef4444', '#f59e0b', '#10b981', '#8b5cf6'];

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
    this.tagViewsSubscription?.unsubscribe();
    this.exportSubscription?.unsubscribe();
    this.entityDetailSubscription?.unsubscribe();
    this.unlockBodyScroll();

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
    if (this.tagSelectorOpen) {
      this.tagSelectorOpen = false;
    }
  }

  @HostListener('document:keydown.escape', ['$event'])
  onEscapeKey(event: Event): void {
    if (this.selectedEntityRowSig()) {
      event.preventDefault();
      this.closeEntityDetails();
      return;
    }

    if (this.showCustomDateModal) {
      event.preventDefault();
      this.cancelCustomRange();
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
            this.resetSelectedTagViews();
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
          this.loadTagViews();
        },
        error: error => {
          this.resetSelectedTagViews();
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
    this.closeEntityDetails();
    this.resetSelectedTagViews();
    this.tableTake = AnalyticsPageComponent.initialTake;
    this.visibleCount.set(this.tableTake);
    this.loadSidebarSummary(regionId);
    this.loadAnalytics();
    this.loadMonthlyVisits();
    this.loadTagViews();
  }

  setFilter(filter: CategoryFilter): void {
    if (filter === this.activeFilter()) {
      return;
    }

    this.activeFilter.set(filter);
    this.closeEntityDetails();
    this.tableTake = AnalyticsPageComponent.initialTake;
    this.visibleCount.set(this.tableTake);
    this.loadAnalytics();
    this.loadMonthlyVisits();
    this.loadTagViews();
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

  onTagViewsPeriodChange(value: RegionalTagViewsPeriod | string): void {
    if (!this.isTagViewsPeriod(value) || value === this.selectedTagViewsPeriod) {
      return;
    }

    this.selectedTagViewsPeriod = value;
    this.loadTagViews();
  }

  toggleTagSelector(event: Event): void {
    event.stopPropagation();
    this.tagSelectorOpen = !this.tagSelectorOpen;
  }

  onTagSearchChange(value: string): void {
    this.tagSearchSig.set(value);
  }

  onTagCheckboxChange(tagId: number, event: Event): void {
    const input = event.target as HTMLInputElement;
    this.setTagSelection(tagId, input.checked);
  }

  isTagSelected(tagId: number): boolean {
    return this.selectedTagIdsSig().includes(tagId);
  }

  clearSelectedTags(): void {
    if (this.selectedTagIdsSig().length === 0) {
      return;
    }

    this.selectedTagIdsSig.set([]);
    this.loadTagViews();
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

  openEntityDetails(row: EntityRow, event?: Event): void {
    event?.preventDefault();
    event?.stopPropagation();

    if (!this.activeRegionId) {
      return;
    }

    this.selectedEntityRowSig.set(row);
    this.selectedEntityDetailSig.set(null);
    this.entityDetailErrorMessage = null;
    this.entityDetailLoading = true;
    this.lockBodyScroll();
    this.notifyStateChanged();

    this.entityDetailSubscription?.unsubscribe();
    this.entityDetailSubscription = this.analyticsService
      .getEntityDetail({
        regionId: this.activeRegionId,
        entityType: row.entityType,
        entityId: row.entityId,
        period: this.periodKey(),
      })
      .pipe(
        finalize(() => {
          this.entityDetailLoading = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: detail => {
          this.selectedEntityDetailSig.set(this.normalizeEntityDetail(detail));
          this.notifyStateChanged();
        },
        error: error => {
          this.selectedEntityDetailSig.set(null);
          this.entityDetailErrorMessage = this.entityDetailError(error);
          this.notifyStateChanged();
        },
      });
  }

  closeEntityDetails(): void {
    this.entityDetailSubscription?.unsubscribe();
    this.entityDetailSubscription = null;
    this.selectedEntityRowSig.set(null);
    this.selectedEntityDetailSig.set(null);
    this.entityDetailErrorMessage = null;
    this.entityDetailLoading = false;
    this.unlockBodyScroll();
    this.notifyStateChanged();
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

  destinationRankingBarPct(item: RegionalDestinationRankingItem): number {
    const topVisits = this.destinationRanking().reduce((max, row) => Math.max(max, row.visits), 0);
    return topVisits > 0 ? Math.round((item.visits / topVisits) * 100) : 0;
  }

  entityScopeLabel(scope: string): string {
    switch (scope) {
      case 'destination':
        return 'Destination';
      case 'location':
        return 'Location';
      case 'event':
        return 'Event';
      case 'place':
        return 'Place';
      default:
        return scope;
    }
  }

  tagScopeSummary(tag: RegionalTagViewsAvailableTag | RegionalTagViewsItem): string {
    return (tag.entityScopes ?? []).map(scope => this.entityScopeLabel(scope)).join(', ');
  }

  formatPercent(value: number): string {
    return `${Math.max(0, value).toFixed(value % 1 === 0 ? 0 : 1)}%`;
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

  formatDateTime(value: string | null | undefined): string {
    if (!value) return '';
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return '';
    return new Intl.DateTimeFormat(undefined, {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    }).format(date);
  }

  formatDate(value: string | null | undefined): string {
    if (!value) return '';
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return '';
    return new Intl.DateTimeFormat(undefined, {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
    }).format(date);
  }

  formatCoordinates(detail: RegionalAnalyticsEntityDetail): string | null {
    if (detail.latitude === null || detail.latitude === undefined || detail.longitude === null || detail.longitude === undefined) {
      return null;
    }

    return `${detail.latitude.toFixed(5)}, ${detail.longitude.toFixed(5)}`;
  }

  entityDetailImageBackground(row: EntityRow | null, detail: RegionalAnalyticsEntityDetail | null): string {
    if (detail?.imageUrl) {
      return `url("${detail.imageUrl}") center/cover`;
    }

    return row?.imageBg ?? 'linear-gradient(135deg, #1252A0 0%, #38bdf8 100%)';
  }

  entityDetailTitle(row: EntityRow | null, detail: RegionalAnalyticsEntityDetail | null): string {
    return detail?.title || detail?.name || row?.name || 'Entity details';
  }

  entityDetailType(row: EntityRow | null, detail: RegionalAnalyticsEntityDetail | null): EntityRow['type'] {
    if (row) {
      return row.type;
    }

    switch (detail?.entityType) {
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

  isStayDetail(detail: RegionalAnalyticsEntityDetail | null): boolean {
    if (!detail || detail.entityType !== 'place') {
      return false;
    }

    if (detail.typeSpecific?.stayType?.trim()) {
      return true;
    }

    const placeType = (detail.typeSpecific?.placeType ?? '')
      .trim()
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '');

    return ['stay', 'stays', 'hotel', 'hotels', 'accommodation', 'accommodations', 'lodging'].includes(placeType);
  }

  entityDetailMetrics(detail: RegionalAnalyticsEntityDetail | null): { label: string; value: string; sub?: string }[] {
    const analytics = detail?.analytics ?? this.emptyEntityDetailMetrics();
    return [
      { label: 'Views', value: this.formatNum(analytics.views) },
      { label: 'Visitors', value: this.formatNum(analytics.uniqueVisitors) },
      { label: 'Saves', value: this.formatNum(analytics.saves) },
      { label: 'Actions', value: this.formatNum(analytics.externalClicks) },
      { label: 'Rating', value: analytics.averageRating.toFixed(1), sub: `${analytics.ratingCount} ratings` },
      { label: 'Reviews', value: this.formatNum(analytics.reviews) },
    ];
  }

  entityOverviewItems(detail: RegionalAnalyticsEntityDetail): DetailInfoItem[] {
    return [
      { label: 'Region', value: detail.regionDisplayName },
      { label: 'Destination', value: detail.destinationName ?? '' },
      { label: 'Location', value: detail.locationName ?? '' },
      { label: 'Place', value: detail.placeName ?? '' },
      { label: 'Address', value: detail.address ?? '' },
      { label: 'Coordinates', value: this.formatCoordinates(detail) ?? '' },
      { label: 'Created', value: this.formatDate(detail.createdAt) },
      { label: 'Updated', value: this.formatDate(detail.updatedAt) },
      { label: 'Published', value: this.formatDate(detail.publishedAt) },
      { label: 'Last viewed', value: this.formatDateTime(detail.analytics.lastViewedAt) },
    ].filter(item => !!item.value);
  }

  typeSpecificItems(detail: RegionalAnalyticsEntityDetail): DetailInfoItem[] {
    const typeSpecific = detail.typeSpecific ?? this.emptyTypeSpecific();

    switch (detail.entityType) {
      case 'destination':
        return this.compactItems([
          { label: 'Destination type', value: typeSpecific.destinationType ?? '' },
          { label: 'Locations', value: this.optionalCount(typeSpecific.locationsCount) },
          { label: 'Places', value: this.optionalCount(typeSpecific.placesCount) },
          { label: 'Events', value: this.optionalCount(typeSpecific.eventsCount) },
          { label: 'Boundary type', value: typeSpecific.boundaryShapeType ?? '' },
          { label: 'Boundary note', value: typeSpecific.boundaryNote ?? '' },
        ]);
      case 'location':
        return this.compactItems([
          { label: 'Location type', value: typeSpecific.locationType ?? '' },
          { label: 'Parent destination', value: typeSpecific.parentDestination ?? '' },
          { label: 'Places', value: this.optionalCount(typeSpecific.placesCount) },
          { label: 'Events', value: this.optionalCount(typeSpecific.eventsCount) },
          { label: 'Boundary type', value: typeSpecific.boundaryShapeType ?? '' },
          { label: 'Boundary note', value: typeSpecific.boundaryNote ?? '' },
        ]);
      case 'place':
        return this.compactItems([
          { label: 'Place type', value: typeSpecific.placeType ?? '' },
          { label: 'Stay type', value: typeSpecific.stayType ?? '' },
          { label: 'Parent location', value: typeSpecific.parentLocation ?? '' },
          { label: 'Parent destination', value: typeSpecific.parentDestination ?? '' },
          { label: 'Website', value: typeSpecific.website ?? '', href: typeSpecific.website ?? undefined, external: true },
          { label: 'Booking link', value: typeSpecific.bookingExternalLink ?? '', href: typeSpecific.bookingExternalLink ?? undefined, external: true },
          { label: 'Contact phone', value: typeSpecific.contactPhone ?? '' },
          { label: 'Contact email', value: typeSpecific.contactEmail ?? '' },
          { label: 'Price range', value: typeSpecific.priceRange ?? '' },
          { label: 'Opening hours', value: typeSpecific.openingHours ?? '' },
          { label: 'Best time', value: typeSpecific.bestTimeToVisit ?? '' },
          { label: 'Visitor tips', value: typeSpecific.visitorTips ?? '' },
          { label: 'Access notes', value: typeSpecific.accessNotes ?? '' },
          { label: 'Accessibility', value: typeSpecific.accessibilityNotes ?? '' },
        ]);
      case 'event':
        return this.compactItems([
          { label: 'Event type', value: typeSpecific.eventType ?? typeSpecific.category ?? '' },
          { label: 'Parent', value: typeSpecific.parentEntityType ?? '' },
          { label: 'Destination', value: typeSpecific.parentDestination ?? '' },
          { label: 'Location', value: typeSpecific.parentLocation ?? '' },
          { label: 'Place', value: typeSpecific.parentPlace ?? '' },
          { label: 'Starts', value: this.formatDateTime(typeSpecific.startsAt) },
          { label: 'Ends', value: this.formatDateTime(typeSpecific.endsAt) },
          { label: 'All day', value: typeSpecific.allDay === null || typeSpecific.allDay === undefined ? '' : typeSpecific.allDay ? 'Yes' : 'No' },
          { label: 'Recurring', value: typeSpecific.isRecurring === null || typeSpecific.isRecurring === undefined ? '' : typeSpecific.isRecurring ? 'Yes' : 'No' },
          { label: 'Venue', value: typeSpecific.venueName ?? '' },
          { label: 'Ticket', value: typeSpecific.isFree ? 'Free' : typeSpecific.ticketPrice ?? '' },
          { label: 'Ticket link', value: typeSpecific.externalTicketLink ?? '', href: typeSpecific.externalTicketLink ?? undefined, external: true },
          { label: 'Organizer', value: typeSpecific.organizerName ?? '' },
          { label: 'Contact email', value: typeSpecific.contactEmail ?? '' },
          { label: 'Attendance limit', value: this.optionalCount(typeSpecific.attendanceLimit) },
          { label: 'Age restriction', value: typeSpecific.ageRestriction ?? '' },
          { label: 'Duration', value: typeSpecific.duration ?? '' },
          { label: 'Venue details', value: typeSpecific.venueDescription ?? '' },
          { label: 'Arrival tips', value: typeSpecific.arrivalTips ?? '' },
          { label: 'Accessibility', value: typeSpecific.accessibility ?? '' },
        ]);
      default:
        return [];
    }
  }

  operatingHoursLabel(hour: RegionalPlaceOperatingHour): string {
    if (!hour.isOpen) {
      return `${hour.dayOfWeek}: Closed`;
    }

    return `${hour.dayOfWeek}: ${hour.openTime ?? ''} - ${hour.closeTime ?? ''}`.trim();
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

  private compactItems(items: DetailInfoItem[]): DetailInfoItem[] {
    return items.filter(item => !!item.value?.trim());
  }

  private optionalCount(value: number | null | undefined): string {
    return value === null || value === undefined ? '' : this.formatNum(value);
  }

  private emptyEntityDetailMetrics(): RegionalAnalyticsEntityDetailMetrics {
    return {
      views: 0,
      uniqueVisitors: 0,
      saves: 0,
      externalClicks: 0,
      reviews: 0,
      averageRating: 0,
      ratingCount: 0,
      trendChangePercent: 0,
      lastViewedAt: null,
    };
  }

  private emptyTypeSpecific(): RegionalAnalyticsEntityTypeSpecific {
    return {
      operatingHours: [],
      amenities: [],
      bookingPartners: [],
      externalPartnerClicks: [],
    };
  }

  private normalizeEntityDetail(detail: RegionalAnalyticsEntityDetail): RegionalAnalyticsEntityDetail {
    return {
      ...detail,
      galleryImages: detail.galleryImages ?? [],
      tags: detail.tags ?? [],
      analytics: detail.analytics ?? this.emptyEntityDetailMetrics(),
      typeSpecific: {
        ...this.emptyTypeSpecific(),
        ...(detail.typeSpecific ?? {}),
        operatingHours: detail.typeSpecific?.operatingHours ?? [],
        amenities: detail.typeSpecific?.amenities ?? [],
        bookingPartners: detail.typeSpecific?.bookingPartners ?? [],
        externalPartnerClicks: detail.typeSpecific?.externalPartnerClicks ?? [],
      },
    };
  }

  private lockBodyScroll(): void {
    if (this.previousBodyOverflow !== null) {
      return;
    }

    this.previousBodyOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
  }

  private unlockBodyScroll(): void {
    if (this.previousBodyOverflow === null) {
      return;
    }

    document.body.style.overflow = this.previousBodyOverflow;
    this.previousBodyOverflow = null;
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

  private loadTagViews(): void {
    if (!this.activeRegionId) {
      this.clearTagViewData();
      return;
    }

    this.tagViewsLoading = true;
    this.tagViewsErrorMessage = null;
    this.notifyStateChanged();

    this.tagViewsSubscription?.unsubscribe();
    this.tagViewsSubscription = this.analyticsService
      .getTagViews(this.currentTagViewsParams())
      .pipe(
        finalize(() => {
          this.tagViewsLoading = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: tagViews => {
          this.tagViewsSig.set(tagViews ?? this.emptyTagViews());
          this.notifyStateChanged();
        },
        error: error => {
          this.tagViewsSig.set(this.emptyTagViews());
          this.tagViewsErrorMessage = this.tagViewsError(error, 'Unable to load tag views for the active region.');
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
          this.destinationRankingSig.set(overview.destinationRanking ?? []);
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

  private currentTagViewsParams() {
    return {
      regionId: this.activeRegionId ?? 0,
      period: this.selectedTagViewsPeriod,
      tagIds: this.selectedTagIdsSig(),
      take: 15,
      entityType: this.entityTypeKey(),
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
    this.destinationRankingSig.set([]);
    this.insightsSig.set([]);
    this.visitorBehaviorSig.set(this.emptyVisitorBehavior());
    this.funnelSig.set({ pageViews: 0, uniqueVisitors: 0, saves: 0, externalClicks: 0, reviews: 0 });
    this.paginationSig.set({ totalCount: 0, skip: 0, take: AnalyticsPageComponent.initialTake, hasMore: false });
    this.visibleCount.set(AnalyticsPageComponent.initialTake);
    this.clearMonthlyVisitData();
    this.clearTagViewData();
  }

  private clearMonthlyVisitData(): void {
    this.monthlyVisitsErrorMessage = null;
    this.monthlyVisitsSig.set(this.emptyMonthlyVisits(this.selectedMonthlyVisitYears));
  }

  private clearTagViewData(): void {
    this.tagViewsErrorMessage = null;
    this.tagViewsSig.set(this.emptyTagViews());
  }

  private resetSelectedTagViews(): void {
    this.selectedTagIdsSig.set([]);
    this.tagSearchSig.set('');
    this.tagSelectorOpen = false;
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

  private emptyTagViews(): RegionalTagViewsAnalyticsResponse {
    return {
      period: this.selectedTagViewsPeriod,
      regionId: this.activeRegionId ?? 0,
      regionDisplayName: this.regionDisplayName,
      totalViews: 0,
      availableTags: [],
      items: [],
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

  private tagViewsError(error: unknown, fallback: string): string {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 401) {
        return 'Sign in with an authenticated Regional Manager account to view tag views.';
      }
      if (error.status === 403) {
        return 'You do not have access to tag views for this assigned region.';
      }
      return error.error?.message ?? fallback;
    }

    return fallback;
  }

  private entityDetailError(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 401) {
        return 'Sign in with an authenticated Regional Manager account to view entity details.';
      }
      if (error.status === 403) {
        return 'You do not have access to this entity in the active assigned region.';
      }
      if (error.status === 404) {
        return 'This entity could not be found.';
      }
      return error.error?.message ?? 'Unable to load entity details.';
    }

    return 'Unable to load entity details.';
  }

  private notifyStateChanged(): void {
    this.cdr.markForCheck();
  }

  private isMonthlyVisitYearsBack(value: number): value is RegionalMonthlyVisitYearsBack {
    return value === 1 || value === 2 || value === 3;
  }

  private isTagViewsPeriod(value: string): value is RegionalTagViewsPeriod {
    return value === '24h' || value === '7d' || value === '1m' || value === '3m' || value === '6m' || value === '1y';
  }

  private setTagSelection(tagId: number, selected: boolean): void {
    const current = this.selectedTagIdsSig();
    const exists = current.includes(tagId);

    if (selected && !exists) {
      this.selectedTagIdsSig.set([...current, tagId]);
      this.loadTagViews();
      return;
    }

    if (!selected && exists) {
      this.selectedTagIdsSig.set(current.filter(id => id !== tagId));
      this.loadTagViews();
    }
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

  private buildTagViewsChart(data: RegionalTagViewsAnalyticsResponse): TagViewsChart {
    const items = data.items ?? [];
    const chartWidth = Math.max(720, items.length * 74 + 76);
    const plotLeft = 54;
    const plotRight = 22;
    const plotTop = 20;
    const plotHeight = 150;
    const plotBottom = plotTop + plotHeight;
    const values = items.map(item => Math.max(0, item.views));
    const maxValue = Math.max(0, ...values);
    const step = this.niceStep(Math.max(1, maxValue));
    const yMax = Math.max(step * 4, 1);
    const plotWidth = chartWidth - plotLeft - plotRight;
    const groupWidth = items.length > 0 ? plotWidth / items.length : plotWidth;
    const barWidth = Math.max(10, Math.min(36, groupWidth * 0.48));
    const color = '#185FA5';

    const bars = items.map((item, index) => {
      const value = Math.max(0, item.views);
      const height = (value / yMax) * plotHeight;
      const groupX = plotLeft + index * groupWidth;
      return {
        x: +(groupX + (groupWidth - barWidth) / 2).toFixed(1),
        y: +(plotBottom - height).toFixed(1),
        width: +barWidth.toFixed(1),
        height: +height.toFixed(1),
        color,
        value,
        tagName: item.tagName,
        percentageOfTotal: item.percentageOfTotal,
      };
    });

    return {
      width: chartWidth,
      bars,
      yLabels: [4, 3, 2, 1, 0].map(multiplier => ({
        y: +(plotBottom - (multiplier / 4) * plotHeight).toFixed(1),
        text: this.fmtK(step * multiplier),
        value: step * multiplier,
      })),
      xLabels: items.map((item, index) => ({
        x: +(plotLeft + index * groupWidth + groupWidth / 2).toFixed(1),
        text: this.truncateTagLabel(item.tagName),
        fullText: item.tagName,
      })),
      isEmpty: items.length === 0 || maxValue === 0,
      totalViews: data.totalViews,
    };
  }

  private buildExternalPartnerClickChart(items: RegionalAnalyticsExternalPartnerClick[]): ExternalPartnerClickChart {
    const normalizedItems = (items ?? [])
      .map((item, index) => ({
        ...item,
        partnerLabel: item.partnerLabel?.trim() || 'Other External Link',
        clickCount: Math.max(0, item.clickCount ?? 0),
        sharePercent: Number.isFinite(item.sharePercent) ? Math.max(0, item.sharePercent) : 0,
        color: this.externalPartnerClickColors[index % this.externalPartnerClickColors.length],
      }))
      .filter(item => item.partnerLabel.length > 0);

    const totalClicks = normalizedItems.reduce((sum, item) => sum + item.clickCount, 0);
    const circumference = 2 * Math.PI * 54;
    let offset = 0;

    const arcs = normalizedItems.map(item => {
      const sharePercent = totalClicks > 0
        ? (item.sharePercent || (item.clickCount / totalClicks) * 100)
        : 0;
      const arcLength = totalClicks > 0 ? (item.clickCount / totalClicks) * circumference : 0;
      const arc = {
        partnerLabel: item.partnerLabel,
        clickCount: item.clickCount,
        sharePercent,
        color: item.color,
        dasharray: `${arcLength.toFixed(1)} ${(circumference - arcLength).toFixed(1)}`,
        dashoffset: `-${offset.toFixed(1)}`,
      };
      offset += arcLength;
      return arc;
    });

    return {
      arcs,
      items: normalizedItems,
      totalClicks,
      hasClickData: totalClicks > 0,
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

  private truncateTagLabel(value: string): string {
    const trimmed = value.trim();
    return trimmed.length > 14 ? `${trimmed.slice(0, 13)}...` : trimmed;
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
