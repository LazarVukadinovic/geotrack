import { Injectable } from '@angular/core';
import { Observable, of, BehaviorSubject } from 'rxjs';
import { delay, tap } from 'rxjs/operators';


export interface RegionListItem {
  id: string;
  name: string;
  description: string;
  isTop: boolean;
  destinations: number;
  places: number;
  locations: number;
  events: number;
  assignedPersons: number;
  status: 'Active' | 'Inactive';
  imageBg: string;
}

export type RegionSortField = 'name' | 'status' | 'assignedPersons' | 'destinations';
export type RegionSortDir   = 'asc' | 'desc';

export interface RegionPage {
  items: RegionListItem[];
  total: number;
}

export interface RegionPageRequest {
  page: number;
  pageSize: number;
  search: string;
  sortField: RegionSortField;
  sortDir: RegionSortDir;
}


export interface RegionTeamMember {
  id: string;
  name: string;
  email: string;
  initials: string;
  avatarColor: string;
  role: string;
  status: 'Active' | 'Inactive';
  assignedDate: string;
}

export interface RegionDetail extends RegionListItem {
  fullName: string;
  internalRefId: string;
  shortSummary: string;
  fullDescription: string;
  team: RegionTeamMember[];
  createdBy: string;
  createdDate: string;
  lastUpdated: string;
  activityScore: number;
  activityLabel: string;
  processingLabel: string;
  pendingRequests: number;
  avgProcessingTime: number;
}


const ALL_REGIONS: RegionListItem[] = [
  {
    id: 'REG-001', name: 'Šumadija', isTop: true,
    description: 'Coastal & mountainous regions including major national forests.',
    destinations: 45, places: 315, locations: 120, events: 18,
    assignedPersons: 2, status: 'Active',
    imageBg: 'linear-gradient(135deg,#dbeafe 0%,#bfdbfe 100%)',
  },
  {
    id: 'REG-002', name: 'Banat', isTop: false,
    description: 'Desert ecosystems, canyons, and arid plateaus.',
    destinations: 28, places: 190, locations: 85, events: 5,
    assignedPersons: 15, status: 'Active',
    imageBg: 'linear-gradient(135deg,#fef9c3 0%,#fde68a 100%)',
  },
  {
    id: 'REG-003', name: 'Pomoravlje', isTop: true,
    description: 'Historic landmarks, dense urban coastal areas, and mixed forests.',
    destinations: 65, places: 480, locations: 180, events: 32,
    assignedPersons: 110, status: 'Active',
    imageBg: 'linear-gradient(135deg,#d1fae5 0%,#a7f3d0 100%)',
  },
  {
    id: 'REG-004', name: 'Raška', isTop: false,
    description: 'Wetlands, coastal shores, and southern marine sanctuaries.',
    destinations: 40, places: 275, locations: 105, events: 21,
    assignedPersons: 65, status: 'Active',
    imageBg: 'linear-gradient(135deg,#ede9fe 0%,#ddd6fe 100%)',
  },
  {
    id: 'REG-005', name: 'Bačka', isTop: false,
    description: 'Extensive agricultural regions, prairies, and major river systems.',
    destinations: 35, places: 210, locations: 90, events: 8,
    assignedPersons: 24, status: 'Active',
    imageBg: 'linear-gradient(135deg,#fce7f3 0%,#fbcfe8 100%)',
  },
  {
    id: 'REG-006', name: 'Mačva', isTop: false,
    description: 'Ancient mountain ranges, dense eastern forests, and trails.',
    destinations: 19, places: 140, locations: 65, events: 12,
    assignedPersons: 38, status: 'Active',
    imageBg: 'linear-gradient(135deg,#ffedd5 0%,#fed7aa 100%)',
  },
  {
    id: 'REG-007', name: 'Podunavlje', isTop: false,
    description: 'River valleys with medieval fortresses and scenic gorges.',
    destinations: 22, places: 160, locations: 78, events: 6,
    assignedPersons: 0, status: 'Inactive',
    imageBg: 'linear-gradient(135deg,#e0f2fe 0%,#bae6fd 100%)',
  },
  {
    id: 'REG-008', name: 'Timočka Krajina', isTop: false,
    description: 'Remote wilderness with rich mining heritage and cave systems.',
    destinations: 14, places: 95, locations: 44, events: 3,
    assignedPersons: 0, status: 'Inactive',
    imageBg: 'linear-gradient(135deg,#f3f4f6 0%,#e5e7eb 100%)',
  },
  {
    id: 'REG-009', name: 'Zlatiborski okrug', isTop: false,
    description: 'Alpine meadows, ski resorts, and traditional ethno-villages.',
    destinations: 33, places: 220, locations: 91, events: 14,
    assignedPersons: 42, status: 'Active',
    imageBg: 'linear-gradient(135deg,#dcfce7 0%,#bbf7d0 100%)',
  },
  {
    id: 'REG-010', name: 'Nišavski okrug', isTop: false,
    description: 'Urban cultural hub with ancient Roman sites and fortress.',
    destinations: 17, places: 130, locations: 60, events: 9,
    assignedPersons: 0, status: 'Inactive',
    imageBg: 'linear-gradient(135deg,#fef3c7 0%,#fde68a 100%)',
  },
];

