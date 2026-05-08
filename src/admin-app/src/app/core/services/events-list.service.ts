import { Injectable } from '@angular/core';
import { Observable, of } from 'rxjs';
import { delay } from 'rxjs/operators';

export interface EventListItem {
  id: string;
  name: string;
  summary: string;
  region: string;
  status: 'Published' | 'Draft' | 'Archived';
  lastUpdated: string;
  imageBg: string;
  category: string;
  dateRange?: string;
}

export type EventSortField = 'name' | 'lastUpdated' | 'status';
export type EventSortDir   = 'asc' | 'desc';

export interface EventsPage {
  items: EventListItem[];
  total: number;
  page: number;
  pageSize: number;
}

@Injectable({ providedIn: 'root' })
export class EventsListService {

  private readonly _all: EventListItem[] = [
    {
      id: 'e1',
      name: 'Šumadija Wine Festival',
      summary: 'Annual celebration of Oplenac wine heritage with tastings, cellar tours and live music.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Oct 24, 2023',
      imageBg: 'url(https://images.unsplash.com/photo-1510812431401-41d2bd2722f3?w=120&q=60) center/cover',
      category: 'Festival',
      dateRange: 'Nov 10–12, 2023',
    },
    {
      id: 'e2',
      name: 'Kragujevac Jazz Weekend',
      summary: 'Three-day outdoor jazz festival in the city centre featuring domestic and international artists.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Oct 22, 2023',
      imageBg: 'url(https://images.unsplash.com/photo-1514320291840-2e0a9bf2a9ae?w=120&q=60) center/cover',
      category: 'Concert',
      dateRange: 'Nov 17–19, 2023',
    },
    {
      id: 'e3',
      name: 'Rudnik Trail Marathon',
      summary: 'Mountain trail running race across Rudnik with 21 km and 42 km distance categories.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Oct 20, 2023',
      imageBg: 'url(https://images.unsplash.com/photo-1530549387789-4c1017266635?w=120&q=60) center/cover',
      category: 'Sports',
      dateRange: 'Nov 25, 2023',
    },
    {
      id: 'e4',
      name: 'Oplenac Harvest Days',
      summary: 'Traditional grape harvest festival with folk costumes, folklore performances and grape stomping.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Oct 18, 2023',
      imageBg: 'url(https://images.unsplash.com/photo-1506905925346-21bda4d32df4?w=120&q=60) center/cover',
      category: 'Festival',
      dateRange: 'Oct 5–7, 2023',
    },
    {
      id: 'e5',
      name: 'Medieval Stragari Reenactment',
      summary: 'Living history event at the Stragari fortress ruins with knight combat and archery competitions.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Oct 15, 2023',
      imageBg: 'url(https://images.unsplash.com/photo-1548267882-23a4a7f3ab77?w=120&q=60) center/cover',
      category: 'Exhibition',
      dateRange: 'Dec 2–3, 2023',
    },
    {
      id: 'e6',
      name: 'Šumarice Commemoration Ceremony',
      summary: 'Annual state ceremony commemorating the victims of the 1941 Kragujevac massacre.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Oct 12, 2023',
      imageBg: 'linear-gradient(135deg,#475569 0%,#334155 100%)',
      category: 'Exhibition',
      dateRange: 'Oct 21, 2023',
    },
    {
      id: 'e7',
      name: 'Bubanj Lake Kayak Cup',
      summary: 'Regional kayak and SUP competition on Bubanj lake open to amateur and professional paddlers.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Oct 10, 2023',
      imageBg: 'linear-gradient(135deg,#0ea5e9 0%,#0369a1 100%)',
      category: 'Sports',
      dateRange: 'Nov 4–5, 2023',
    },
    {
      id: 'e8',
      name: 'Jasenica Folk Craft Market',
      summary: 'Artisan market showcasing traditional Šumadija crafts: pottery, embroidery and woodcarving.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Oct 8, 2023',
      imageBg: 'linear-gradient(135deg,#f59e0b 0%,#d97706 100%)',
      category: 'Market',
      dateRange: 'Dec 9–10, 2023',
    },
    {
      id: 'e9',
      name: 'Divčibare Winter Opening',
      summary: 'Ski season opening ceremony with freestyle ski show, DJ sets and après-ski party.',
      region: 'Šumadija, Srbija',
      status: 'Draft',
      lastUpdated: 'Oct 5, 2023',
      imageBg: 'linear-gradient(135deg,#84cc16 0%,#65a30d 100%)',
      category: 'Festival',
      dateRange: 'Dec 15, 2023',
    },
    {
      id: 'e10',
      name: 'Photography Workshop – Rudnik',
      summary: 'Two-day landscape photography workshop led by professional photographers on Rudnik mountain.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Oct 3, 2023',
      imageBg: 'linear-gradient(135deg,#8b5cf6 0%,#6d28d9 100%)',
      category: 'Workshop',
      dateRange: 'Nov 11–12, 2023',
    },
    {
      id: 'e11',
      name: 'Šumadija Food & Rakija Fair',
      summary: 'Regional fair celebrating local gastronomy with tastings of traditional spirits and dishes.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Sep 28, 2023',
      imageBg: 'linear-gradient(135deg,#f97316 0%,#ea580c 100%)',
      category: 'Market',
      dateRange: 'Dec 1–3, 2023',
    },
    {
      id: 'e12',
      name: 'Kalenić Monastery Night Concert',
      summary: "Sacred music candlelight concert held in the monastery courtyard on the saint's day.",
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Sep 20, 2023',
      imageBg: 'linear-gradient(135deg,#92400e 0%,#78350f 100%)',
      category: 'Concert',
      dateRange: 'Nov 30, 2023',
    },
    {
      id: 'e13',
      name: 'Gruža Fishing Championship',
      summary: 'Open fishing competition on Gruža reservoir with prizes for largest catch and junior categories.',
      region: 'Šumadija, Srbija',
      status: 'Archived',
      lastUpdated: 'Sep 15, 2023',
      imageBg: 'linear-gradient(135deg,#14b8a6 0%,#0d9488 100%)',
      category: 'Sports',
      dateRange: 'Sep 30, 2023',
    },
    {
      id: 'e14',
      name: 'Heritage Ceramics Exhibition',
      summary: 'Exhibition of archaeological pottery finds from across Šumadija at the Narodni Muzej.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Sep 10, 2023',
      imageBg: 'linear-gradient(135deg,#e11d48 0%,#be123c 100%)',
      category: 'Exhibition',
      dateRange: 'Nov 1–30, 2023',
    },
    {
      id: 'e15',
      name: 'Rača River Rafting Day',
      summary: 'Guided white-water rafting descent of the Rača gorge suitable for beginners and families.',
      region: 'Šumadija, Srbija',
      status: 'Draft',
      lastUpdated: 'Sep 5, 2023',
      imageBg: 'linear-gradient(135deg,#2563eb 0%,#1d4ed8 100%)',
      category: 'Sports',
      dateRange: 'Dec 23, 2023',
    },
    {
      id: 'e16',
      name: 'Christmas Market Kragujevac',
      summary: 'Traditional Christmas market in the city centre with crafts, food stalls and ice skating rink.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Aug 28, 2023',
      imageBg: 'linear-gradient(135deg,#06b6d4 0%,#0891b2 100%)',
      category: 'Market',
      dateRange: 'Dec 20–Jan 7, 2024',
    },
    {
      id: 'e17',
      name: 'Birdwatching Tour – Opovo Wetlands',
      summary: 'Expert-guided birdwatching excursion to the Opovo wetlands during peak migratory season.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Aug 20, 2023',
      imageBg: 'linear-gradient(135deg,#16a34a 0%,#15803d 100%)',
      category: 'Workshop',
      dateRange: 'Nov 18, 2023',
    },
    {
      id: 'e18',
      name: 'Šumadija Half Marathon',
      summary: 'City road race through Kragujevac with 10 km fun run and 21 km competitive categories.',
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Aug 15, 2023',
      imageBg: 'linear-gradient(135deg,#dc2626 0%,#b91c1c 100%)',
      category: 'Sports',
      dateRange: 'Nov 26, 2023',
    },
    {
      id: 'e19',
      name: 'Traditional Slava Workshop',
      summary: 'Interactive cultural workshop teaching the rituals and culinary traditions of the Serbian Slava.',
      region: 'Šumadija, Srbija',
      status: 'Draft',
      lastUpdated: 'Aug 10, 2023',
      imageBg: 'linear-gradient(135deg,#a855f7 0%,#9333ea 100%)',
      category: 'Workshop',
      dateRange: 'Dec 19, 2023',
    },
    {
      id: 'e20',
      name: "New Year's Eve at Veliki Park",
      summary: "Outdoor New Year's Eve concert and countdown celebration with fireworks in the city park.",
      region: 'Šumadija, Srbija',
      status: 'Published',
      lastUpdated: 'Aug 5, 2023',
      imageBg: 'linear-gradient(135deg,#f59e0b 0%,#b45309 100%)',
      category: 'Concert',
      dateRange: 'Dec 31, 2023',
    },
  ];

  getPage$(params: {
    page: number;
    pageSize: number;
    search: string;
    sortField: EventSortField;
    sortDir: EventSortDir;
  }): Observable<EventsPage> {
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