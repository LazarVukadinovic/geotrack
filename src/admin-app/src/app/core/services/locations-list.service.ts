import { Injectable } from '@angular/core';
import { Observable, of } from 'rxjs';
import { delay } from 'rxjs/operators';

export interface LocationListItem {
  id: string;
  name: string;
  summary: string;
  region: string;
  status: 'Published' | 'Draft' | 'Archived';
  lastUpdated: string;
  sortTimestamp: number;
  imageUrl?: string;
  imageBg: string;
  category: string;
  lat?: number;
  lng?: number;
  address?: string;
  zone?: [number, number][];
  description?: string;
  classification?: string[];
  tags?: string[];
  coverImageUrl?: string;
  coverImageName?: string;
  galleryImages?: { url: string; name: string }[];
  parentDestination?: string;
  internalRefId?: string;
  analyticsViews?: number;
  analyticsVisitors?: number;
  analyticsSaves?: number;
  analyticsActions?: number;
  analyticsReviewAvg?: number;
  analyticsReviewCount?: number;
  analyticsTrend?: 'up' | 'stable' | 'down';
}

export type LocationSortField = 'name' | 'lastUpdated' | 'status';
export type LocationSortDir   = 'asc' | 'desc';

export interface LocationsPage {
  items: LocationListItem[];
  total: number;
  page: number;
  pageSize: number;
}

function makeTimestamp(dateStr: string): number {
  return new Date(dateStr).getTime();
}

function todayDisplay(): string {
  return new Date().toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
}

@Injectable({ providedIn: 'root' })
export class LocationsListService {

