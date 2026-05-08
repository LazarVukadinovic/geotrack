import { Injectable } from '@angular/core';
import { Observable, of } from 'rxjs';
import { delay } from 'rxjs/operators';

export interface PlaceListItem {
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

export type PlaceSortField = 'name' | 'lastUpdated' | 'status';
export type PlaceSortDir   = 'asc' | 'desc';

export interface PlacesPage {
  items: PlaceListItem[];
  total: number;
  page: number;
  pageSize: number;
}

@Injectable({ providedIn: 'root' })
export class PlacesListService {

  private readonly _all: PlaceListItem[] = [
    {
      id: 'p1',
      name: 'Narodni Muzej Kragujevac',
      summary: 'City museum housing archaeological finds, fine art and regional history collections.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Oct 24, 2023',
      imageBg: 'url(https://images.unsplash.com/photo-1554907984-15263bfd63bd?w=120&q=60) center/cover',
      category: 'Museum',
    },
    {
      id: 'p2',
      name: 'Crkva Svetog Đorđa – Oplenac',
      summary: 'Iconic royal mausoleum church covered entirely in mosaic reproductions of Serbian frescoes.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Oct 22, 2023',
      imageBg: 'url(https://images.unsplash.com/photo-1529420705456-b94baeebe4b5?w=120&q=60) center/cover',
      category: 'Landmark',
    },
    {
      id: 'p3',
      name: 'Hotel Šumarice',
      summary: 'Four-star hotel adjacent to the Šumarice memorial park with conference facilities.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Oct 20, 2023',
      imageBg: 'url(https://images.unsplash.com/photo-1566073771259-6a8506099945?w=120&q=60) center/cover',
      category: 'Hotel',
    },
    {
      id: 'p4',
      name: 'Restoran Stara Srbija',
      summary: 'Traditional Serbian cuisine restaurant in a rustic interior with live folk music on weekends.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Oct 18, 2023',
      imageBg: 'url(https://images.unsplash.com/photo-1414235077428-338989a2e8c0?w=120&q=60) center/cover',
      category: 'Restaurant',
    },
    {
      id: 'p5',
      name: 'Zavod za zaštitu prirode – Rudnik',
      summary: 'Nature conservation visitor centre at the foot of Rudnik mountain with educational exhibits.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Oct 15, 2023',
      imageBg: 'url(https://images.unsplash.com/photo-1470770903676-69b98201ea1c?w=120&q=60) center/cover',
      category: 'Park',
    },
    {
      id: 'p6',
      name: 'Spomen-park Kragujevački oktobar',
      summary: 'Open-air memorial with sculptures commemorating the 1941 Kragujevac massacre victims.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Oct 12, 2023',
      imageBg: 'linear-gradient(135deg,#10b981 0%,#059669 100%)',
      category: 'Landmark',
    },
    {
      id: 'p7',
      name: 'Vinarija Topola',
      summary: 'Estate winery producing Oplenac wines with cellar tours and tasting events.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Oct 10, 2023',
      imageBg: 'linear-gradient(135deg,#f97316 0%,#ea580c 100%)',
      category: 'Landmark',
    },
    {
      id: 'p8',
      name: 'Banja Bukovička – Izvor',
      summary: 'Historic mineral spring pavilion in Aranđelovac parkland, open to visitors year-round.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Oct 8, 2023',
      imageBg: 'linear-gradient(135deg,#0ea5e9 0%,#0369a1 100%)',
      category: 'Park',
    },
    {
      id: 'p9',
      name: 'Kafana "Kod Miloša"',
      summary: 'Iconic old-town kafana serving roštilj and domestic wines since 1934.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Oct 5, 2023',
      imageBg: 'linear-gradient(135deg,#f59e0b 0%,#d97706 100%)',
      category: 'Restaurant',
    },
    {
      id: 'p10',
      name: 'Risovača Cave Entrance',
      summary: 'Illuminated cave entrance complex with a paleontology exhibition and guided tours.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Oct 3, 2023',
      imageBg: 'url(https://images.unsplash.com/photo-1504893524553-b855bce32c67?w=120&q=60) center/cover',
      category: 'Landmark',
    },
    {
      id: 'p11',
      name: 'Hotel Kalenić',
      summary: 'Boutique hotel near the monastery with organic breakfast and wine pairing packages.',
      region: 'Šumadija, Srbija',
      status: 'Draft',
      lastUpdated: 'Sep 28, 2023',
      imageBg: 'linear-gradient(135deg,#ec4899 0%,#db2777 100%)',
      category: 'Hotel',
    },
    {
      id: 'p12',
      name: 'Etnografski muzej Jasenica',
      summary: 'Open-air ethnographic museum with reconstructed 19th-century farmsteads and craft workshops.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Sep 20, 2023',
      imageBg: 'linear-gradient(135deg,#a855f7 0%,#9333ea 100%)',
      category: 'Museum',
    },
    {
      id: 'p13',
      name: 'Aqua Park Šumadija',
      summary: 'Outdoor water park on the Gruža reservoir shore, open June to September.',
      region: 'Šumadija, Srbija',
      status: 'Archived',
      lastUpdated: 'Sep 15, 2023',
      imageBg: 'linear-gradient(135deg,#14b8a6 0%,#0d9488 100%)',
      category: 'Park',
    },
    {
      id: 'p14',
      name: 'Galerija Risto Stijović',
      summary: 'Municipal gallery dedicated to the sculptor Risto Stijović with rotating modern art shows.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Sep 10, 2023',
      imageBg: 'linear-gradient(135deg,#8b5cf6 0%,#6d28d9 100%)',
      category: 'Museum',
    },
    {
      id: 'p15',
      name: 'Planinska kuća Divčibare',
      summary: 'Mountain lodge at 1000m elevation offering ski rental, guided hikes and weekend packages.',
      region: 'Šumadija, Srbija',
      status: 'Draft',
      lastUpdated: 'Sep 5, 2023',
      imageBg: 'linear-gradient(135deg,#84cc16 0%,#65a30d 100%)',
      category: 'Hotel',
    },
    {
      id: 'p16',
      name: 'Manastir Kalenić',
      summary: 'Serbian Orthodox monastery built in 1413, renowned for the finest preserved medieval frescoes.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Aug 28, 2023',
      imageBg: 'linear-gradient(135deg,#92400e 0%,#78350f 100%)',
      category: 'Landmark',
    },
    {
      id: 'p17',
      name: 'Restoran Rača',
      summary: 'Riverside restaurant specialising in freshwater fish dishes and local Šumadija brandy.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Aug 20, 2023',
      imageBg: 'linear-gradient(135deg,#2563eb 0%,#1d4ed8 100%)',
      category: 'Restaurant',
    },
    {
      id: 'p18',
      name: 'Turistički info centar Kragujevac',
      summary: 'Regional tourism office providing maps, guided tour bookings and accommodation assistance.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Aug 15, 2023',
      imageBg: 'linear-gradient(135deg,#e11d48 0%,#be123c 100%)',
      category: 'Landmark',
    },
    {
      id: 'p19',
      name: 'Sportski centar Bubanj',
      summary: 'Lakeside sports complex with tennis courts, cycling rental and open-air fitness areas.',
      region: 'Šumadija, Srbija',
      status: 'Draft',
      lastUpdated: 'Aug 10, 2023',
      imageBg: 'linear-gradient(135deg,#06b6d4 0%,#0891b2 100%)',
      category: 'Park',
    },
    {
      id: 'p20',
      name: 'Stari Grad Kragujevac',
      summary: 'Historic core featuring the Old Palace, Arsenal museum and 19th-century merchant quarter.',
      region: 'Šumadija, Srbija',
      status: 'Archived',
      lastUpdated: 'Aug 5, 2023',
      imageBg: 'linear-gradient(135deg,#475569 0%,#334155 100%)',
      category: 'Landmark',
    },
  ];

  getPage$(params: {
    page: number;
    pageSize: number;
    search: string;
    sortField: PlaceSortField;
    sortDir: PlaceSortDir;
  }): Observable<PlacesPage> {
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