const DETAIL_OVERRIDES: Record<string, Partial<RegionDetail>> = {
  'REG-001': {
    fullName: 'Šumadija, Srbija',
    internalRefId: 'REG-SRB-001',
    shortSummary: 'Central historical region known for agriculture and tourism.',
    fullDescription:
      'Šumadija is a geographical region in the central part of Serbia. The area is heavily forested, hence its name. It is known for its rich history, rolling hills, and agricultural output.',
    team: [
      {
        id: 'USR-001', name: 'Sarah Jenkins', email: 'sarah.j@geotrack.com',
        initials: 'SJ', avatarColor: '#3b82f6',
        role: 'Regional Manager', status: 'Active', assignedDate: '01 Sep 2023',
      },
    ],
    createdBy: 'System Admin',
    createdDate: '12 Oct 2023',
    lastUpdated: '15 Mar 2024',
    activityScore: 92,
    activityLabel: 'Highly Active',
    processingLabel: 'Fast Processing',
    pendingRequests: 12,
    avgProcessingTime: 2.4,
  },
  'REG-002': {
    fullName: 'Banat, Srbija',
    internalRefId: 'REG-SRB-002',
    shortSummary: 'Geographical and historical region in northeastern Serbia.',
    fullDescription:
      'Banat is a geographical and historical region in Central Europe. The Serbian part of Banat is located in the northeastern part of Serbia, comprising flat fertile plains along the Tisa and Danube rivers.',
    team: [
      {
        id: 'USR-013', name: 'Bojana Lazić', email: 'b.lazic@geotrack.com',
        initials: 'BL', avatarColor: '#f43f5e',
        role: 'Regional Manager', status: 'Active', assignedDate: '01 Oct 2023',
      },
      {
        id: 'USR-002', name: 'Michael Roberts', email: 'm.roberts@geotrack.com',
        initials: 'MR', avatarColor: '#8b5cf6',
        role: 'Viewer', status: 'Active', assignedDate: '15 Oct 2023',
      },
    ],
    createdBy: 'System Admin',
    createdDate: '05 Nov 2023',
    lastUpdated: '20 Feb 2024',
    activityScore: 78,
    activityLabel: 'Active',
    processingLabel: 'Normal',
    pendingRequests: 4,
    avgProcessingTime: 3.1,
  },
  'REG-003': {
    fullName: 'Pomoravlje, Srbija',
    internalRefId: 'REG-SRB-003',
    shortSummary: 'Historic landmarks, dense urban coastal areas, and mixed forests.',
    fullDescription:
      'Pomoravlje is a district in central Serbia. It takes its name from the Great Morava river. The area is known for its rich cultural heritage, including medieval monasteries and fortresses.',
    team: [
      {
        id: 'USR-004', name: 'Nikola Petrović', email: 'n.petrovic@geotrack.com',
        initials: 'NP', avatarColor: '#f59e0b',
        role: 'Regional Manager', status: 'Active', assignedDate: '12 Aug 2023',
      },
    ],
    createdBy: 'System Admin',
    createdDate: '18 Sep 2023',
    lastUpdated: '02 Apr 2024',
    activityScore: 88,
    activityLabel: 'Highly Active',
    processingLabel: 'Fast Processing',
    pendingRequests: 7,
    avgProcessingTime: 1.8,
  },
  'REG-004': {
    fullName: 'Raška, Srbija',
    internalRefId: 'REG-SRB-004',
    shortSummary: 'Wetlands, coastal shores, and southern marine sanctuaries.',
    fullDescription:
      'Raška is a district in southwestern Serbia. The region is known for its medieval monasteries, including Studenica and Žiča, as well as its natural beauty and rural landscapes.',
    team: [
      {
        id: 'USR-005', name: 'Ana Kovačević', email: 'a.kovacevic@geotrack.com',
        initials: 'AK', avatarColor: '#ef4444',
        role: 'Regional Manager', status: 'Active', assignedDate: '30 Jul 2023',
      },
      {
        id: 'USR-015', name: 'Luka Popović', email: 'l.popovic@geotrack.com',
        initials: 'LP', avatarColor: '#22c55e',
        role: 'Viewer', status: 'Active', assignedDate: '28 Feb 2024',
      },
    ],
    createdBy: 'System Admin',
    createdDate: '10 Oct 2023',
    lastUpdated: '10 Jan 2024',
    activityScore: 81,
    activityLabel: 'Active',
    processingLabel: 'Normal',
    pendingRequests: 5,
    avgProcessingTime: 2.8,
  },
  'REG-005': {
    fullName: 'Bačka, Srbija',
    internalRefId: 'REG-SRB-005',
    shortSummary: 'Extensive agricultural regions, prairies, and major river systems.',
    fullDescription:
      'Bačka is a geographical region divided between Serbia and Hungary. The Serbian part lies within Vojvodina and is characterized by flat, fertile plains along the Danube and Tisa rivers.',
    team: [
      {
        id: 'USR-013', name: 'Bojana Lazić', email: 'b.lazic@geotrack.com',
        initials: 'BL', avatarColor: '#f43f5e',
        role: 'Regional Manager', status: 'Active', assignedDate: '01 Oct 2023',
      },
      {
        id: 'USR-003', name: 'Emily Chen', email: 'echen@geotrack.com',
        initials: 'EC', avatarColor: '#10b981',
        role: 'Viewer', status: 'Inactive', assignedDate: '20 Nov 2023',
      },
    ],
    createdBy: 'System Admin',
    createdDate: '01 Nov 2023',
    lastUpdated: '05 Mar 2024',
    activityScore: 74,
    activityLabel: 'Active',
    processingLabel: 'Normal',
    pendingRequests: 3,
    avgProcessingTime: 3.5,
  },
  'REG-006': {
    fullName: 'Mačva, Srbija',
    internalRefId: 'REG-SRB-006',
    shortSummary: 'Ancient mountain ranges, dense eastern forests, and trails.',
    fullDescription:
      'Mačva is a region in western Serbia, bordered by the Sava and Drina rivers. It is known for fertile agricultural land and the historic town of Šabac.',
    team: [
      {
        id: 'USR-009', name: 'Dragana Nikolić', email: 'd.nikolic@geotrack.com',
        initials: 'DN', avatarColor: '#ec4899',
        role: 'Regional Manager', status: 'Active', assignedDate: '10 Dec 2023',
      },
      {
        id: 'USR-003', name: 'Emily Chen', email: 'echen@geotrack.com',
        initials: 'EC', avatarColor: '#10b981',
        role: 'Viewer', status: 'Inactive', assignedDate: '20 Nov 2023',
      },
    ],
    createdBy: 'System Admin',
    createdDate: '15 Nov 2023',
    lastUpdated: '01 Apr 2024',
    activityScore: 68,
    activityLabel: 'Active',
    processingLabel: 'Normal',
    pendingRequests: 2,
    avgProcessingTime: 3.2,
  },
  'REG-007': {
    fullName: 'Podunavlje, Srbija',
    internalRefId: 'REG-SRB-007',
    shortSummary: 'River valleys with medieval fortresses and scenic gorges.',
    fullDescription:
      'Podunavlje is a district in central Serbia along the Danube river. The region features scenic river landscapes, medieval fortresses, and the famous Đerdap gorge.',
    team: [
      {
        id: 'USR-006', name: 'Stefan Đorđević', email: 's.djordjevic@geotrack.com',
        initials: 'SĐ', avatarColor: '#06b6d4',
        role: 'Regional Manager', status: 'Active', assignedDate: '18 Jun 2023',
      },
    ],
    createdBy: 'System Admin',
    createdDate: '20 Oct 2023',
    lastUpdated: '15 Feb 2024',
    activityScore: 55,
    activityLabel: 'Moderate',
    processingLabel: 'Normal',
    pendingRequests: 1,
    avgProcessingTime: 4.0,
  },
  'REG-008': {
    fullName: 'Timočka Krajina, Srbija',
    internalRefId: 'REG-SRB-008',
    shortSummary: 'Remote wilderness with rich mining heritage and cave systems.',
    fullDescription:
      'Timočka Krajina is a region in eastern Serbia along the Timok river. Known for its wild landscapes, rich mining history, and the spectacular Rajac and Zlot cave systems.',
    team: [
      {
        id: 'USR-010', name: 'Ivan Vasić', email: 'i.vasic@geotrack.com',
        initials: 'IV', avatarColor: '#14b8a6',
        role: 'Viewer', status: 'Active', assignedDate: '20 Mar 2024',
      },
    ],
    createdBy: 'System Admin',
    createdDate: '01 Dec 2023',
    lastUpdated: '25 Mar 2024',
    activityScore: 42,
    activityLabel: 'Low Activity',
    processingLabel: 'Slow',
    pendingRequests: 0,
    avgProcessingTime: 5.5,
  },
  'REG-009': {
    fullName: 'Zlatiborski okrug, Srbija',
    internalRefId: 'REG-SRB-009',
    shortSummary: 'Alpine meadows, ski resorts, and traditional ethno-villages.',
    fullDescription:
      'Zlatibor district is located in southwestern Serbia and includes the famous Zlatibor mountain resort, Tara National Park, and traditional ethno-villages that attract tourists year-round.',
    team: [
      {
        id: 'USR-009', name: 'Dragana Nikolić', email: 'd.nikolic@geotrack.com',
        initials: 'DN', avatarColor: '#ec4899',
        role: 'Regional Manager', status: 'Active', assignedDate: '10 Dec 2023',
      },
    ],
    createdBy: 'System Admin',
    createdDate: '08 Nov 2023',
    lastUpdated: '12 Mar 2024',
    activityScore: 76,
    activityLabel: 'Active',
    processingLabel: 'Normal',
    pendingRequests: 4,
    avgProcessingTime: 2.9,
  },
  'REG-010': {
    fullName: 'Nišavski okrug, Srbija',
    internalRefId: 'REG-SRB-010',
    shortSummary: 'Urban cultural hub with ancient Roman sites and fortress.',
    fullDescription:
      'Nišava district is centered around Niš, the third-largest city in Serbia. The region is rich in Roman history, featuring the famous Niš Fortress, Skull Tower, and the birthplace of Constantine the Great.',
    team: [
      {
        id: 'USR-012', name: 'Petar Jovanović', email: 'p.jovanovic@geotrack.com',
        initials: 'PJ', avatarColor: '#0ea5e9',
        role: 'Viewer', status: 'Inactive', assignedDate: '15 Jan 2024',
      },
    ],
    createdBy: 'System Admin',
    createdDate: '10 Dec 2023',
    lastUpdated: '20 Jan 2024',
    activityScore: 38,
    activityLabel: 'Low Activity',
    processingLabel: 'Slow',
    pendingRequests: 0,
    avgProcessingTime: 6.0,
  },
};

