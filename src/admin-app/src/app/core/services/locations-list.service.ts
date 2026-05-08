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
  imageUrl?: string;
  imageBg: string;
  category: string;
}

export type LocationSortField = 'name' | 'lastUpdated' | 'status';
export type LocationSortDir   = 'asc' | 'desc';

export interface LocationsPage {
  items: LocationListItem[];
  total: number;
  page: number;
  pageSize: number;
}

@Injectable({ providedIn: 'root' })
export class LocationsListService {

  // ─── MOCK DATA ────────────────────────────────────────────────────
  // Replace the body of getAll$() / getPage$() with real HTTP calls.
  private readonly _all: LocationListItem[] = [
    {
      id: '1',
      name: 'Oplenac Royal Complex',
      summary: 'Historical royal mausoleum and winery of the Karađorđević dynasty.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Oct 24, 2023',
      imageBg: 'url(https://images.unsplash.com/photo-1529420705456-b94baeebe4b5?w=120&q=60) center/cover',
      category: 'Location',
    },
    {
      id: '2',
      name: 'Kragujevac Memorial Park',
      summary: 'Expansive memorial park dedicated to the victims of WWII, featuring sculptures.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Oct 22, 2023',
      imageBg: 'url(https://images.unsplash.com/photo-1500534314209-a25ddb2bd429?w=120&q=60) center/cover',
      category: 'Location',
    },
    {
      id: '3',
      name: 'Bukovička Banja Spa',
      summary: 'Famous park and spa resort known for its healing mineral waters.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Oct 20, 2023',
      imageBg: 'url(https://images.unsplash.com/photo-1571019613454-1cb2f99b2d8b?w=120&q=60) center/cover',
      category: 'Location',
    },
    {
      id: '4',
      name: 'Risovača Cave',
      summary: 'Important archaeological and paleontological cave site with illuminated galleries.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Oct 18, 2023',
      imageBg: 'url(https://images.unsplash.com/photo-1504893524553-b855bce32c67?w=120&q=60) center/cover',
      category: 'Location',
    },
    {
      id: '5',
      name: 'Gledić Mountains Reserve',
      summary: 'Scenic mountain range offering extensive hiking trails and traditional villages.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Oct 15, 2023',
      imageBg: 'url(https://images.unsplash.com/photo-1470770903676-69b98201ea1c?w=120&q=60) center/cover',
      category: 'Location',
    },
    {
      id: '6',
      name: 'Šumarice Memorial Park',
      summary: 'Memorial park dedicated to victims of the Kragujevac massacre in 1941.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Oct 12, 2023',
      imageBg: 'linear-gradient(135deg,#10b981 0%,#059669 100%)',
      category: 'Location',
    },
    {
      id: '7',
      name: 'Bubanj Lake Area',
      summary: 'Popular recreational lake area perfect for walks and outdoor activities.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Oct 10, 2023',
      imageBg: 'linear-gradient(135deg,#0ea5e9 0%,#0369a1 100%)',
      category: 'Location',
    },
    {
      id: '8',
      name: 'Kragujevac Old Town',
      summary: 'Historic heart of Kragujevac with cultural landmarks and old architecture.',
      region: 'Šumadija, Srbija',
      status: 'Draft',
      lastUpdated: 'Oct 8, 2023',
      imageBg: 'linear-gradient(135deg,#f59e0b 0%,#d97706 100%)',
      category: 'Location',
    },
    {
      id: '9',
      name: 'Veliki Park',
      summary: 'Large public park in central Kragujevac with walking paths.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Oct 5, 2023',
      imageBg: 'linear-gradient(135deg,#8b5cf6 0%,#6d28d9 100%)',
      category: 'Location',
    },
    {
      id: '10',
      name: 'City Center Kragujevac',
      summary: 'Central square and surrounding urban area of Kragujevac.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Oct 3, 2023',
      imageBg: 'linear-gradient(135deg,#e11d48 0%,#be123c 100%)',
      category: 'Location',
    },
    {
      id: '11',
      name: 'Rudnik Mountain',
      summary: 'Mountain and nature reserve with rich flora and cultural heritage.',
      region: 'Šumadija, Srbija',
      status: 'Draft',
      lastUpdated: 'Sep 28, 2023',
      imageBg: 'linear-gradient(135deg,#06b6d4 0%,#0891b2 100%)',
      category: 'Location',
    },
    {
      id: '12',
      name: 'Topola Vineyard Zone',
      summary: 'Wine region surrounding Topola known for Oplenac wines.',
      region: 'Šumadija, Srbija',
      status: 'Archived',
      lastUpdated: 'Sep 20, 2023',
      imageBg: 'linear-gradient(135deg,#f97316 0%,#ea580c 100%)',
      category: 'Location',
    },
    {
      id: '13',
      name: 'Divčibare Mountain Resort',
      summary: 'Maljen mountain resort area with skiing and summer hiking.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Sep 15, 2023',
      imageBg: 'linear-gradient(135deg,#84cc16 0%,#65a30d 100%)',
      category: 'Location',
    },
    {
      id: '14',
      name: 'Aranđelovac Spa Park',
      summary: 'Mineral water spa park and Bukovička Banja complex.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Sep 10, 2023',
      imageBg: 'linear-gradient(135deg,#ec4899 0%,#db2777 100%)',
      category: 'Location',
    },
    {
      id: '15',
      name: 'Gruža Reservoir',
      summary: 'Large artificial lake with beaches, fishing, and picnic areas.',
      region: 'Šumadija, Srbija',
      status: 'Draft',
      lastUpdated: 'Sep 5, 2023',
      imageBg: 'linear-gradient(135deg,#14b8a6 0%,#0d9488 100%)',
      category: 'Location',
    },
    {
      id: '16',
      name: 'Jasenica Ethnographic Zone',
      summary: 'Traditional villages preserving Serbian folk customs and architecture.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Sep 1, 2023',
      imageBg: 'linear-gradient(135deg,#a855f7 0%,#9333ea 100%)',
      category: 'Location',
    },
    {
      id: '17',
      name: 'Mladenovac Forest Belt',
      summary: 'Protected forest belt near Mladenovac with walking and cycling paths.',
      region: 'Šumadija, Srbija',
      status: 'Archived',
      lastUpdated: 'Aug 28, 2023',
      imageBg: 'linear-gradient(135deg,#78716c 0%,#57534e 100%)',
      category: 'Location',
    },
    {
      id: '18',
      name: 'Rača River Gorge',
      summary: 'Scenic river gorge with rapids, popular for kayaking and fishing.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Aug 20, 2023',
      imageBg: 'linear-gradient(135deg,#2563eb 0%,#1d4ed8 100%)',
      category: 'Location',
    },
    {
      id: '19',
      name: 'Opovo Wetlands',
      summary: 'Rich wetland ecosystem important for migratory birds and biodiversity.',
      region: 'Šumadija, Srbija',
      status: 'Draft',
      lastUpdated: 'Aug 15, 2023',
      imageBg: 'linear-gradient(135deg,#16a34a 0%,#15803d 100%)',
      category: 'Location',
    },
    {
      id: '20',
      name: 'Kladnica Valley',
      summary: 'Tranquil valley with medieval monastery and orchard landscapes.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Aug 10, 2023',
      imageBg: 'linear-gradient(135deg,#dc2626 0%,#b91c1c 100%)',
      category: 'Location',
    },
    {
      id: '21',
      name: 'Kalenić Monastery Surroundings',
      summary: 'Area surrounding the 15th-century Kalenić Monastery, a UNESCO candidate.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Aug 5, 2023',
      imageBg: 'linear-gradient(135deg,#92400e 0%,#78350f 100%)',
      category: 'Location',
    },
    {
      id: '22',
      name: 'Preljina Industrial Heritage Zone',
      summary: 'Industrial heritage corridor with former factories and workers\' museums.',
      region: 'Šumadija, Srbija',
      status: 'Archived',
      lastUpdated: 'Jul 30, 2023',
      imageBg: 'linear-gradient(135deg,#475569 0%,#334155 100%)',
      category: 'Location',
    },
    {
      id: '23',
      name: 'Vrba Nature Reserve',
      summary: 'Inland wetland and forest reserve with protected wildlife corridors.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Jul 25, 2023',
      imageBg: 'linear-gradient(135deg,#4ade80 0%,#22c55e 100%)',
      category: 'Location',
    },
    {
      id: '24',
      name: 'Stragari Medieval Fort Area',
      summary: 'Ruins of medieval fortification overlooking the Šumadija plain.',
      region: 'Šumadija, Srbija',
      status: 'Draft',
      lastUpdated: 'Jul 20, 2023',
      imageBg: 'linear-gradient(135deg,#fb923c 0%,#f97316 100%)',
      category: 'Location',
    },
  ];

