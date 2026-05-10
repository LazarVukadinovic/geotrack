import { Injectable } from '@angular/core';
import { Observable, of } from 'rxjs';
import { delay } from 'rxjs/operators';

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

@Injectable({ providedIn: 'root' })
export class RegionsService {

  getPage$(req: RegionPageRequest): Observable<RegionPage> {
    let filtered = ALL_REGIONS.filter(r => {
      if (!req.search) return true;
      const q = req.search.toLowerCase();
      return r.name.toLowerCase().includes(q) || r.description.toLowerCase().includes(q);
    });

    filtered = filtered.sort((a, b) => {
      let cmp = 0;
      if (req.sortField === 'name') {
        cmp = a.name.localeCompare(b.name);
      } else if (req.sortField === 'status') {
        cmp = a.status.localeCompare(b.status);
      } else if (req.sortField === 'assignedPersons') {
        cmp = a.assignedPersons - b.assignedPersons;
      } else if (req.sortField === 'destinations') {
        cmp = a.destinations - b.destinations;
      }
      return req.sortDir === 'asc' ? cmp : -cmp;
    });

    const total = filtered.length;
    const items = filtered.slice(0, req.pageSize);
    return of({ items, total }).pipe(delay(350));
  }

  delete$(id: string): Observable<void> {
    const idx = ALL_REGIONS.findIndex(r => r.id === id);
    if (idx !== -1) ALL_REGIONS.splice(idx, 1);
    return of(undefined).pipe(delay(500));
  }
}