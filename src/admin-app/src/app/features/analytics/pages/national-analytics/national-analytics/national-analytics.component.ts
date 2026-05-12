import { Component, OnInit, OnDestroy, ChangeDetectionStrategy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { SidebarComponent } from '../../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../../shared/components/topbar/topbar.component';
import { AuthService } from '../../../../../core/services/auth.service';
import { NationalAnalyticsService } from '../../../../../core/services/national/national-analytics.service';
import { NationalKpi, TopRegionHighlight, FastestGrowingRegion, TopEntityItem, PageViewSeries, TopDestination, TopEvent, ContentQualityPoint, RegionAnalyticRow, RegionStatus, OperationalAlert } from '../../../../../core/models/national-analytics.model';

@Component({
  selector: 'app-national-analytics',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './national-analytics.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NationalAnalyticsComponent implements OnInit, OnDestroy {

  private readonly destroy$ = new Subject<void>();

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
  selectedRegion = 'All Regions';
  selectedEntity = 'All';
  selectedStatus = 'Active';
  viewMode: 'standard' | 'compare' = 'standard';

  readonly periodOptions = ['Last 7 Days', 'Last 30 Days', 'Last 6 Months', 'Last 12 Months', 'Custom'];
  readonly regionOptions = ['All Regions', 'Šumadija', 'Banat', 'Raška', 'Pomoravlje', 'Mačva', 'Podunavlje'];
  readonly entityOptions = ['All', 'Destinations', 'Locations', 'Places', 'Events'];
  readonly statusOptions = ['Active', 'Inactive', 'All'];

  // ── Data from service ────────────────────────────────────────────────────
  kpis:            NationalKpi[]         = [];
  topRegion:       TopRegionHighlight    = { name: '', score: 0, note: '' };
  fastestGrowing:  FastestGrowingRegion  = { name: '', change: '' };
  topEntities:     TopEntityItem[]       = [];
  pageViewsData:   PageViewSeries[]      = [];
  topDestinations: TopDestination[]      = [];
  topEvents:       TopEvent[]            = [];
  contentQuality:  ContentQualityPoint[] = [];
  allRows:         RegionAnalyticRow[]   = [];
  alerts:          OperationalAlert[]    = [];

  // ── Table state ──────────────────────────────────────────────────────────
  tablePageSize = 5;
  tablePage     = 1;
  visibleColumns: string[] = ['pageViews', 'visitors', 'saves', 'avgRating', 'entities', 'status', 'qualityScore', 'pendingRequests', 'hasManager'];
  showColumnPicker = false;

  sortCol = '';
  sortDir: 'asc' | 'desc' = 'asc';

  get totalPending(): number         { return this.allRows.reduce((s, r) => s + r.pendingRequests, 0); }
  get regionsWithoutManager(): number { return this.allRows.filter(r => !r.hasManager).length; }
  get regionsWithDelays(): number    { return this.allRows.filter(r => r.status === 'Processing Delays').length; }

  get tableRows(): RegionAnalyticRow[] {
    const start = (this.tablePage - 1) * this.tablePageSize;
    return this.allRows.slice(start, start + this.tablePageSize);
  }
  get totalTablePages(): number { return Math.ceil(this.allRows.length / this.tablePageSize); }
  get hasNextPage(): boolean    { return this.tablePage < this.totalTablePages; }
  get hasPrevPage(): boolean    { return this.tablePage > 1; }

  goToPage(p: number): void { this.tablePage = p; this.cdr.markForCheck(); }
  nextPage(): void { if (this.hasNextPage) { this.tablePage++; this.cdr.markForCheck(); } }
  prevPage(): void { if (this.hasPrevPage) { this.tablePage--; this.cdr.markForCheck(); } }

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
        this.contentQuality  = data.contentQuality;
        this.allRows         = data.regionRows;
        this.alerts          = data.alerts;
        this.isLoading       = false;
        this.cdr.markForCheck();
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  // ── Helpers ──────────────────────────────────────────────────────────────
  statusClass(status: RegionStatus): string {
    if (status.startsWith('Active'))        return 'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200';
    if (status === 'Processing Delays')     return 'bg-red-50 text-red-600 ring-1 ring-red-200';
    return 'bg-gray-100 text-gray-500 ring-1 ring-gray-200';
  }

  scoreColor(score: number): string {
    if (score >= 85) return '#2563eb';
    if (score >= 70) return '#d97706';
    return '#dc2626';
  }

  toggleColumnPicker(e: Event): void {
    e.stopPropagation();
    this.showColumnPicker = !this.showColumnPicker;
    this.cdr.markForCheck();
  }

  columnLabel(col: string): string {
    const map: Record<string, string> = {
      pageViews:       'Page Views',
      visitors:        'Visitors',
      saves:           'Saves',
      avgRating:       'Avg Rating',
      entities:        'Entities (D/P/E)',
      status:          'Status',
      qualityScore:    'Quality Score',
      pendingRequests: 'Pending',
      hasManager:      'Manager',
    };
    return map[col] ?? col;
  }

  sortBy(col: string): void {
    if (this.sortCol === col) {
      this.sortDir = this.sortDir === 'asc' ? 'desc' : 'asc';
    } else {
      this.sortCol = col;
      this.sortDir = 'asc';
    }
    this.allRows = [...this.allRows].sort((a, b) => {
      const mult = this.sortDir === 'asc' ? 1 : -1;
      const aVal = (a as unknown as Record<string, unknown>)[col];
      const bVal = (b as unknown as Record<string, unknown>)[col];
      if (typeof aVal === 'number' && typeof bVal === 'number') return (aVal - bVal) * mult;
      if (typeof aVal === 'boolean' && typeof bVal === 'boolean') return (Number(aVal) - Number(bVal)) * mult;
      return String(aVal).localeCompare(String(bVal)) * mult;
    });
    this.tablePage = 1;
    this.cdr.markForCheck();
  }

  alertIcon(type: OperationalAlert['type']): string {
    if (type === 'no-manager')       return '#dc2626';
    if (type === 'processing-delay') return '#d97706';
    return '#d97706';
  }

  isColumnVisible(col: string): boolean { return this.visibleColumns.includes(col); }

  toggleColumn(col: string): void {
    this.visibleColumns = this.visibleColumns.includes(col)
      ? this.visibleColumns.filter(c => c !== col)
      : [...this.visibleColumns, col];
    this.cdr.markForCheck();
  }

  exportReport(): void {
  }
}