  private _all: LocationListItem[] = [
    {
      id: '1',  name: 'Oplenac Royal Complex',       summary: 'Historical royal mausoleum and winery of the Karađorđević dynasty.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Oct 24, 2023', sortTimestamp: makeTimestamp('Oct 24, 2023'),
      imageBg: 'url(https://images.unsplash.com/photo-1529420705456-b94baeebe4b5?w=120&q=60) center/cover',
      category: 'Historical', lat: 44.2561, lng: 20.6934, address: 'Oplenac bb, 34310 Topola',
      zone: [[44.27,20.68],[44.27,20.71],[44.24,20.71],[44.24,20.68]],
      analyticsViews: 45200, analyticsVisitors: 32100, analyticsSaves: 8400, analyticsActions: 12500, analyticsReviewAvg: 4.7, analyticsReviewCount: 1200, analyticsTrend: 'up',
    },
    {
      id: '2',  name: 'Kragujevac Memorial Park',     summary: 'Expansive memorial park dedicated to the victims of WWII, featuring sculptures.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Oct 22, 2023', sortTimestamp: makeTimestamp('Oct 22, 2023'),
      imageBg: 'url(https://images.unsplash.com/photo-1500534314209-a25ddb2bd429?w=120&q=60) center/cover',
      category: 'Memorial', lat: 44.0213, lng: 20.8943, address: 'Šumarice, 34000 Kragujevac',
      zone: [[44.025,20.889],[44.027,20.902],[44.020,20.902],[44.0185,20.889]],
      analyticsViews: 22100, analyticsVisitors: 18400, analyticsSaves: 2800, analyticsActions: 4100, analyticsReviewAvg: 3.8, analyticsReviewCount: 340, analyticsTrend: 'down',
    },
    {
      id: '3',  name: 'Bukovička Banja Spa',          summary: 'Famous park and spa resort known for its healing mineral waters.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Oct 20, 2023', sortTimestamp: makeTimestamp('Oct 20, 2023'),
      imageBg: 'url(https://images.unsplash.com/photo-1571019613454-1cb2f99b2d8b?w=120&q=60) center/cover',
      category: 'Spa', lat: 44.3019, lng: 20.5604, address: 'Bukovička Banja bb, 34300 Aranđelovac',
      zone: [[44.32,20.555],[44.32,20.570],[44.29,20.570],[44.29,20.555]],
      analyticsViews: 29200, analyticsVisitors: 22100, analyticsSaves: 5100, analyticsActions: 7600, analyticsReviewAvg: 4.6, analyticsReviewCount: 580, analyticsTrend: 'up',
    },
    {
      id: '4',  name: 'Risovača Cave',                summary: 'Important archaeological and paleontological cave site with illuminated galleries.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Oct 18, 2023', sortTimestamp: makeTimestamp('Oct 18, 2023'),
      imageBg: 'url(https://images.unsplash.com/photo-1504893524553-b855bce32c67?w=120&q=60) center/cover',
      category: 'Cave', lat: 44.3056, lng: 20.5528, address: 'Risovača bb, 34300 Aranđelovac',
      analyticsViews: 17300, analyticsVisitors: 13800, analyticsSaves: 2900, analyticsActions: 4200, analyticsReviewAvg: 4.5, analyticsReviewCount: 310, analyticsTrend: 'stable',
    },
    {
      id: '5',  name: 'Gledić Mountains Reserve',     summary: 'Scenic mountain range offering extensive hiking trails and traditional villages.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Oct 15, 2023', sortTimestamp: makeTimestamp('Oct 15, 2023'),
      imageBg: 'url(https://images.unsplash.com/photo-1470770903676-69b98201ea1c?w=120&q=60) center/cover',
      category: 'Nature', lat: 43.9742, lng: 20.9876, address: 'Gledićke planine, 34000 Kragujevac',
      zone: [[43.99,20.97],[43.99,21.01],[43.96,21.01],[43.96,20.97]],
      analyticsViews: 11400, analyticsVisitors: 9200, analyticsSaves: 1700, analyticsActions: 2600, analyticsReviewAvg: 4.3, analyticsReviewCount: 195, analyticsTrend: 'stable',
    },
    {
      id: '6',  name: 'Šumarice Memorial Park',       summary: 'Memorial park dedicated to victims of the Kragujevac massacre in 1941.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Oct 12, 2023', sortTimestamp: makeTimestamp('Oct 12, 2023'),
      imageBg: 'linear-gradient(135deg,#10b981 0%,#059669 100%)',
      category: 'Memorial', lat: 44.0213, lng: 20.8943, address: 'Park Šumarice bb, 34000 Kragujevac',
      zone: [[44.025,20.889],[44.027,20.902],[44.020,20.902],[44.0185,20.889]],
      analyticsViews: 34500, analyticsVisitors: 26000, analyticsSaves: 5800, analyticsActions: 8900, analyticsReviewAvg: 4.3, analyticsReviewCount: 720, analyticsTrend: 'stable',
    },
    {
      id: '7',  name: 'Bubanj Lake Area',             summary: 'Popular recreational lake area perfect for walks and outdoor activities.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Oct 10, 2023', sortTimestamp: makeTimestamp('Oct 10, 2023'),
      imageBg: 'linear-gradient(135deg,#0ea5e9 0%,#0369a1 100%)',
      category: 'Lake', lat: 44.0169, lng: 20.9258, address: 'Jezero Bubanj bb, 34000 Kragujevac',
      zone: [[44.0185,20.9220],[44.0190,20.9310],[44.0155,20.9315],[44.0148,20.9225]],
      analyticsViews: 19800, analyticsVisitors: 15600, analyticsSaves: 3100, analyticsActions: 4800, analyticsReviewAvg: 4.2, analyticsReviewCount: 410, analyticsTrend: 'up',
    },
    {
      id: '8',  name: 'Kragujevac Old Town',          summary: 'Historic heart of Kragujevac with cultural landmarks and old architecture.',
      region: 'Šumadija, Srbija', status: 'Draft', lastUpdated: 'Oct 8, 2023', sortTimestamp: makeTimestamp('Oct 8, 2023'),
      imageBg: 'linear-gradient(135deg,#f59e0b 0%,#d97706 100%)',
      category: 'Historical', lat: 44.0128, lng: 20.9142, address: 'Staro jezgro, 34000 Kragujevac',
      zone: [[44.0135,20.9090],[44.0140,20.9200],[44.0100,20.9220],[44.0085,20.9130],[44.0110,20.9080]],
      analyticsViews:  8200, analyticsVisitors:  6400, analyticsSaves:  980, analyticsActions: 1450, analyticsReviewAvg: 3.9, analyticsReviewCount:  98, analyticsTrend: 'stable',
    },
    {
      id: '9',  name: 'Veliki Park',                  summary: 'Large public park in central Kragujevac with walking paths.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Oct 5, 2023', sortTimestamp: makeTimestamp('Oct 5, 2023'),
      imageBg: 'linear-gradient(135deg,#8b5cf6 0%,#6d28d9 100%)',
      category: 'Park', lat: 44.0178, lng: 20.9042, address: 'Kragujevačkog oktobra bb, 34000 Kragujevac',
      zone: [[44.0195,20.9010],[44.0200,20.9075],[44.0165,20.9080],[44.0155,20.9015]],
      analyticsViews: 14700, analyticsVisitors: 11900, analyticsSaves: 2200, analyticsActions: 3400, analyticsReviewAvg: 4.4, analyticsReviewCount: 260, analyticsTrend: 'up',
    },
    {
      id: '10', name: 'City Center Kragujevac',       summary: 'Central square and surrounding urban area of Kragujevac.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Oct 3, 2023', sortTimestamp: makeTimestamp('Oct 3, 2023'),
      imageBg: 'linear-gradient(135deg,#e11d48 0%,#be123c 100%)',
      category: 'Urban', lat: 44.0110, lng: 20.9162, address: 'Trg Radomira Putnika, 34000 Kragujevac',
      zone: [[44.0135,20.9090],[44.0140,20.9200],[44.0100,20.9220],[44.0085,20.9130],[44.0110,20.9080]],
      analyticsViews: 38100, analyticsVisitors: 29400, analyticsSaves: 4900, analyticsActions: 7200, analyticsReviewAvg: 4.1, analyticsReviewCount: 680, analyticsTrend: 'stable',
    },
    {
      id: '11', name: 'Rudnik Mountain',              summary: 'Mountain and nature reserve with rich flora and cultural heritage.',
      region: 'Šumadija, Srbija', status: 'Draft', lastUpdated: 'Sep 28, 2023', sortTimestamp: makeTimestamp('Sep 28, 2023'),
      imageBg: 'linear-gradient(135deg,#06b6d4 0%,#0891b2 100%)',
      category: 'Mountain', lat: 44.1150, lng: 20.5230, address: 'Rudnik planina bb, 32252 Rudnik',
      zone: [[44.14,20.49],[44.14,20.54],[44.10,20.54],[44.10,20.49]],
      analyticsViews: 18500, analyticsVisitors: 14200, analyticsSaves: 3200, analyticsActions: 4800, analyticsReviewAvg: 4.5, analyticsReviewCount: 280, analyticsTrend: 'stable',
    },
    {
      id: '12', name: 'Topola Vineyard Zone',         summary: 'Wine region surrounding Topola known for Oplenac wines.',
      region: 'Šumadija, Srbija', status: 'Archived', lastUpdated: 'Sep 20, 2023', sortTimestamp: makeTimestamp('Sep 20, 2023'),
      imageBg: 'linear-gradient(135deg,#f97316 0%,#ea580c 100%)',
      category: 'Vineyard', lat: 44.2520, lng: 20.6820, address: 'Topola vinogorje, 34310 Topola',
      zone: [[44.27,20.67],[44.27,20.70],[44.24,20.71],[44.23,20.68]],
      analyticsViews:  6100, analyticsVisitors:  4800, analyticsSaves:  720, analyticsActions: 1050, analyticsReviewAvg: 4.0, analyticsReviewCount:  82, analyticsTrend: 'down',
    },
    {
      id: '13', name: 'Divčibare Mountain Resort',    summary: 'Maljen mountain resort area with skiing and summer hiking.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Sep 15, 2023', sortTimestamp: makeTimestamp('Sep 15, 2023'),
      imageBg: 'linear-gradient(135deg,#84cc16 0%,#65a30d 100%)',
      category: 'Resort', lat: 44.1340, lng: 20.1120, address: 'Divčibare bb, 32240 Ljig',
      zone: [[44.14,20.08],[44.14,20.13],[44.11,20.13],[44.11,20.08]],
      analyticsViews: 21800, analyticsVisitors: 16900, analyticsSaves: 3800, analyticsActions: 5700, analyticsReviewAvg: 4.4, analyticsReviewCount: 420, analyticsTrend: 'stable',
    },
    {
      id: '14', name: 'Aranđelovac Spa Park',         summary: 'Mineral water spa park and Bukovička Banja complex.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Sep 10, 2023', sortTimestamp: makeTimestamp('Sep 10, 2023'),
      imageBg: 'linear-gradient(135deg,#ec4899 0%,#db2777 100%)',
      category: 'Spa', lat: 44.3019, lng: 20.5604, address: 'Park Bukovička Banja, 34300 Aranđelovac',
      zone: [[44.32,20.555],[44.32,20.570],[44.29,20.570],[44.29,20.555]],
      analyticsViews: 14200, analyticsVisitors: 11100, analyticsSaves: 2600, analyticsActions: 3900, analyticsReviewAvg: 4.7, analyticsReviewCount: 310, analyticsTrend: 'up',
    },
    {
      id: '15', name: 'Gruža Reservoir',              summary: 'Large artificial lake with beaches, fishing, and picnic areas.',
      region: 'Šumadija, Srbija', status: 'Draft', lastUpdated: 'Sep 5, 2023', sortTimestamp: makeTimestamp('Sep 5, 2023'),
      imageBg: 'linear-gradient(135deg,#14b8a6 0%,#0d9488 100%)',
      category: 'Lake', lat: 43.9558, lng: 20.6874, address: 'Akumulacija Gruža, 34208 Knić',
      zone: [[43.96,20.66],[43.96,20.70],[43.91,20.70],[43.91,20.66]],
      analyticsViews:  9700, analyticsVisitors:  7600, analyticsSaves: 1400, analyticsActions: 2100, analyticsReviewAvg: 4.2, analyticsReviewCount: 148, analyticsTrend: 'stable',
    },
    {
      id: '16', name: 'Jasenica Ethnographic Zone',   summary: 'Traditional villages preserving Serbian folk customs and architecture.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Sep 1, 2023', sortTimestamp: makeTimestamp('Sep 1, 2023'),
      imageBg: 'linear-gradient(135deg,#a855f7 0%,#9333ea 100%)',
      category: 'Ethnographic', lat: 44.1850, lng: 20.7340, address: 'Jasenica, 34320 Mladenovac',
      zone: [[44.20,20.72],[44.20,20.77],[44.16,20.77],[44.16,20.72]],
      analyticsViews:  7800, analyticsVisitors:  6100, analyticsSaves: 1100, analyticsActions: 1600, analyticsReviewAvg: 4.1, analyticsReviewCount: 112, analyticsTrend: 'stable',
    },
    {
      id: '17', name: 'Mladenovac Forest Belt',       summary: 'Protected forest belt near Mladenovac with walking and cycling paths.',
      region: 'Šumadija, Srbija', status: 'Archived', lastUpdated: 'Aug 28, 2023', sortTimestamp: makeTimestamp('Aug 28, 2023'),
      imageBg: 'linear-gradient(135deg,#78716c 0%,#57534e 100%)',
      category: 'Forest', lat: 44.4370, lng: 20.6962, address: 'Šumski pojas, 11400 Mladenovac',
      zone: [[44.45,20.67],[44.45,20.72],[44.42,20.72],[44.42,20.67]],
      analyticsViews:  4200, analyticsVisitors:  3300, analyticsSaves:  490, analyticsActions:  720, analyticsReviewAvg: 3.7, analyticsReviewCount:  55, analyticsTrend: 'down',
    },
    {
      id: '18', name: 'Rača River Gorge',             summary: 'Scenic river gorge with rapids, popular for kayaking and fishing.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Aug 20, 2023', sortTimestamp: makeTimestamp('Aug 20, 2023'),
      imageBg: 'linear-gradient(135deg,#2563eb 0%,#1d4ed8 100%)',
      category: 'River', lat: 43.9820, lng: 20.9410, address: 'Klisura reke Rače, 34000 Kragujevac',
      zone: [[43.98,20.55],[43.98,20.60],[43.95,20.60],[43.95,20.55]],
      analyticsViews: 12800, analyticsVisitors: 10100, analyticsSaves: 2100, analyticsActions: 3200, analyticsReviewAvg: 4.6, analyticsReviewCount: 220, analyticsTrend: 'up',
    },
    {
      id: '19', name: 'Opovo Wetlands',               summary: 'Rich wetland ecosystem important for migratory birds and biodiversity.',
      region: 'Šumadija, Srbija', status: 'Draft', lastUpdated: 'Aug 15, 2023', sortTimestamp: makeTimestamp('Aug 15, 2023'),
      imageBg: 'linear-gradient(135deg,#16a34a 0%,#15803d 100%)',
      category: 'Wetland', lat: 44.9860, lng: 20.4380, address: 'Opovačke bare, 26204 Opovo',
      zone: [[45.07,20.41],[45.07,20.46],[45.03,20.46],[45.03,20.41]],
      analyticsViews:  5600, analyticsVisitors:  4400, analyticsSaves:  680, analyticsActions:  980, analyticsReviewAvg: 4.3, analyticsReviewCount:  76, analyticsTrend: 'stable',
    },
    {
      id: '20', name: 'Kladnica Valley',              summary: 'Tranquil valley with medieval monastery and orchard landscapes.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Aug 10, 2023', sortTimestamp: makeTimestamp('Aug 10, 2023'),
      imageBg: 'linear-gradient(135deg,#dc2626 0%,#b91c1c 100%)',
      category: 'Valley', lat: 43.9220, lng: 20.8760, address: 'Dolina Kladnice, 34000 Kragujevac',
      zone: [[44.10,20.61],[44.10,20.66],[44.06,20.66],[44.06,20.61]],
      analyticsViews:  6900, analyticsVisitors:  5400, analyticsSaves:  810, analyticsActions: 1180, analyticsReviewAvg: 4.2, analyticsReviewCount:  91, analyticsTrend: 'stable',
    },
    {
      id: '21', name: 'Kalenić Monastery Surroundings', summary: 'Area surrounding the 15th-century Kalenić Monastery, a UNESCO candidate.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Aug 5, 2023', sortTimestamp: makeTimestamp('Aug 5, 2023'),
      imageBg: 'linear-gradient(135deg,#92400e 0%,#78350f 100%)',
      category: 'Monastery', lat: 43.8960, lng: 20.9540, address: 'Manastir Kalenić bb, 34220 Rekovac',
      zone: [[43.89,20.89],[43.89,20.94],[43.86,20.94],[43.86,20.89]],
      analyticsViews: 10300, analyticsVisitors:  8100, analyticsSaves: 1600, analyticsActions: 2400, analyticsReviewAvg: 4.8, analyticsReviewCount: 168, analyticsTrend: 'up',
    },
    {
      id: '22', name: 'Preljina Industrial Heritage Zone', summary: 'Industrial heritage corridor with former factories and workers\' museums.',
      region: 'Šumadija, Srbija', status: 'Archived', lastUpdated: 'Jul 30, 2023', sortTimestamp: makeTimestamp('Jul 30, 2023'),
      imageBg: 'linear-gradient(135deg,#475569 0%,#334155 100%)',
      category: 'Heritage', lat: 43.8740, lng: 20.4120, address: 'Preljina bb, 32208 Čačak',
      analyticsViews:  3100, analyticsVisitors:  2400, analyticsSaves:  320, analyticsActions:  480, analyticsReviewAvg: 3.5, analyticsReviewCount:  38, analyticsTrend: 'down',
    },
    {
      id: '23', name: 'Vrba Nature Reserve',          summary: 'Inland wetland and forest reserve with protected wildlife corridors.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Jul 25, 2023', sortTimestamp: makeTimestamp('Jul 25, 2023'),
      imageBg: 'linear-gradient(135deg,#4ade80 0%,#22c55e 100%)',
      category: 'Nature', lat: 44.0560, lng: 20.7890, address: 'Rezervat Vrba, 34000 Kragujevac',
      zone: [[44.07,20.77],[44.07,20.81],[44.04,20.81],[44.04,20.77]],
      analyticsViews:  7200, analyticsVisitors:  5700, analyticsSaves:  950, analyticsActions: 1380, analyticsReviewAvg: 4.4, analyticsReviewCount: 103, analyticsTrend: 'stable',
    },
    {
      id: '24', name: 'Stragari Medieval Fort Area',  summary: 'Ruins of medieval fortification overlooking the Šumadija plain.',
      region: 'Šumadija, Srbija', status: 'Draft', lastUpdated: 'Jul 20, 2023', sortTimestamp: makeTimestamp('Jul 20, 2023'),
      imageBg: 'linear-gradient(135deg,#fb923c 0%,#f97316 100%)',
      category: 'Historical', lat: 44.0840, lng: 20.8210, address: 'Stragari tvrđava bb, 34000 Kragujevac',
      zone: [[44.07,20.76],[44.07,20.81],[44.04,20.81],[44.04,20.76]],
      analyticsViews:  5300, analyticsVisitors:  4100, analyticsSaves:  620, analyticsActions:  910, analyticsReviewAvg: 4.1, analyticsReviewCount:  67, analyticsTrend: 'stable',
    },
  ];

