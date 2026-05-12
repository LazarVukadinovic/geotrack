import { Component, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SidebarComponent } from '../../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../../shared/components/topbar/topbar.component';
export interface MetricCard {
  label: string;
  value: string;
  delta: number;
  deltaLabel: string;
  icon: string;
}

export interface CategoryAggregate {
  name: string;
  count: number;
  visits: number;
  maxVisits: number;
  color: string;
}

export interface EntityRow {
  name: string;
  type: 'PLACE' | 'DESTINATION' | 'EVENT' | 'HOTEL';
  location: string;
  views: number;
  visitors: number;
  saves: number;
  actions: number;
  reviewMin: number;
  reviewAvg: number;
  reviewMax: number;
  reviewCount: number;
  trend: 'up' | 'stable' | 'down';
  imageBg: string;
}

export interface AutoInsight {
  type: 'success' | 'warning' | 'danger' | 'info';
  title: string;
  detail: string;
}

export interface ActionableInsight {
  icon: string;
  label: string;
  title: string;
  subtitle: string;
  highlight?: string;
  highlightClass?: string;
}

export type CategoryFilter = 'all' | 'hotels' | 'destinations' | 'events' | 'places';

@Component({
  selector: 'app-analytics-page',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './analytics-page.component.html',
})
export class AnalyticsPageComponent {

  protected Math = Math;

  region   = 'Šumadija, Srbija';
  userName = 'Sarah Jenkins';
  userRole = 'Regional Manager';

  // ── Date range ──────────────────────────────────────────────────────
  dateFrom = '2026-04-01';
  dateTo   = '2026-05-09';

  // Search & filters
  searchQuery = '';
  selectedType = 'All Entities';
  selectedCategory = 'Any';
  selectedStatus = 'Active';

  typeOptions = ['All Entities', 'Place', 'Destination', 'Event', 'Hotel'];
  categoryOptions = ['Any', 'Hoteli', 'Destinacije', 'Događaji', 'Mesta'];
  statusOptions = ['Active', 'Inactive', 'Pending'];

  // ── Active category filter ──────────────────────────────────────────
  activeFilter = signal<CategoryFilter>('all');

  filters: { key: CategoryFilter; label: string }[] = [
    { key: 'all',          label: 'Sve kategorije' },
    { key: 'hotels',       label: 'Hoteli'         },
    { key: 'destinations', label: 'Destinacije'    },
    { key: 'events',       label: 'Događaji'       },
    { key: 'places',       label: 'Mesta'          },
  ];

  setFilter(f: CategoryFilter): void {
    this.activeFilter.set(f);
  }

  catToFilter(name: string): CategoryFilter {
    const n = name.toLowerCase();
    if (n === 'hoteli')       return 'hotels';
    if (n === 'destinacije')  return 'destinations';
    if (n.includes('aji'))    return 'events';
    return 'places';
  }

  // ── KPI metrics ─────────────────────────────────────────────────────
  private readonly allMetrics: MetricCard[] = [
    { label: 'Total Page Views',        value: '248.5K', delta:  12.5, deltaLabel: 'vs last month', icon: 'trend'    },
    { label: 'Unique Visitors',         value: '184.2K', delta:   8.2, deltaLabel: 'vs last month', icon: 'users'    },
    { label: 'Total Saves / Favorites', value: '32.8K',  delta:  15.4, deltaLabel: 'vs last month', icon: 'bookmark' },
    { label: 'Average Rating',          value: '4.6',    delta:   0.5, deltaLabel: 'vs last month', icon: 'star'     },
    { label: 'Ext. Link Clicks',        value: '18.9K',  delta:  -2.1, deltaLabel: 'vs last month', icon: 'link'     },
    { label: 'Total Reviews',           value: '4,821',  delta:   5.8, deltaLabel: 'vs last month', icon: 'chat'     },
  ];

