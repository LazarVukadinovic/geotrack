import { Injectable } from '@angular/core';
import { Observable, of } from 'rxjs';
import { delay } from 'rxjs/operators';

export interface DestinationListItem {
  id: string;
  name: string;
  summary: string;
  region: string;
  status: 'Published' | 'Draft' | 'Archived';
  lastUpdated: string;
  imageBg: string;
  category: string;
  lat?: number;
  lng?: number;
  address?: string;
}

export type DestinationSortField = 'name' | 'lastUpdated' | 'status';
export type DestinationSortDir   = 'asc' | 'desc';

export interface DestinationsPage {
  items: DestinationListItem[];
  total: number;
  page: number;
  pageSize: number;
}

@Injectable({ providedIn: 'root' })
export class DestinationsListService {

  private readonly _all: DestinationListItem[] = [
    {
      id: 'd1',
      name: 'Šumadija Heartland',
      summary: 'The geographic and cultural core of central Serbia, known for vineyards and medieval heritage.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Oct 24, 2023',
      imageBg: 'url(https://images.unsplash.com/photo-1506905925346-21bda4d32df4?w=120&q=60) center/cover',
      category: 'Destination',
    },
    {
      id: 'd2',
      name: 'Kragujevac Urban Zone',
      summary: 'The largest city in Šumadija, blending industrial heritage with cultural institutions.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Oct 22, 2023',
      imageBg: 'url(https://images.unsplash.com/photo-1477959858617-67f85cf4f1df?w=120&q=60) center/cover',
      category: 'Destination',
    },
    {
      id: 'd3',
      name: 'Topola & Oplenac Region',
      summary: 'Royal Serbian dynasty homeland with the famous Oplenac mausoleum and wine tradition.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Oct 20, 2023',
      imageBg: 'url(https://images.unsplash.com/photo-1529420705456-b94baeebe4b5?w=120&q=60) center/cover',
      category: 'Destination',
    },
    {
      id: 'd4',
      name: 'Aranđelovac Spa Territory',
      summary: 'Mineral spring resort area centered around Bukovička Banja with therapeutic waters.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Oct 18, 2023',
      imageBg: 'url(https://images.unsplash.com/photo-1571019613454-1cb2f99b2d8b?w=120&q=60) center/cover',
      category: 'Destination',
    },
    {
      id: 'd5',
      name: 'Rudnik Mountain Area',
      summary: 'Ancient mining mountain with medieval monasteries, hiking routes and forest landscapes.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Oct 15, 2023',
      imageBg: 'url(https://images.unsplash.com/photo-1470770903676-69b98201ea1c?w=120&q=60) center/cover',
      category: 'Destination',
    },
    {
      id: 'd6',
      name: 'Gruža Lake Corridor',
      summary: 'Recreational corridor along the Gruža reservoir with fishing, beaches and village tourism.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Oct 12, 2023',
      imageBg: 'linear-gradient(135deg,#0ea5e9 0%,#0369a1 100%)',
      category: 'Destination',
    },
    {
      id: 'd7',
      name: 'Jasenica Ethnographic Zone',
      summary: 'A belt of traditional Serbian villages preserving folk architecture and rural customs.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Oct 10, 2023',
      imageBg: 'linear-gradient(135deg,#a855f7 0%,#9333ea 100%)',
      category: 'Destination',
    },
    {
      id: 'd8',
      name: 'Gledić Mountains Reserve',
      summary: 'Undeveloped highland reserve with traditional shepherding communities and endemic flora.',
      region: 'Šumadija, Srbija',
      status: 'Draft',
      lastUpdated: 'Oct 8, 2023',
      imageBg: 'linear-gradient(135deg,#10b981 0%,#059669 100%)',
      category: 'Destination',
    },
    {
      id: 'd9',
      name: 'Rača River Valley',
      summary: 'Scenic gorge destination popular for kayaking, fly-fishing and riverside camping.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Oct 5, 2023',
      imageBg: 'linear-gradient(135deg,#2563eb 0%,#1d4ed8 100%)',
      category: 'Destination',
    },
    {
      id: 'd10',
      name: 'Kalenić Monastery Heritage Zone',
      summary: 'UNESCO candidate area surrounding the 15th-century Kalenić Monastery complex.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Oct 3, 2023',
      imageBg: 'linear-gradient(135deg,#92400e 0%,#78350f 100%)',
      category: 'Destination',
    },
    {
      id: 'd11',
      name: 'Divčibare Highland Resort',
      summary: 'Mountain resort on Maljen peak with winter skiing and summer wellness tourism.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Sep 28, 2023',
      imageBg: 'linear-gradient(135deg,#84cc16 0%,#65a30d 100%)',
      category: 'Destination',
    },
    {
      id: 'd12',
      name: 'Mladenovac Green Belt',
      summary: 'Protected periurban forest zone with cycling paths and weekend retreat facilities.',
      region: 'Šumadija, Srbija',
      status: 'Draft',
      lastUpdated: 'Sep 20, 2023',
      imageBg: 'linear-gradient(135deg,#78716c 0%,#57534e 100%)',
      category: 'Destination',
    },
    {
      id: 'd13',
      name: 'Opovo Wetland Sanctuary',
      summary: 'Protected wetland ecosystem critical for migratory waterbirds and biodiversity research.',
      region: 'Šumadija, Srbija',
      status: 'Archived',
      lastUpdated: 'Sep 15, 2023',
      imageBg: 'linear-gradient(135deg,#16a34a 0%,#15803d 100%)',
      category: 'Destination',
    },
    {
      id: 'd14',
      name: 'Kladnica Valley Retreat',
      summary: 'Quiet agritourism valley with medieval monastery ruins and traditional orchard farms.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Sep 10, 2023',
      imageBg: 'linear-gradient(135deg,#dc2626 0%,#b91c1c 100%)',
      category: 'Destination',
    },
    {
      id: 'd15',
      name: 'Stragari Medieval Corridor',
      summary: 'Archaeological trail connecting medieval fortification ruins across the Šumadija plain.',
      region: 'Šumadija, Srbija',
      status: 'Draft',
      lastUpdated: 'Sep 5, 2023',
      imageBg: 'linear-gradient(135deg,#fb923c 0%,#f97316 100%)',
      category: 'Destination',
    },
  ];

  getPage$(params: {
    page: number;
    pageSize: number;
    search: string;
    sortField: DestinationSortField;
    sortDir: DestinationSortDir;
  }): Observable<DestinationsPage> {
    let list = [...this._all];

    if (params.search.trim()) {
      const q = params.search.trim().toLowerCase();
      list = list.filter(
        l => l.name.toLowerCase().includes(q) || l.summary.toLowerCase().includes(q),
      );
    }

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

    return of({ items, total, page: params.page, pageSize: params.pageSize }).pipe(delay(120));
  }

  delete$(id: string): Observable<void> {
    const idx = this._all.findIndex(l => l.id === id);
    if (idx !== -1) this._all.splice(idx, 1);
    return of(undefined).pipe(delay(300));
  }

  get total(): number { return this._all.length; }
}