export function buildDetail(item: RegionListItem): RegionDetail {
  const ov = DETAIL_OVERRIDES[item.id] ?? {};
  return {
    ...item,
    fullName:         ov.fullName         ?? `${item.name}, Srbija`,
    internalRefId:    ov.internalRefId    ?? item.id,
    shortSummary:     ov.shortSummary     ?? item.description,
    fullDescription:  ov.fullDescription  ?? item.description,
    team:             ov.team             ?? [],
    createdBy:        ov.createdBy        ?? 'System Admin',
    createdDate:      ov.createdDate      ?? '01 Jan 2024',
    lastUpdated:      ov.lastUpdated      ?? '01 Jan 2024',
    activityScore:    ov.activityScore    ?? 70,
    activityLabel:    ov.activityLabel    ?? 'Active',
    processingLabel:  ov.processingLabel  ?? 'Normal',
    pendingRequests:  ov.pendingRequests  ?? 0,
    avgProcessingTime: ov.avgProcessingTime ?? 3.0,
  };
}

@Injectable({ providedIn: 'root' })
export class RegionsService {
  readonly lastPage$ = new BehaviorSubject<RegionPage | null>(null);
  getPage$(req: RegionPageRequest): Observable<RegionPage> {
    let filtered = ALL_REGIONS.filter(r => {
      if (!req.search) return true;
      const q = req.search.toLowerCase();
      return r.name.toLowerCase().includes(q) || r.description.toLowerCase().includes(q);
    });

    filtered = filtered.sort((a, b) => {
      let cmp = 0;
      if (req.sortField === 'name')            cmp = a.name.localeCompare(b.name);
      else if (req.sortField === 'status')     cmp = a.status.localeCompare(b.status);
      else if (req.sortField === 'assignedPersons') cmp = a.assignedPersons - b.assignedPersons;
      else if (req.sortField === 'destinations')    cmp = a.destinations - b.destinations;
      return req.sortDir === 'asc' ? cmp : -cmp;
    });

    const total = filtered.length;
    const items = filtered.slice(0, req.pageSize);
    return of({ items, total }).pipe(tap(page => this.lastPage$.next(page)),);
  }