  private readonly metricsByFilter: Record<CategoryFilter, MetricCard[]> = {
    all: this.allMetrics,
    hotels: [
      { label: 'Page Views – Hotels',    value: '112K',  delta:  9.4, deltaLabel: 'vs prethodni', icon: 'trend'    },
      { label: 'Visitors – Hotels',      value: '84.1K', delta:  7.1, deltaLabel: 'vs prethodni', icon: 'users'    },
      { label: 'Saves – Hotels',         value: '14.2K', delta: 11.2, deltaLabel: 'vs prethodni', icon: 'bookmark' },
      { label: 'Average Rating',         value: '4.4',   delta:  0.3, deltaLabel: 'vs prethodni', icon: 'star'     },
      { label: 'Ext. Clicks – Hotels',   value: '6.8K',  delta: -1.5, deltaLabel: 'vs prethodni', icon: 'link'     },
      { label: 'Reviews – Hotels',       value: '1,840', delta:  4.2, deltaLabel: 'vs prethodni', icon: 'chat'     },
    ],
    destinations: [
      { label: 'Page Views – Dest.',     value: '78K',   delta: 14.2, deltaLabel: 'vs prethodni', icon: 'trend'    },
      { label: 'Visitors – Dest.',       value: '56.3K', delta:  9.8, deltaLabel: 'vs prethodni', icon: 'users'    },
      { label: 'Saves – Dest.',          value: '9.6K',  delta: 18.1, deltaLabel: 'vs prethodni', icon: 'bookmark' },
      { label: 'Average Rating',         value: '4.7',   delta:  0.8, deltaLabel: 'vs prethodni', icon: 'star'     },
      { label: 'Ext. Clicks – Dest.',    value: '5.2K',  delta:  3.1, deltaLabel: 'vs prethodni', icon: 'link'     },
      { label: 'Reviews – Dest.',        value: '1,230', delta:  7.6, deltaLabel: 'vs prethodni', icon: 'chat'     },
    ],
    events: [
      { label: 'Page Views – Events',    value: '22K',   delta: 21.3, deltaLabel: 'vs prethodni', icon: 'trend'    },
      { label: 'Visitors – Events',      value: '18.9K', delta: 15.4, deltaLabel: 'vs prethodni', icon: 'users'    },
      { label: 'Saves – Events',         value: '4.1K',  delta: 22.0, deltaLabel: 'vs prethodni', icon: 'bookmark' },
      { label: 'Average Rating',         value: '4.8',   delta:  1.2, deltaLabel: 'vs prethodni', icon: 'star'     },
      { label: 'Ext. Clicks – Events',   value: '3.1K',  delta:  8.4, deltaLabel: 'vs prethodni', icon: 'link'     },
      { label: 'Reviews – Events',       value: '610',   delta: 12.3, deltaLabel: 'vs prethodni', icon: 'chat'     },
    ],
    places: [
      { label: 'Page Views – Places',    value: '61K',   delta:  6.8, deltaLabel: 'vs prethodni', icon: 'trend'    },
      { label: 'Visitors – Places',      value: '43.2K', delta:  4.5, deltaLabel: 'vs prethodni', icon: 'users'    },
      { label: 'Saves – Places',         value: '8.2K',  delta:  9.3, deltaLabel: 'vs prethodni', icon: 'bookmark' },
      { label: 'Average Rating',         value: '4.5',   delta: -0.2, deltaLabel: 'vs prethodni', icon: 'star'     },
      { label: 'Ext. Clicks – Places',   value: '3.8K',  delta: -4.2, deltaLabel: 'vs prethodni', icon: 'link'     },
      { label: 'Reviews – Places',       value: '1,141', delta:  2.1, deltaLabel: 'vs prethodni', icon: 'chat'     },
    ],
  };

  filteredMetrics = computed(() => this.metricsByFilter[this.activeFilter()]);

  // ── Category aggregates ─────────────────────────────────────────────
  categories: CategoryAggregate[] = [
    { name: 'Hoteli',       count: 14, visits: 112000, maxVisits: 112000, color: '#1a4db5' },
    { name: 'Destinacije',  count:  6, visits:  78000, maxVisits: 112000, color: '#1e6bb8' },
    { name: 'Mesta',        count: 22, visits:  61000, maxVisits: 112000, color: '#2d8dd6' },
    { name: 'Događaji',     count:  8, visits:  22000, maxVisits: 112000, color: '#5ab0e8' },
  ];

  barWidth(cat: CategoryAggregate): number {
    return Math.round((cat.visits / cat.maxVisits) * 100);
  }

  formatVisits(v: number): string {
    return v >= 1000 ? (v / 1000).toFixed(0) + 'K' : String(v);
  }

