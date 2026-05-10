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
  sortTimestamp: number;
  imageBg: string;
  category: string;
  lat?: number;
  lng?: number;
  address?: string;
  description?: string;
}

export type PlaceSortField = 'name' | 'lastUpdated' | 'status';
export type PlaceSortDir   = 'asc' | 'desc';

export interface PlacesPage {
  items: PlaceListItem[];
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
export class PlacesListService {

  private _all: PlaceListItem[] = [
    {
      id: 'p1', name: 'Narodni Muzej Kragujevac',
      summary: 'City museum housing archaeological finds, fine art and regional history collections.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Oct 24, 2023', sortTimestamp: makeTimestamp('Oct 24, 2023'),
      imageBg: 'url(https://images.unsplash.com/photo-1554907984-15263bfd63bd?w=120&q=60) center/cover',
      category: 'Museum', lat: 44.0126, lng: 20.9131, address: 'Vuka Karadžića 1, 34000 Kragujevac',
      description: 'The National Museum of Kragujevac holds over 35,000 artefacts spanning prehistoric, Roman, medieval and modern Serbian history. The permanent collection includes the Zastava automobile archive and a significant numismatic collection.',
    },
    {
      id: 'p2', name: 'Crkva Svetog Đorđa – Oplenac',
      summary: 'Iconic royal mausoleum church covered entirely in mosaic reproductions of Serbian frescoes.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Oct 22, 2023', sortTimestamp: makeTimestamp('Oct 22, 2023'),
      imageBg: 'url(https://images.unsplash.com/photo-1529420705456-b94baeebe4b5?w=120&q=60) center/cover',
      category: 'Landmark', lat: 44.2561, lng: 20.6934, address: 'Oplenac bb, 34310 Topola',
      description: 'The Church of Saint George on Oplenac Hill was commissioned by King Petar I Karađorđević in 1912. Its white marble exterior and interior mosaics — composed of 40 million tesserae — reproduce frescoes from over 60 medieval Serbian monasteries, making it one of the finest examples of Byzantine revival architecture.',
    },
    {
      id: 'p3', name: 'Hotel Šumarice',
      summary: 'Four-star conference hotel surrounded by the Šumarice Memorial Park forest.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Oct 20, 2023', sortTimestamp: makeTimestamp('Oct 20, 2023'),
      imageBg: 'url(https://images.unsplash.com/photo-1566073771259-6a8506099945?w=120&q=60) center/cover',
      category: 'Accommodation', lat: 44.0189, lng: 20.8912, address: 'Marka Oreškovića 1, 34000 Kragujevac',
      description: 'Hotel Šumarice is a four-star property set within the memorial park forest, offering conference facilities, a spa, and direct access to the park\'s walking trails.',
    },
    {
      id: 'p4', name: 'Risovača Cave',
      summary: 'Illuminated prehistoric cave with paleolithic artefacts and guided tours.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Oct 18, 2023', sortTimestamp: makeTimestamp('Oct 18, 2023'),
      imageBg: 'url(https://images.unsplash.com/photo-1504893524553-b855bce32c67?w=120&q=60) center/cover',
      category: 'Attraction', lat: 44.3056, lng: 20.5528, address: 'Risovača bb, 34300 Aranđelovac',
      description: 'Risovača Cave near Aranđelovac is one of the most significant archaeological sites in Serbia. Excavations revealed tools and animal bones from Paleolithic inhabitants, and the cave\'s illuminated galleries stretch for over 400 metres.',
    },
    {
      id: 'p5', name: 'Knjazevsko-Srpski Teatar',
      summary: 'One of the oldest theatre buildings in Serbia, founded in the 19th century.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Oct 15, 2023', sortTimestamp: makeTimestamp('Oct 15, 2023'),
      imageBg: 'url(https://images.unsplash.com/photo-1503095396549-807759245b35?w=120&q=60) center/cover',
      category: 'Culture', lat: 44.0106, lng: 20.9103, address: 'Danićeva 3, 34000 Kragujevac',
      description: 'The Principality of Serbia Theatre was founded in 1835 by Prince Miloš Obrenović, making it one of the oldest theatres in the Balkans. The current building preserves the neoclassical façade from the late 19th-century reconstruction.',
    },
    {
      id: 'p6', name: 'Zastava Museum',
      summary: 'Heritage museum documenting the history of the Zastava automobile factory.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Oct 12, 2023', sortTimestamp: makeTimestamp('Oct 12, 2023'),
      imageBg: 'linear-gradient(135deg,#ef4444 0%,#b91c1c 100%)',
      category: 'Museum', lat: 44.0238, lng: 20.9285, address: 'Trg Topolivca 4, 34000 Kragujevac',
      description: 'The Zastava Museum traces the evolution of Yugoslavia\'s most iconic car brand, from the 1955 Fiat licence agreement to the final production models. Exhibits include restored Zastava 750 (Fića) vehicles and original factory machinery.',
    },
    {
      id: 'p7', name: 'Oplenac Winery',
      summary: 'Royal estate winery producing the acclaimed Oplenac white and rosé wines.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Oct 10, 2023', sortTimestamp: makeTimestamp('Oct 10, 2023'),
      imageBg: 'url(https://images.unsplash.com/photo-1510812431401-41d2bd2722f3?w=120&q=60) center/cover',
      category: 'Winery', lat: 44.2548, lng: 20.6912, address: 'Vinska 5, 34310 Topola',
      description: 'The Oplenac Royal Winery was established in 1903 on the royal estate of King Petar I. Today it produces certified PDO Oplenac wines from indigenous Smederevka and Tamjanika grape varieties across 100 hectares of certified vineyards.',
    },
    {
      id: 'p8', name: 'Sumadija Sajam',
      summary: 'Regional fairground and multi-purpose event venue hosting agricultural and cultural fairs.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Oct 8, 2023', sortTimestamp: makeTimestamp('Oct 8, 2023'),
      imageBg: 'linear-gradient(135deg,#0891b2 0%,#0e7490 100%)',
      category: 'Venue', lat: 44.0248, lng: 20.9305, address: 'Save Kovačevića bb, 34000 Kragujevac',
      description: 'Šumadija Sajam is the largest exposition and fairground in central Serbia, hosting the annual Agricultural Fair, technology expos, and large-scale concerts. The site covers 15 hectares with 12 pavilions.',
    },
    {
      id: 'p9', name: 'Stadion Čika Dača',
      summary: 'Home stadium of FK Radnički Kragujevac, one of Serbia\'s historic football clubs.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Oct 5, 2023', sortTimestamp: makeTimestamp('Oct 5, 2023'),
      imageBg: 'linear-gradient(135deg,#15803d 0%,#166534 100%)',
      category: 'Sports', lat: 44.0212, lng: 20.8991, address: 'Kralja Milana 4, 34000 Kragujevac',
      description: 'Čika Dača Stadium has been the home ground of FK Radnički since 1923. With a capacity of 20,000, it is one of the largest football stadiums in Serbia outside Belgrade.',
    },
    {
      id: 'p10', name: 'Hala Jezero',
      summary: 'Multi-purpose indoor sports and events arena located by Bubanj Lake.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Oct 3, 2023', sortTimestamp: makeTimestamp('Oct 3, 2023'),
      imageBg: 'linear-gradient(135deg,#7c3aed 0%,#6d28d9 100%)',
      category: 'Venue', lat: 44.0139, lng: 20.9241, address: 'Grada Sirena 15, 34000 Kragujevac',
      description: 'Hala Jezero (Lake Arena) is a 5,000-seat multi-purpose hall used for basketball, volleyball, handball, and large indoor events. It hosts KK Radnički home games and regional cultural events.',
    },
    {
      id: 'p11', name: 'Vikend Kamp Gruža',
      summary: 'Lakeside camping ground offering bungalows, boat rentals, and fishing piers.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Sep 28, 2023', sortTimestamp: makeTimestamp('Sep 28, 2023'),
      imageBg: 'linear-gradient(135deg,#0ea5e9 0%,#0369a1 100%)',
      category: 'Accommodation', lat: 43.9558, lng: 20.6874, address: 'Obala Gruže bb, 34208 Knić',
      description: 'The Gruža Weekend Camp offers 40 bungalows, a campsite for tents and caravans, a boat rental service, and six fishing piers on the reservoir. Open from April to October.',
    },
    {
      id: 'p12', name: 'Restoran Tri Šešira',
      summary: 'Traditional Serbian restaurant specialising in Šumadija cuisine and local wines.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Sep 20, 2023', sortTimestamp: makeTimestamp('Sep 20, 2023'),
      imageBg: 'linear-gradient(135deg,#a16207 0%,#92400e 100%)',
      category: 'Restaurant', lat: 44.0115, lng: 20.9148, address: 'Kneza Mihaila 12, 34000 Kragujevac',
      description: 'Tri Šešira (Three Hats) has served traditional Šumadija cuisine for over 40 years. Signature dishes include pljeskavica, šopska salata, and slow-roasted lamb, paired with Oplenac wines from the restaurant\'s cellar.',
    },
    {
      id: 'p13', name: 'Kalenić Monastery',
      summary: '15th-century Serbian Orthodox monastery considered the pinnacle of the Morava School.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Sep 15, 2023', sortTimestamp: makeTimestamp('Sep 15, 2023'),
      imageBg: 'linear-gradient(135deg,#92400e 0%,#78350f 100%)',
      category: 'Landmark', lat: 43.8743, lng: 20.9156, address: 'Manastir Kalenić bb, 34220 Rekovac',
      description: 'Built around 1413 by the Serbian nobleman Bogdan and his wife Milica, Kalenić Monastery is renowned for its exquisite stone carvings on the church portal and narthex. The surviving frescoes, dating from the same period, are among the most refined examples of Byzantine-Slavic painting.',
    },
    {
      id: 'p14', name: 'Aranđelovac Marble Sculpture Park',
      summary: 'Outdoor sculpture park created by international artists attending the annual Marble and Sounds festival.',
      region: 'Šumadija, Srbija', status: 'Published', lastUpdated: 'Sep 10, 2023', sortTimestamp: makeTimestamp('Sep 10, 2023'),
      imageBg: 'url(https://images.unsplash.com/photo-1571019613454-1cb2f99b2d8b?w=120&q=60) center/cover',
      category: 'Attraction', lat: 44.3019, lng: 20.5604, address: 'Park Bukovička Banja, 34300 Aranđelovac',
      description: 'The Bukovička Banja park displays over 200 marble sculptures created by artists from more than 40 countries who participated in the Mermer i Zvuci (Marble and Sounds) symposium since 1963. The collection continues to grow with each annual festival.',
    },
    {
      id: 'p15', name: 'Stragari Fortress Ruins',
      summary: 'Partially excavated medieval fortress ruins on a hilltop above the Šumadija plain.',
      region: 'Šumadija, Srbija', status: 'Draft', lastUpdated: 'Sep 5, 2023', sortTimestamp: makeTimestamp('Sep 5, 2023'),
      imageBg: 'linear-gradient(135deg,#fb923c 0%,#f97316 100%)',
      category: 'Archaeological Site', lat: 44.0534, lng: 20.7823, address: 'Stragari bb, 34000 Kragujevac',
      description: 'Stragari Fortress sits on a hilltop at 380 m and was one of the key defensive points of medieval Serbian rulers. Ongoing excavations since 2015 have uncovered Byzantine ceramics, a watch tower foundation, and a medieval church nave.',
    },
  ];

  // ── Public accessors ─────────────────────────────────────────────────────

  addItem(item: Omit<PlaceListItem, 'sortTimestamp'>): void {
    const ts = Date.now();
    this._all.unshift({ ...item, sortTimestamp: ts });
  }

  updateItem(id: string, patch: Partial<PlaceListItem>): void {
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

  findById(id: string): PlaceListItem | undefined {
    return this._all.find(x => x.id === id);
  }

  getPage$(params: {
    page: number; pageSize: number; search: string;
    sortField: PlaceSortField; sortDir: PlaceSortDir;
  }): Observable<PlacesPage> {
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