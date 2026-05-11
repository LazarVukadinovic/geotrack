import { Component, OnInit, OnDestroy, ChangeDetectionStrategy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { SidebarComponent } from '../../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../../shared/components/topbar/topbar.component';
import { AuthService } from '../../../../../core/services/auth.service';
export interface RegionAnalyticRow {
  name: string;
  refId: string;
  pageViews: string;
  visitors: string;
  saves: string;
  avgRating: number;
  entities: string;
  status: 'Active / Fast' | 'Active / Normal' | 'Processing Delays' | 'Inactive';
  qualityScore: number;
}

export interface NationalKpi {
  label: string;
  value: string;
  change: string;
  up: boolean;
  icon: 'eye' | 'users' | 'link' | 'star' | 'bookmark' | 'chat';
}

@Component({
  selector: 'app-national-analytics',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './national-analytics.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NationalAnalyticsComponent implements OnInit, OnDestroy {

  constructor(
    private router: Router,
    private cdr: ChangeDetectorRef,
    private auth: AuthService,
  ) {}

  get userName(): string { return this.auth.fullName ?? 'Sarah Jenkins'; }
  get userRole():  string { return this.auth.getUser()?.role ?? 'National Manager'; }
  get region():    string { return this.auth.getUser()?.region ?? 'Republika Srbija'; }

  // ── Filters ──────────────────────────────────────────────────
  selectedPeriod  = 'Last 6 Months';
  selectedRegion  = 'All Regions';
  selectedEntity  = 'All';
  selectedStatus  = 'Active';
  viewMode: 'standard' | 'compare' = 'standard';

  periodOptions  = ['Last 7 Days', 'Last 30 Days', 'Last 6 Months', 'Last 12 Months', 'Custom'];
  regionOptions  = ['All Regions', 'Šumadija', 'Banat', 'Raška', 'Pomoravlje', 'Mačva', 'Podunavlje'];
  entityOptions  = ['All', 'Destinations', 'Locations', 'Places', 'Events'];
  statusOptions  = ['Active', 'Inactive', 'All'];

  // ── KPIs ─────────────────────────────────────────────────────
  kpis: NationalKpi[] = [
    { label: 'Total Page Views',      value: '2.4M',  change: '+12.5%', up: true,  icon: 'eye'      },
    { label: 'Unique Visitors',        value: '1.1M',  change: '+8.2%',  up: true,  icon: 'users'    },
    { label: 'External Link Clicks',   value: '320K',  change: '+15.3%', up: true,  icon: 'link'     },
    { label: 'Total Saves / Favorites',value: '450K',  change: '+5.1%',  up: true,  icon: 'bookmark' },
    { label: 'Total Reviews',          value: '120K',  change: '0%',     up: true,  icon: 'chat'     },
    { label: 'Average Rating',         value: '4.5/5', change: '+0.2',   up: true,  icon: 'star'     },
  ];

  // ── Top Region ───────────────────────────────────────────────
  topRegion = { name: 'Šumadija', score: 94, note: 'Leading in overall engagement and unique visitors this quarter.' };
  fastestGrowing = { name: 'Šumadija', change: '+24% visitor growth' };

  topEntities = [
    { name: 'Yellowstone NP',   sub: 'Destination • Pacific NW', rank: 1, icon: 'destination' },
    { name: 'Summer Solstice',  sub: 'Event • Northeast',         rank: 1, icon: 'event'       },
    { name: 'Grand Canyon',     sub: 'Destination • Southwest',  rank: 2, icon: 'destination' },
  ];

  // ── Chart data
  pageViewsData = [
    { label: 'Šumadija',    color: '#2563eb', points: [30,42,38,55,48,62,58,74,68,82,79,92] },
    { label: 'Banat',       color: '#16a34a', points: [20,28,25,35,32,40,38,48,44,55,52,60] },
    { label: 'Pomoravlje',  color: '#d97706', points: [15,20,18,24,22,30,28,36,33,42,40,48] },
  ];

  topDestinations = [
    { name: 'Đavolja Varoš',   region: 'Jug i Istok',  views: 98 },
    { name: 'Tara',             region: 'Šumadija',     views: 87 },
    { name: 'Fruška Gora',      region: 'Vojvodina',    views: 74 },
    { name: 'Uvac kanjon',      region: 'Raška',        views: 61 },
    { name: 'Zlatibor',         region: 'Zlatiborski',  views: 58 },
  ];

  topEvents = [
    { name: 'Exit Festival',      region: 'Vojvodina',  saves: 42 },
    { name: 'Guča Sabor',         region: 'Šumadija',   saves: 38 },
    { name: 'Nišville Jazz',      region: 'Nišavski',   saves: 29 },
    { name: 'Oplenac Harvest',    region: 'Šumadija',   saves: 24 },
    { name: 'Belgrade Beer Fest', region: 'Beograd',    saves: 21 },
  ];

  contentQuality = [
    { region: 'Šumadija',   reviews: 450, avgRating: 4.8 },
    { region: 'Banat',      reviews: 320, avgRating: 4.5 },
    { region: 'Raška',      reviews: 580, avgRating: 4.7 },
    { region: 'Pomoravlje', reviews: 250, avgRating: 4.0 },
    { region: 'Mačva',      reviews: 290, avgRating: 4.2 },
    { region: 'Podunavlje', reviews: 180, avgRating: 4.3 },
  ];

  // ── Regional Performance Table
  allRows: RegionAnalyticRow[] = [
    { name: 'Šumadija',   refId: 'REG-001', pageViews: '1.2M', visitors: '450K', saves: '85K', avgRating: 4.8, entities: '45 / 315 / 18', status: 'Active / Fast',       qualityScore: 92 },
    { name: 'Banat',      refId: 'REG-002', pageViews: '850K', visitors: '320K', saves: '62K', avgRating: 4.5, entities: '28 / 190 / 5',  status: 'Processing Delays',   qualityScore: 78 },
    { name: 'Raška',      refId: 'REG-003', pageViews: '1.5M', visitors: '580K', saves: '110K',avgRating: 4.7, entities: '65 / 480 / 32', status: 'Active / Normal',     qualityScore: 88 },
    { name: 'Pomoravlje', refId: 'REG-004', pageViews: '650K', visitors: '250K', saves: '45K', avgRating: 4.0, entities: '40 / 275 / 21', status: 'Processing Delays',   qualityScore: 71 },
    { name: 'Mačva',      refId: 'REG-005', pageViews: '720K', visitors: '290K', saves: '51K', avgRating: 4.2, entities: '35 / 210 / 8',  status: 'Active / Fast',       qualityScore: 81 },
    { name: 'Podunavlje', refId: 'REG-006', pageViews: '540K', visitors: '210K', saves: '38K', avgRating: 4.3, entities: '22 / 160 / 6',  status: 'Active / Normal',     qualityScore: 75 },
    { name: 'Bačka',      refId: 'REG-007', pageViews: '480K', visitors: '185K', saves: '31K', avgRating: 3.9, entities: '19 / 140 / 4',  status: 'Inactive',            qualityScore: 62 },
  ];

  tablePageSize = 5;
  tablePage = 1;
  visibleColumns: string[] = ['pageViews', 'visitors', 'saves', 'avgRating', 'entities', 'status', 'qualityScore'];
  showColumnPicker = false;

  get tableRows(): RegionAnalyticRow[] {
    const start = (this.tablePage - 1) * this.tablePageSize;
    return this.allRows.slice(start, start + this.tablePageSize);
  }
  get totalTablePages(): number { return Math.ceil(this.allRows.length / this.tablePageSize); }
  get hasNextPage(): boolean { return this.tablePage < this.totalTablePages; }
  get hasPrevPage(): boolean { return this.tablePage > 1; }

  goToPage(p: number): void { this.tablePage = p; this.cdr.markForCheck(); }

  nextPage(): void { if (this.hasNextPage) { this.tablePage++; this.cdr.markForCheck(); } }
  prevPage(): void { if (this.hasPrevPage) { this.tablePage--; this.cdr.markForCheck(); } }

  statusClass(status: string): string {
    if (status.startsWith('Active')) return 'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200';
    if (status === 'Processing Delays') return 'bg-red-50 text-red-600 ring-1 ring-red-200';
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
      pageViews: 'Page Views', visitors: 'Visitors', saves: 'Saves',
      avgRating: 'Avg Rating', entities: 'Entities (D/P/E)',
      status: 'Status', qualityScore: 'Quality Score',
    };
    return map[col] ?? col;
  }

  isColumnVisible(col: string): boolean { return this.visibleColumns.includes(col); }

  toggleColumn(col: string): void {
    if (this.visibleColumns.includes(col)) {
      this.visibleColumns = this.visibleColumns.filter(c => c !== col);
    } else {
      this.visibleColumns = [...this.visibleColumns, col];
    }
    this.cdr.markForCheck();
  }

  exportReport(): void { }

  ngOnInit(): void {}
  ngOnDestroy(): void {}
}