  // ── Seasonal heatmap ────────────────────────────────────────────────
  months = ['J', 'F', 'M', 'A', 'M', 'J', 'J', 'A', 'S', 'O', 'N', 'D'];
  monthIntensity = [2, 2, 4, 6, 8, 9, 10, 10, 8, 6, 2, 1];

  private readonly heatPalette = [
    '#EBF3FC', '#D1E5F7', '#AAD0F0', '#7DB8E8',
    '#4E9BD4', '#2E7EC0', '#185FA5', '#1252A0',
    '#0F4F9C', '#0B3E82', '#082F68', '#052150',
  ];

  heatmapBg(intensity: number): string {
    return this.heatPalette[Math.min(intensity, this.heatPalette.length - 1)];
  }

  heatmapFg(intensity: number): string {
    return intensity >= 5 ? '#EBF3FC' : '#1a2f6b';
  }

  // ── Entity rows ─────────────────────────────────────────────────────
  entityRows: EntityRow[] = [
    { name: 'Oplenac Complex',      type: 'PLACE',       location: 'Šumadija › Topola',      views: 45200, visitors: 32100, saves: 8400, actions: 12500, reviewMin: 3.2, reviewAvg: 4.7, reviewMax: 5.0, reviewCount: 1200, trend: 'up',     imageBg: '#C7D9F5' },
    { name: 'Šumadija Wine Route',  type: 'DESTINATION', location: 'Šumadija',               views: 38900, visitors: 29500, saves: 6200, actions:  9800, reviewMin: 2.8, reviewAvg: 4.5, reviewMax: 5.0, reviewCount:  890, trend: 'stable', imageBg: '#D4EAD4' },
    { name: 'Oplenac Harvest Fest', type: 'EVENT',       location: 'Šumadija › Topola',      views: 15400, visitors: 11200, saves: 3100, actions:  5200, reviewMin: 4.1, reviewAvg: 4.8, reviewMax: 5.0, reviewCount:  410, trend: 'up',     imageBg: '#F5E6C7' },
    { name: 'Kosmaj Monument',      type: 'PLACE',       location: 'Šumadija › Mladenovac',  views: 22100, visitors: 18400, saves: 2800, actions:  4100, reviewMin: 1.9, reviewAvg: 3.8, reviewMax: 4.8, reviewCount:  340, trend: 'down',   imageBg: '#E8D5C7' },
    { name: 'Arandjelovac Spa',     type: 'PLACE',       location: 'Šumadija › Aranđelovac', views: 31000, visitors: 24200, saves: 4500, actions:  6300, reviewMin: 3.5, reviewAvg: 4.4, reviewMax: 5.0, reviewCount:  620, trend: 'up',     imageBg: '#C7E8F5' },
  ];

  filteredRows = computed(() => {
    const f = this.activeFilter();
    if (f === 'all') return this.entityRows;
    const typeMap: Record<CategoryFilter, EntityRow['type'][]> = {
      all:          [],
      hotels:       ['HOTEL'],
      destinations: ['DESTINATION'],
      events:       ['EVENT'],
      places:       ['PLACE'],
    };
    return this.entityRows.filter(r => typeMap[f].includes(r.type));
  });

  formatNum(n: number): string {
    return n >= 1000 ? (n / 1000).toFixed(1) + 'K' : String(n);
  }

  visibleCount = 5;
  showMore(): void { this.visibleCount += 5; }
  get tableHasMore(): boolean { return this.visibleCount < this.filteredRows().length; }

  trendLabel(t: EntityRow['trend']): string {
    return t === 'up' ? 'Trending' : t === 'down' ? 'Low Engagement' : 'Stable';
  }

  trendClass(t: EntityRow['trend']): string {
    const map: Record<EntityRow['trend'], string> = {
      up:     'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200',
      down:   'bg-red-50 text-red-600 ring-1 ring-red-200',
      stable: 'bg-gray-100 text-gray-600 ring-1 ring-gray-200',
    };
    return map[t];
  }

  reviewMinClass(val: number): string {
    if (val < 2.5) return 'text-red-600 font-semibold';
    if (val < 3.5) return 'text-amber-600';
    return 'text-gray-500';
  }