  delete$(id: string): Observable<void> {
    const idx = ALL_REGIONS.findIndex(r => r.id === id);
    if (idx !== -1) ALL_REGIONS.splice(idx, 1);
    return of(undefined).pipe(delay(500));
  }

  getDetail$(id: string): Observable<RegionDetail | null> {
    const item = ALL_REGIONS.find(r => r.id === id) ?? null;
    const detail = item ? buildDetail(item) : null;
    return of(detail).pipe(delay(350));
  }

  updateDetail$(id: string, patch: Partial<RegionDetail>): Observable<RegionDetail | null> {
    const idx = ALL_REGIONS.findIndex(r => r.id === id);
    if (idx === -1) return of(null).pipe(delay(500));

    const listFields: (keyof RegionListItem)[] = [
      'name', 'description', 'isTop', 'destinations', 'places',
      'locations', 'events', 'assignedPersons', 'status',
    ];
    listFields.forEach(f => {
      if (f in patch) (ALL_REGIONS[idx] as any)[f] = (patch as any)[f];
    });

    DETAIL_OVERRIDES[id] = { ...(DETAIL_OVERRIDES[id] ?? {}), ...patch };

    const updated = buildDetail(ALL_REGIONS[idx]);
    return of(updated).pipe(delay(500));
  }

  updateTeam$(id: string, team: RegionTeamMember[]): Observable<void> {
    DETAIL_OVERRIDES[id] = { ...(DETAIL_OVERRIDES[id] ?? {}), team };
    return of(undefined).pipe(delay(300));
  }

  private _listState: { searchQuery: string; sortField: string; sortDir: string; displayCount: number; scrollTop: number } | null = null;
  saveListState(s: { searchQuery: string; sortField: string; sortDir: string; displayCount: number; scrollTop: number }): void { this._listState = s; }
  getListState() { return this._listState; }
  clearListState(): void { this._listState = null; }
}