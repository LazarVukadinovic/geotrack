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
  sortTimestamp: number;
  imageBg: string;
  category: string;
  dateRange?: string;
  startDate?: string;
  endDate?: string;
  lat?: number;
  lng?: number;
  address?: string;
  description?: string;
  venueName?: string;
}

export type EventSortField = 'name' | 'lastUpdated' | 'status';
export type EventSortDir   = 'asc' | 'desc';

export interface EventsPage {
  items: EventListItem[];
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
export class EventsListService {

  private _all: EventListItem[] = [
    {
      id: 'e1', name: 'Šumadija Wine Festival',
      summary: 'Annual celebration of Oplenac wine heritage with tastings, cellar tours and live music.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Oct 24, 2023', sortTimestamp: makeTimestamp('Oct 24, 2023'),
      imageBg: 'url(https://images.unsplash.com/photo-1510812431401-41d2bd2722f3?w=120&q=60) center/cover',
      category: 'Festival', dateRange: 'Nov 10–12, 2023', startDate: 'Nov 10, 2023', endDate: 'Nov 12, 2023',
      lat: 44.2548, lng: 20.6912, address: 'Vinska 5, 34310 Topola',
      venueName: 'Oplenac Winery',
      description: 'The Šumadija Wine Festival is held annually in the vineyards surrounding Topola. Visitors can join guided cellar tours, meet local winemakers, attend grape-blending workshops, and enjoy live folk and jazz music across three evenings.',
    },
    {
      id: 'e2', name: 'Kragujevac Jazz Weekend',
      summary: 'Three-day outdoor jazz festival in the city centre featuring domestic and international artists.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Oct 22, 2023', sortTimestamp: makeTimestamp('Oct 22, 2023'),
      imageBg: 'url(https://images.unsplash.com/photo-1514320291840-2e0a9bf2a9ae?w=120&q=60) center/cover',
      category: 'Concert', dateRange: 'Nov 17–19, 2023', startDate: 'Nov 17, 2023', endDate: 'Nov 19, 2023',
      lat: 44.0110, lng: 20.9162, address: 'Trg Radomira Putnika, 34000 Kragujevac',
      venueName: 'Trg Radomira Putnika',
      description: 'Kragujevac Jazz Weekend transforms the central square into an open-air concert venue for three nights. The 2023 edition features over 12 acts across two stages, with free entry to all performances.',
    },
    {
      id: 'e3', name: 'Rudnik Trail Marathon',
      summary: 'Mountain trail running race across Rudnik with 21 km and 42 km distance categories.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Oct 20, 2023', sortTimestamp: makeTimestamp('Oct 20, 2023'),
      imageBg: 'url(https://images.unsplash.com/photo-1530549387789-4c1017266635?w=120&q=60) center/cover',
      category: 'Sports', dateRange: 'Nov 25, 2023', startDate: 'Nov 25, 2023', endDate: 'Nov 25, 2023',
      lat: 44.1150, lng: 20.5230, address: 'Rudnik planina bb, 32252 Rudnik',
      venueName: 'Rudnik Mountain',
      description: 'The Rudnik Trail Marathon takes runners through ancient beech forests, past medieval monastery ruins, and over the 1,132 m summit. Categories include a 21 km semi-marathon and a 42 km full marathon. Registration closes two weeks before the event.',
    },
    {
      id: 'e4', name: 'Oplenac Harvest Days',
      summary: 'Traditional grape harvest festival with folk costumes, folklore performances and grape stomping.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Oct 18, 2023', sortTimestamp: makeTimestamp('Oct 18, 2023'),
      imageBg: 'url(https://images.unsplash.com/photo-1506905925346-21bda4d32df4?w=120&q=60) center/cover',
      category: 'Festival', dateRange: 'Oct 5–7, 2023', startDate: 'Oct 5, 2023', endDate: 'Oct 7, 2023',
      lat: 44.2561, lng: 20.6934, address: 'Oplenac bb, 34310 Topola',
      venueName: 'Oplenac Royal Complex',
      description: 'Oplenac Harvest Days celebrates the grape harvest with traditional Serbian folk costumes, kolo dancing, grape stomping competitions, and local food stalls. The festival takes place on the slopes of Oplenac Hill beneath the Church of Saint George.',
    },
    {
      id: 'e5', name: 'Medieval Stragari Reenactment',
      summary: 'Living history event at the Stragari fortress ruins with knight combat and archery competitions.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Oct 15, 2023', sortTimestamp: makeTimestamp('Oct 15, 2023'),
      imageBg: 'url(https://images.unsplash.com/photo-1548267882-23a4a7f3ab77?w=120&q=60) center/cover',
      category: 'Exhibition', dateRange: 'Dec 2–3, 2023', startDate: 'Dec 2, 2023', endDate: 'Dec 3, 2023',
      lat: 44.0840, lng: 20.8210, address: 'Stragari tvrđava bb, 34000 Kragujevac',
      venueName: 'Stragari Fortress Ruins',
      description: 'Historical reenactment groups from Serbia, Hungary and Bulgaria gather at the Stragari fortress site to recreate 14th-century Balkan life. Events include armoured combat tournaments, archery, blacksmithing demonstrations, and a medieval market.',
    },
    {
      id: 'e6', name: 'Šumarice Commemoration Ceremony',
      summary: 'Annual state ceremony commemorating the victims of the 1941 Kragujevac massacre.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Oct 12, 2023', sortTimestamp: makeTimestamp('Oct 12, 2023'),
      imageBg: 'linear-gradient(135deg,#475569 0%,#334155 100%)',
      category: 'Exhibition', dateRange: 'Oct 21, 2023', startDate: 'Oct 21, 2023', endDate: 'Oct 21, 2023',
      lat: 44.0213, lng: 20.8943, address: 'Park Šumarice bb, 34000 Kragujevac',
      venueName: 'Šumarice Memorial Park',
      description: 'On October 21, 1941, German forces executed approximately 2,800 Kragujevac citizens, including an entire high-school class. The annual state commemoration brings together dignitaries, survivors\' families and international guests for a solemn wreath-laying ceremony at the memorial.',
    },
    {
      id: 'e7', name: 'Bubanj Lake Kayak Cup',
      summary: 'Regional kayak and SUP competition on Bubanj lake open to amateur and professional paddlers.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Oct 10, 2023', sortTimestamp: makeTimestamp('Oct 10, 2023'),
      imageBg: 'linear-gradient(135deg,#0ea5e9 0%,#0369a1 100%)',
      category: 'Sports', dateRange: 'Nov 4–5, 2023', startDate: 'Nov 4, 2023', endDate: 'Nov 5, 2023',
      lat: 44.0169, lng: 20.9258, address: 'Jezero Bubanj bb, 34000 Kragujevac',
      venueName: 'Bubanj Lake Area',
      description: 'The Bubanj Lake Kayak Cup attracts over 150 paddlers from across Serbia and neighbouring countries. Disciplines include 200 m sprint, 1,000 m endurance, and stand-up paddleboard relay. The event includes a youth category for paddlers under 16.',
    },
    {
      id: 'e8', name: 'Jasenica Folk Craft Market',
      summary: 'Artisan market showcasing traditional Šumadija crafts: pottery, embroidery and woodcarving.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Oct 8, 2023', sortTimestamp: makeTimestamp('Oct 8, 2023'),
      imageBg: 'linear-gradient(135deg,#f59e0b 0%,#d97706 100%)',
      category: 'Market', dateRange: 'Dec 9–10, 2023', startDate: 'Dec 9, 2023', endDate: 'Dec 10, 2023',
      lat: 44.1862, lng: 20.7318, address: 'Jasenica bb, 34320 Mladenovac',
      venueName: 'Jasenica Ethnographic Zone',
      description: 'The Jasenica Folk Craft Market brings together over 80 artisans from the Šumadija region. Visitors can purchase handmade pottery, embroidered textiles, carved wooden items, and traditional musical instruments, with live demonstrations throughout both days.',
    },
    {
      id: 'e9', name: 'Divčibare Winter Opening',
      summary: 'Ski season opening ceremony with freestyle ski show, DJ sets and après-ski party.',
      region: 'Šumadija, Srbija', status: 'Draft', lastUpdated: 'Oct 5, 2023', sortTimestamp: makeTimestamp('Oct 5, 2023'),
      imageBg: 'linear-gradient(135deg,#84cc16 0%,#65a30d 100%)',
      category: 'Festival', dateRange: 'Dec 15, 2023', startDate: 'Dec 15, 2023', endDate: 'Dec 15, 2023',
      lat: 44.1340, lng: 20.1120, address: 'Divčibare bb, 32240 Ljig',
      venueName: 'Divčibare Mountain Resort',
      description: 'The Divčibare Winter Opening marks the official start of the ski season on Maljen mountain. The day programme includes a freestyle ski and snowboard demonstration, a torchlight descent, and an evening après-ski party with live DJ sets.',
    },
    {
      id: 'e10', name: 'Photography Workshop – Rudnik',
      summary: 'Two-day landscape photography workshop led by professional photographers on Rudnik mountain.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Oct 3, 2023', sortTimestamp: makeTimestamp('Oct 3, 2023'),
      imageBg: 'linear-gradient(135deg,#8b5cf6 0%,#6d28d9 100%)',
      category: 'Workshop', dateRange: 'Nov 11–12, 2023', startDate: 'Nov 11, 2023', endDate: 'Nov 12, 2023',
      lat: 44.1150, lng: 20.5230, address: 'Rudnik planina bb, 32252 Rudnik',
      venueName: 'Rudnik Mountain Area',
      description: 'The two-day landscape photography workshop on Rudnik mountain is limited to 12 participants. Led by award-winning photographer Marko Vasić, the programme covers golden-hour shooting, long-exposure forest scenes, and post-processing techniques. Accommodation at a mountain hut is included.',
    },
    {
      id: 'e11', name: 'Šumadija Food & Rakija Fair',
      summary: 'Regional fair celebrating local gastronomy with tastings of traditional spirits and dishes.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Sep 28, 2023', sortTimestamp: makeTimestamp('Sep 28, 2023'),
      imageBg: 'linear-gradient(135deg,#f97316 0%,#ea580c 100%)',
      category: 'Market', dateRange: 'Dec 1–3, 2023', startDate: 'Dec 1, 2023', endDate: 'Dec 3, 2023',
      lat: 44.0110, lng: 20.9162, address: 'Trg Radomira Putnika, 34000 Kragujevac',
      venueName: 'Trg Radomira Putnika',
      description: 'The Šumadija Food and Rakija Fair showcases regional gastronomy from across central Serbia. Over 60 producers present home-made šljivovica (plum brandy), quince and pear brandies, traditional smoked meats, cheeses, and preserved vegetables.',
    },
    {
      id: 'e12', name: 'Kalenić Monastery Night Concert',
      summary: 'Sacred music candlelight concert held in the monastery courtyard on the saint\'s day.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Sep 20, 2023', sortTimestamp: makeTimestamp('Sep 20, 2023'),
      imageBg: 'linear-gradient(135deg,#92400e 0%,#78350f 100%)',
      category: 'Concert', dateRange: 'Nov 30, 2023', startDate: 'Nov 30, 2023', endDate: 'Nov 30, 2023',
      lat: 43.8960, lng: 20.9540, address: 'Manastir Kalenić bb, 34220 Rekovac',
      venueName: 'Kalenić Monastery',
      description: 'On the Feast of the Presentation of the Virgin Mary, the Kalenić Monastery courtyard hosts a candlelight sacred music concert by the Belgrade Choral Society. The programme includes Byzantine chant and 19th-century Serbian liturgical compositions.',
    },
    {
      id: 'e13', name: 'Gruža Fishing Championship',
      summary: 'Open fishing competition on Gruža reservoir with prizes for largest catch and junior categories.',
      region: 'Šumadija, Srbija', status: 'Archived', lastUpdated: 'Sep 15, 2023', sortTimestamp: makeTimestamp('Sep 15, 2023'),
      imageBg: 'linear-gradient(135deg,#14b8a6 0%,#0d9488 100%)',
      category: 'Sports', dateRange: 'Sep 30, 2023', startDate: 'Sep 30, 2023', endDate: 'Sep 30, 2023',
      lat: 43.9558, lng: 20.6874, address: 'Akumulacija Gruža, 34208 Knić',
      venueName: 'Vikend Kamp Gruža',
      description: 'The Gruža Fishing Championship draws competitors from across Serbia and the region. Fishing runs from 6 AM to 2 PM in designated sectors along the reservoir shore. Prizes are awarded for the heaviest single catch, the most fish by weight, and a junior under-16 category.',
    },
    {
      id: 'e14', name: 'Heritage Ceramics Exhibition',
      summary: 'Exhibition of archaeological pottery finds from across Šumadija at the Narodni Muzej.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Sep 10, 2023', sortTimestamp: makeTimestamp('Sep 10, 2023'),
      imageBg: 'linear-gradient(135deg,#e11d48 0%,#be123c 100%)',
      category: 'Exhibition', dateRange: 'Nov 1–30, 2023', startDate: 'Nov 1, 2023', endDate: 'Nov 30, 2023',
      lat: 44.0126, lng: 20.9131, address: 'Vuka Karadžića 1, 34000 Kragujevac',
      venueName: 'Narodni Muzej Kragujevac',
      description: 'The Heritage Ceramics Exhibition presents over 300 archaeological pottery artefacts excavated from sites across Šumadija, spanning the Bronze Age, Roman, and medieval periods. The exhibition includes a live pottery wheel demonstration every Saturday.',
    },
    {
      id: 'e15', name: 'Rača River Rafting Day',
      summary: 'Guided white-water rafting descent of the Rača gorge suitable for beginners and families.',
      region: 'Šumadija, Srbija', status: 'Draft', lastUpdated: 'Sep 5, 2023', sortTimestamp: makeTimestamp('Sep 5, 2023'),
      imageBg: 'linear-gradient(135deg,#2563eb 0%,#1d4ed8 100%)',
      category: 'Sports', dateRange: 'Dec 23, 2023', startDate: 'Dec 23, 2023', endDate: 'Dec 23, 2023',
      lat: 43.9820, lng: 20.9410, address: 'Klisura reke Rače, 34000 Kragujevac',
      venueName: 'Rača River Gorge',
      description: 'Guided rafting descent through 8 km of the Rača gorge, from the entry point at Rača village to the exit near Bagrdanski most. Suitable for participants aged 10 and over. Safety equipment, certified guides, and post-rafting lunch are included.',
    },
    {
      id: 'e16', name: 'Christmas Market Kragujevac',
      summary: 'Traditional Christmas market in the city centre with crafts, food stalls and ice skating rink.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Aug 28, 2023', sortTimestamp: makeTimestamp('Aug 28, 2023'),
      imageBg: 'linear-gradient(135deg,#06b6d4 0%,#0891b2 100%)',
      category: 'Market', dateRange: 'Dec 20–Jan 7, 2024', startDate: 'Dec 20, 2023', endDate: 'Jan 7, 2024',
      lat: 44.0110, lng: 20.9162, address: 'Trg Radomira Putnika, 34000 Kragujevac',
      venueName: 'Trg Radomira Putnika',
      description: 'The Kragujevac Christmas Market runs for 18 days with over 50 artisan stalls, a 600 m² outdoor ice skating rink, mulled wine and hot food vendors, and nightly live music. The market is open daily from 10 AM to 11 PM.',
    },
    {
      id: 'e17', name: 'Birdwatching Tour – Opovo Wetlands',
      summary: 'Expert-guided birdwatching excursion to the Opovo wetlands during peak migratory season.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Aug 20, 2023', sortTimestamp: makeTimestamp('Aug 20, 2023'),
      imageBg: 'linear-gradient(135deg,#16a34a 0%,#15803d 100%)',
      category: 'Workshop', dateRange: 'Nov 18, 2023', startDate: 'Nov 18, 2023', endDate: 'Nov 18, 2023',
      lat: 44.9860, lng: 20.4380, address: 'Opovačke bare, 26204 Opovo',
      description: 'Expert ornithologist Dragan Simić leads a full-day guided tour of the Opovo wetlands during peak autumn migration. Target species include white-tailed eagle, black stork, and flocks of wading birds. Binoculars and field guides are provided; group limited to 15.',
    },
    {
      id: 'e18', name: 'Šumadija Half Marathon',
      summary: 'City road race through Kragujevac with 10 km fun run and 21 km competitive categories.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Aug 15, 2023', sortTimestamp: makeTimestamp('Aug 15, 2023'),
      imageBg: 'linear-gradient(135deg,#dc2626 0%,#b91c1c 100%)',
      category: 'Sports', dateRange: 'Nov 26, 2023', startDate: 'Nov 26, 2023', endDate: 'Nov 26, 2023',
      lat: 44.0110, lng: 20.9162, address: 'Start: Trg Radomira Putnika, 34000 Kragujevac',
      venueName: 'Trg Radomira Putnika',
      description: 'The Šumadija Half Marathon routes runners through Kragujevac\'s historic centre, Šumarice Park, and the riverside promenade. The 21 km competitive race is AIMS-certified, and a 10 km fun run is open to all fitness levels. Finisher medals and refreshments included.',
    },
    {
      id: 'e19', name: 'Traditional Slava Workshop',
      summary: 'Interactive cultural workshop teaching the rituals and culinary traditions of the Serbian Slava.',
      region: 'Šumadija, Srbija', status: 'Draft', lastUpdated: 'Aug 10, 2023', sortTimestamp: makeTimestamp('Aug 10, 2023'),
      imageBg: 'linear-gradient(135deg,#a855f7 0%,#9333ea 100%)',
      category: 'Workshop', dateRange: 'Dec 19, 2023', startDate: 'Dec 19, 2023', endDate: 'Dec 19, 2023',
      lat: 44.0126, lng: 20.9131, address: 'Narodni muzej, Vuka Karadžića 1, 34000 Kragujevac',
      venueName: 'Narodni Muzej Kragujevac',
      description: 'This half-day workshop guides participants through the preparation of Slava kolač (ritual bread) and žito (boiled wheat), the blessing ceremony, and the customs associated with family patron saint days. Suitable for foreign visitors and young Serbs reconnecting with heritage.',
    },
    {
      id: 'e20', name: 'New Year\'s Eve at Veliki Park',
      summary: 'Outdoor New Year\'s Eve concert and countdown celebration with fireworks in the city park.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Aug 5, 2023', sortTimestamp: makeTimestamp('Aug 5, 2023'),
      imageBg: 'linear-gradient(135deg,#f59e0b 0%,#b45309 100%)',
      category: 'Concert', dateRange: 'Dec 31, 2023', startDate: 'Dec 31, 2023', endDate: 'Dec 31, 2023',
      lat: 44.0178, lng: 20.9042, address: 'Kragujevačkog oktobra bb, 34000 Kragujevac',
      venueName: 'Veliki Park',
      description: 'Kragujevac welcomes the New Year in Veliki Park with a free outdoor concert by two headline acts, a midnight fireworks display over the park lake, and food and drink stalls open from 8 PM.',
    },
  ];

  // ── Public accessors ─────────────────────────────────────────────────────

  addItem(item: Omit<EventListItem, 'sortTimestamp'>): void {
    const ts = Date.now();
    this._all.unshift({ ...item, sortTimestamp: ts });
  }

  updateItem(id: string, patch: Partial<EventListItem>): void {
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

  findById(id: string): EventListItem | undefined {
    return this._all.find(x => x.id === id);
  }

  getPage$(params: {
    page: number; pageSize: number; search: string;
    sortField: EventSortField; sortDir: EventSortDir;
  }): Observable<EventsPage> {
    let list = [...this._all];
    if (params.search.trim()) {
      const q = params.search.trim().toLowerCase();
      list = list.filter(l => l.name.toLowerCase().includes(q) || l.summary.toLowerCase().includes(q));
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
}