  reviewAvgClass(val: number): string {
    if (val >= 4.5) return 'text-emerald-700 font-semibold';
    if (val >= 3.5) return 'text-gray-800 font-semibold';
    return 'text-red-600 font-semibold';
  }

  typeClass(t: EntityRow['type']): string {
    const map: Record<EntityRow['type'], string> = {
      PLACE:       'bg-violet-100 text-violet-700',
      DESTINATION: 'bg-blue-100 text-blue-700',
      EVENT:       'bg-emerald-100 text-emerald-700',
      HOTEL:       'bg-amber-100 text-amber-700',
    };
    return map[t];
  }

  typeLabel(t: EntityRow['type']): string {
    const map: Record<EntityRow['type'], string> = {
      PLACE: 'Place', DESTINATION: 'Destination', EVENT: 'Event', HOTEL: 'Hotel',
    };
    return map[t];
  }

  // ── Actionable Insights (right sidebar panel) ───────────────────────
  actionableInsights: ActionableInsight[] = [
    {
      icon: 'pin',
      label: 'TOP VIEWED DESTINATION',
      title: 'Šumadija Wine Route',
      subtitle: '38.9K Views (+15%)',
      highlightClass: 'text-blue-600',
    },
    {
      icon: 'bookmark',
      label: 'MOST SAVED PLACE',
      title: 'Oplenac Complex',
      subtitle: '8.4K Saves (High Intent)',
      highlightClass: 'text-gray-700',
    },
    {
      icon: 'calendar',
      label: 'HIGHEST PERFORMING EVENT',
      title: 'Oplenac Harvest Fest',
      subtitle: '15.4K Views (Trending)',
      highlightClass: 'text-gray-700',
    },
    {
      icon: 'warning',
      label: 'ACTION REQUIRED',
      title: 'Kosmaj Monument',
      subtitle: '-12% Engagement Rate',
      highlightClass: 'text-red-600',
    },
  ];

  // ── Auto insights ────────────────────────────────────────────────────
  insights: AutoInsight[] = [
    {
      type: 'success',
      title: 'Hotels — Weekend Effect +40%',
      detail: 'Hotels record 40% more visits on weekends than weekdays. Recommended: increase promotional capacity during the week.',
    },
    {
      type: 'warning',
      title: 'Winter Drop is a Structural Issue',
      detail: 'Visit drop Nov–Jan correlates with closure of 4 hotels and absence of winter content — not low interest. Aranđelovac Spa operating year-round shows stable traffic.',
    },
    {
      type: 'danger',
      title: 'Kosmaj Monument Requires Attention',
      detail: 'Average rating 3.8 with 22K views — visitors come but leave unsatisfied. Recommended: review reviews to identify recurring complaints.',
    },
    {
      type: 'info',
      title: 'Aranđelovac Spa — Fastest Growing in Region',
      detail: '+24% unique visitors in 7 days. The same content model can be applied to other facilities as best practice.',
    },
  ];

  insightIconBg(type: AutoInsight['type']): string {
    const map: Record<AutoInsight['type'], string> = {
      success: '#E1F5EE', warning: '#FAEEDA', danger: '#FCEBEB', info: '#E6F1FB',
    };
    return map[type];
  }

  insightIconColor(type: AutoInsight['type']): string {
    const map: Record<AutoInsight['type'], string> = {
      success: '#0F6E56', warning: '#854F0B', danger: '#A32D2D', info: '#185FA5',
    };
    return map[type];
  }

  insightIcon(type: AutoInsight['type']): string {
    const map: Record<AutoInsight['type'], string> = {
      success: '↑', warning: '❄', danger: '⚠', info: '★',
    };
    return map[type];
  }

  exportData(): void {
    console.log('Export CSV');
  }
  getEntityImage(name: string): string {
    const map: Record<string, string> = {
      'Oplenac Complex': 'https://source.unsplash.com/100x100/?church',
      'Avala Tower': 'https://source.unsplash.com/100x100/?tower',
      'KosmajMonument': 'https://source.unsplash.com/100x100/?monument',
      'Sumadija WineRoute': 'https://source.unsplash.com/100x100/?vineyard',
      'Arandjelovac Spa': 'https://source.unsplash.com/100x100/?spa',
    };

    return map[name] || 'https://source.unsplash.com/100x100/?nature';
  }
}