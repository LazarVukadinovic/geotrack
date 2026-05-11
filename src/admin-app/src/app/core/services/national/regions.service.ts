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
        id: 'u1', name: 'Jovan Đorđević', email: 'jovan.d@example.com',
        initials: 'JD', avatarColor: '#3b82f6',
        role: 'Regional Admin', status: 'Active', assignedDate: '12 Jan 2024',
      },
      {
        id: 'u2', name: 'Milica Petrović', email: 'milica.p@example.com',
        initials: 'MP', avatarColor: '#8b5cf6',
        role: 'Regional Admin', status: 'Active', assignedDate: '04 Feb 2024',
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
    shortSummary: 'Desert ecosystems, canyons, and arid plateaus.',
    fullDescription:
      'Banat is a geographical and historical region in Central Europe. The Serbian part of Banat is located in the northeastern part of Serbia, comprising flat fertile plains along the Tisa and Danube rivers.',
    team: [
      {
        id: 'u3', name: 'Stefan Nikolić', email: 'stefan.n@example.com',
        initials: 'SN', avatarColor: '#10b981',
        role: 'Regional Admin', status: 'Active', assignedDate: '01 Mar 2024',
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
        id: 'u4', name: 'Ana Jovanović', email: 'ana.j@example.com',
        initials: 'AJ', avatarColor: '#f59e0b',
        role: 'Regional Admin', status: 'Active', assignedDate: '20 Jan 2024',
      },
      {
        id: 'u5', name: 'Marko Stanković', email: 'marko.s@example.com',
        initials: 'MS', avatarColor: '#ef4444',
        role: 'Regional Editor', status: 'Active', assignedDate: '10 Feb 2024',
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