  // ─── PUBLIC API ────────────────────────────────────────────────────

  /** Paginated + filtered slice. Simulates async backend call. */
  getPage$(params: {
    page: number;
    pageSize: number;
    search: string;
    sortField: LocationSortField;
    sortDir: LocationSortDir;
  }): Observable<LocationsPage> {
    let list = [...this._all];

    // Filter
    if (params.search.trim()) {
      const q = params.search.trim().toLowerCase();
      list = list.filter(
        l => l.name.toLowerCase().includes(q) || l.summary.toLowerCase().includes(q),
      );
    }

    // Sort
    list.sort((a, b) => {
      let cmp = 0;
      if (params.sortField === 'name')        cmp = a.name.localeCompare(b.name);
      if (params.sortField === 'lastUpdated') cmp = a.lastUpdated.localeCompare(b.lastUpdated);
      if (params.sortField === 'status')      cmp = a.status.localeCompare(b.status);
      return params.sortDir === 'asc' ? cmp : -cmp;
    });

    const total = list.length;
    const start = (params.page - 1) * params.pageSize;
    const items = list.slice(start, start + params.pageSize);

    // Simulate network latency
    return of({ items, total, page: params.page, pageSize: params.pageSize }).pipe(delay(120));
  }

  delete$(id: string): Observable<void> {
    // MOCK → return this.http.delete<void>(`/api/locations/${id}`);
    const idx = this._all.findIndex(l => l.id === id);
    if (idx !== -1) this._all.splice(idx, 1);
    return of(undefined).pipe(delay(300));
  }

  get total(): number { return this._all.length; }
}