  // ── Public accessors ─────────────────────────────────────────────────────

  getAllItems(): LocationListItem[] { return [...this._all]; }

  addItem(item: Omit<LocationListItem, 'sortTimestamp'>): void {
    const ts = Date.now();
    this._all.unshift({ ...item, sortTimestamp: ts });
  }

  updateItem(id: string, patch: Partial<LocationListItem>): void {
    const idx = this._all.findIndex(x => x.id === id);
    if (idx === -1) return;
    const ts = Date.now();
    this._all[idx] = {
      ...this._all[idx],
      ...patch,
      sortTimestamp: ts,
      lastUpdated: todayDisplay(),
    };
  }

  findById(id: string): LocationListItem | undefined {
    return this._all.find(x => x.id === id);
  }

  getPage$(params: {
    page: number; pageSize: number; search: string;
    sortField: LocationSortField; sortDir: LocationSortDir;
  }): Observable<LocationsPage> {
    let list = [...this._all];
    if (params.search.trim()) {
      const q = params.search.trim().toLowerCase();
      list = list.filter(l => l.name.toLowerCase().includes(q) || l.summary.toLowerCase().includes(q) || (l.category ?? '').toLowerCase().includes(q));
    }
    list.sort((a, b) => {
      let cmp = 0;
      if (params.sortField === 'name')        cmp = a.name.localeCompare(b.name);
      if (params.sortField === 'lastUpdated') cmp = a.sortTimestamp - b.sortTimestamp;  // FIX: numeric sort
      if (params.sortField === 'status')      cmp = a.status.localeCompare(b.status);
      return params.sortDir === 'asc' ? cmp : -cmp;
    });
    const total = list.length;
    const start = (params.page - 1) * params.pageSize;
    const items = list.slice(start, start + params.pageSize);
    return of({ items, total, page: params.page, pageSize: params.pageSize }).pipe(delay(120));
  }

  delete$(id: string): Observable<void> {
    const idx = this._all.findIndex(l => l.id === id);
    if (idx !== -1) this._all.splice(idx, 1);
    return of(undefined).pipe(delay(300));
  }

  get total(): number { return this._all.length; }

  private _listState: { searchQuery: string; sortField: string; sortDir: string; displayCount: number; scrollTop: number } | null = null;
  saveListState(s: { searchQuery: string; sortField: string; sortDir: string; displayCount: number; scrollTop: number }): void { this._listState = s; }
  getListState()  { return this._listState; }
}