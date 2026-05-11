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
  sortTimestamp: number;
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
  internalRefId?: string;
}

export type DestinationSortField = 'name' | 'lastUpdated' | 'status';
export type DestinationSortDir   = 'asc' | 'desc';

export interface DestinationsPage {
  items: DestinationListItem[];
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
export class DestinationsListService {

  private _all: DestinationListItem[] = [
    {
      id: 'd1', name: 'Šumadija Heartland',
      summary: 'The geographic and cultural core of central Serbia, known for vineyards and medieval heritage.',
      description: 'Šumadija Heartland covers the rolling hills between Kragujevac and Topola, a region celebrated for its Oplenac wines, medieval monasteries and traditional village life. The area was the cradle of the First Serbian Uprising in the 19th century and remains a cornerstone of Serbian national identity.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Oct 24, 2023', sortTimestamp: makeTimestamp('Oct 24, 2023'),
      imageBg: 'url(https://images.unsplash.com/photo-1506905925346-21bda4d32df4?w=400&q=80) center/cover',
      category: 'Destination', lat: 44.0165, lng: 20.9114, address: 'Šumadija region, centralna Srbija',
      zone: [[44.05,20.85],[44.08,20.97],[44.02,21.00],[43.98,20.93],[43.97,20.85]],
    },
    {
      id: 'd2', name: 'Kragujevac Urban Zone',
      summary: 'The largest city in Šumadija, blending industrial heritage with cultural institutions.',
      description: 'Kragujevac is the fourth-largest city in Serbia and the historical capital of the country during the 19th century. The city houses the Narodni muzej, the Zastava car factory heritage site, and the Šumarice Memorial Park — one of the most poignant WWII monuments in the Balkans.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Oct 22, 2023', sortTimestamp: makeTimestamp('Oct 22, 2023'),
      imageBg: 'url(https://images.unsplash.com/photo-1477959858617-67f85cf4f1df?w=400&q=80) center/cover',
      category: 'Destination', lat: 44.0128, lng: 20.9237, address: 'Kragujevac, Šumadija',
      zone: [[44.03,20.89],[44.03,20.95],[44.00,20.96],[43.99,20.90]],
    },
    {
      id: 'd3', name: 'Topola & Oplenac Region',
      summary: 'Royal Serbian dynasty homeland with the famous Oplenac mausoleum and wine tradition.',
      description: 'Topola is the birthplace of Karađorđe, the leader of the First Serbian Uprising. The town is crowned by the Church of Saint George on Oplenac hill, a royal mausoleum covered entirely in mosaic reproductions of medieval Serbian frescoes. The surrounding vineyards produce award-winning Oplenac wines.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Oct 20, 2023', sortTimestamp: makeTimestamp('Oct 20, 2023'),
      imageBg: 'url(https://images.unsplash.com/photo-1529420705456-b94baeebe4b5?w=400&q=80) center/cover',
      category: 'Destination', lat: 44.2558, lng: 20.6817, address: 'Topola, Šumadija',
      zone: [[44.27,20.67],[44.27,20.70],[44.24,20.71],[44.23,20.68]],
    },
    {
      id: 'd4', name: 'Aranđelovac Spa Territory',
      summary: 'Mineral spring resort area centered around Bukovička Banja with therapeutic waters.',
      description: 'Aranđelovac has been a spa destination since the 19th century. The Bukovička Banja park features mineral springs, shaded promenades and the famous marble sculpture collection. The town also hosts the Mermer i zvuci (Marble and Sounds) international art festival each summer.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Oct 18, 2023', sortTimestamp: makeTimestamp('Oct 18, 2023'),
      imageBg: 'url(https://images.unsplash.com/photo-1571019613454-1cb2f99b2d8b?w=400&q=80) center/cover',
      category: 'Destination', lat: 44.3017, lng: 20.5631, address: 'Aranđelovac, Šumadija',
      zone: [[44.32,20.55],[44.32,20.58],[44.29,20.58],[44.29,20.55]],
    },
    {
      id: 'd5', name: 'Rudnik Mountain Area',
      summary: 'Ancient mining mountain with medieval monasteries, hiking routes and forest landscapes.',
      description: 'Rudnik is one of the oldest mining areas in the Balkans, documented since Roman times. The forested slopes at 1,100 m elevation shelter several medieval monasteries and provide excellent hiking, mountain biking, and paragliding conditions throughout the year.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Oct 15, 2023', sortTimestamp: makeTimestamp('Oct 15, 2023'),
      imageBg: 'url(https://images.unsplash.com/photo-1470770903676-69b98201ea1c?w=400&q=80) center/cover',
      category: 'Destination', lat: 44.1228, lng: 20.5142, address: 'Rudnik planina, Šumadija',
      zone: [[44.14,20.49],[44.14,20.54],[44.10,20.54],[44.10,20.49]],
    },
    {
      id: 'd6', name: 'Gruža Lake Corridor',
      summary: 'Recreational corridor along the Gruža reservoir with fishing, beaches and village tourism.',
      description: 'The Gruža reservoir stretches for 12 km through an idyllic valley flanked by traditional Serbian villages. Popular activities include freshwater fishing, kayaking, and agritourism stays at local farms. The corridor connects several historic mills and rural Orthodox churches.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Oct 12, 2023', sortTimestamp: makeTimestamp('Oct 12, 2023'),
      imageBg: 'linear-gradient(135deg,#0ea5e9 0%,#0369a1 100%)',
      category: 'Destination', lat: 43.9347, lng: 20.6812, address: 'Gruža jezero, Knić, Šumadija',
      zone: [[43.96,20.66],[43.96,20.70],[43.91,20.70],[43.91,20.66]],
    },
    {
      id: 'd7', name: 'Jasenica Ethnographic Zone',
      summary: 'A belt of traditional Serbian villages preserving folk architecture and rural customs.',
      description: 'Jasenica is a sub-region of Šumadija where 19th-century village architecture, folk crafts and traditional slava celebrations have been particularly well preserved. The open-air ethnographic museum at Jasenica displays authentic farmsteads, craft workshops, and textile looms.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Oct 10, 2023', sortTimestamp: makeTimestamp('Oct 10, 2023'),
      imageBg: 'linear-gradient(135deg,#a855f7 0%,#9333ea 100%)',
      category: 'Destination', lat: 44.1803, lng: 20.7451, address: 'Jasenica, Šumadija',
      zone: [[44.20,20.72],[44.20,20.77],[44.16,20.77],[44.16,20.72]],
    },
    {
      id: 'd8', name: 'Gledić Mountains Reserve',
      summary: 'Undeveloped highland reserve with traditional shepherding communities and endemic flora.',
      description: 'The Gledić Mountains form a wild highland belt east of Kragujevac, with shepherding communities maintaining ancient transhumance traditions. The reserve protects rare endemic plant species and provides habitat for wolves, lynx and golden eagles.',
      region: 'Šumadija, Srbija', status: 'Draft', lastUpdated: 'Oct 8, 2023', sortTimestamp: makeTimestamp('Oct 8, 2023'),
      imageBg: 'linear-gradient(135deg,#10b981 0%,#059669 100%)',
      category: 'Destination', lat: 43.8912, lng: 20.9834, address: 'Gledićke planine, Šumadija',
      zone: [[43.91,20.96],[43.91,21.01],[43.87,21.01],[43.87,20.96]],
    },
    {
      id: 'd9', name: 'Rača River Valley',
      summary: 'Scenic gorge destination popular for kayaking, fly-fishing and riverside camping.',
      description: 'The Rača gorge cuts through limestone cliffs for over 8 km, creating one of the most dramatic river landscapes in central Serbia. Rapids ranging from Grade II to IV attract whitewater enthusiasts, while calmer stretches are ideal for fly-fishing for brown trout.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Oct 5, 2023', sortTimestamp: makeTimestamp('Oct 5, 2023'),
      imageBg: 'linear-gradient(135deg,#2563eb 0%,#1d4ed8 100%)',
      category: 'Destination', lat: 43.9651, lng: 20.5728, address: 'Rača klisura, Šumadija',
      zone: [[43.98,20.55],[43.98,20.60],[43.95,20.60],[43.95,20.55]],
    },
    {
      id: 'd10', name: 'Kalenić Monastery Heritage Zone',
      summary: 'UNESCO candidate area surrounding the 15th-century Kalenić Monastery complex.',
      description: 'Kalenić Monastery, built around 1413, is considered the pinnacle of the Morava School of Serbian medieval architecture. Its church portal and remaining frescoes represent some of the finest examples of Byzantine-influenced art in the Balkans. The heritage zone encompasses surrounding forest and pilgrimage routes.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Oct 3, 2023', sortTimestamp: makeTimestamp('Oct 3, 2023'),
      imageBg: 'linear-gradient(135deg,#92400e 0%,#78350f 100%)',
      category: 'Destination', lat: 43.8743, lng: 20.9156, address: 'Manastir Kalenić, Rekovac, Šumadija',
      zone: [[43.89,20.89],[43.89,20.94],[43.86,20.94],[43.86,20.89]],
    },
    {
      id: 'd11', name: 'Divčibare Highland Resort',
      summary: 'Mountain resort on Maljen peak with winter skiing and summer wellness tourism.',
      description: 'Divčibare sits at approximately 1,000 m on Maljen mountain and is the closest ski resort to Belgrade. The plateau features 12 km of ski runs, cross-country trails, a sports centre, and a growing number of wellness hotels. In summer the fresh mountain air attracts hikers and trail runners.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Sep 28, 2023', sortTimestamp: makeTimestamp('Sep 28, 2023'),
      imageBg: 'linear-gradient(135deg,#84cc16 0%,#65a30d 100%)',
      category: 'Destination', lat: 44.1251, lng: 20.1034, address: 'Divčibare, Maljen planina',
      zone: [[44.14,20.08],[44.14,20.13],[44.11,20.13],[44.11,20.08]],
    },
    {
      id: 'd12', name: 'Mladenovac Green Belt',
      summary: 'Protected periurban forest zone with cycling paths and weekend retreat facilities.',
      description: 'The Mladenovac Green Belt is a protected periurban forest corridor that serves as the green lung of southern Belgrade metropolitan area. It features 35 km of marked cycling and walking trails, weekend cabin rentals, and a freshwater fishing lake.',
      region: 'Šumadija, Srbija', status: 'Draft', lastUpdated: 'Sep 20, 2023', sortTimestamp: makeTimestamp('Sep 20, 2023'),
      imageBg: 'linear-gradient(135deg,#78716c 0%,#57534e 100%)',
      category: 'Destination', lat: 44.4328, lng: 20.6934, address: 'Mladenovac, Šumadija',
      zone: [[44.45,20.67],[44.45,20.72],[44.42,20.72],[44.42,20.67]],
    },
    {
      id: 'd13', name: 'Opovo Wetland Sanctuary',
      summary: 'Protected wetland ecosystem critical for migratory waterbirds and biodiversity research.',
      description: 'The Opovo wetlands form part of the Pannonian wetland network and are recognised as an Important Bird Area. Over 200 species of birds have been recorded here, including the white-tailed eagle, black stork, and several heron species. Guided birdwatching tours run from March to November.',
      region: 'Šumadija, Srbija', status: 'Archived', lastUpdated: 'Sep 15, 2023', sortTimestamp: makeTimestamp('Sep 15, 2023'),
      imageBg: 'linear-gradient(135deg,#16a34a 0%,#15803d 100%)',
      category: 'Destination', lat: 45.0512, lng: 20.4317, address: 'Opovo, Vojvodina',
      zone: [[45.07,20.41],[45.07,20.46],[45.03,20.46],[45.03,20.41]],
    },
    {
      id: 'd14', name: 'Kladnica Valley Retreat',
      summary: 'Quiet agritourism valley with medieval monastery ruins and traditional orchard farms.',
      description: 'Kladnica Valley is an emerging agritourism destination where guests can pick fruit in heritage orchards, learn traditional brandy distillation, and visit the partially preserved medieval Kladnica Monastery ruins. Local guesthouses offer farm-to-table dining experiences.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Sep 10, 2023', sortTimestamp: makeTimestamp('Sep 10, 2023'),
      imageBg: 'linear-gradient(135deg,#dc2626 0%,#b91c1c 100%)',
      category: 'Destination', lat: 44.0812, lng: 20.6341, address: 'Kladnica dolina, Šumadija',
      zone: [[44.10,20.61],[44.10,20.66],[44.06,20.66],[44.06,20.61]],
    },
    {
      id: 'd15', name: 'Stragari Medieval Corridor',
      summary: 'Archaeological trail connecting medieval fortification ruins across the Šumadija plain.',
      description: 'The Stragari Medieval Corridor links four fortification ruins dating from the 13th to 15th century across a 20 km trail through the Šumadija plain. Archaeological excavations are ongoing at the primary Stragari fortress site, and a small on-site museum displays recovered artefacts.',
      region: 'Šumadija, Srbija', status: 'Draft', lastUpdated: 'Sep 5, 2023', sortTimestamp: makeTimestamp('Sep 5, 2023'),
      imageBg: 'linear-gradient(135deg,#fb923c 0%,#f97316 100%)',
      category: 'Destination', lat: 44.0534, lng: 20.7823, address: 'Stragari, Šumadija',
      zone: [[44.07,20.76],[44.07,20.81],[44.04,20.81],[44.04,20.76]],
    },
  ];

  // ── Public accessors ─────────────────────────────────────────────────────

  getAllItems(): DestinationListItem[] { return [...this._all]; }

  addItem(item: Omit<DestinationListItem, 'sortTimestamp'>): void {
    const ts = Date.now();
    this._all.unshift({ ...item, sortTimestamp: ts });
  }

  updateItem(id: string, patch: Partial<DestinationListItem>): void {
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

  findById(id: string): DestinationListItem | undefined {
    return this._all.find(x => x.id === id);
  }

  getPage$(params: {
    page: number; pageSize: number; search: string;
    sortField: DestinationSortField; sortDir: DestinationSortDir;
  }): Observable<DestinationsPage> {
    let list = [...this._all];

    if (params.search.trim()) {
      const q = params.search.trim().toLowerCase();
      list = list.filter(l =>
        l.name.toLowerCase().includes(q) || l.summary.toLowerCase().includes(q),
      );
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