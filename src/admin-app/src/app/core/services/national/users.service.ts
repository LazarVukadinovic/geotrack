import { Injectable } from '@angular/core';
import { Observable, of, BehaviorSubject } from 'rxjs';
import { delay, tap } from 'rxjs/operators';
import { PlatformUser, PlatformUserRole, PlatformUserStatus } from '../../models/platform-user.model';

export interface UsersPage {
  items: PlatformUser[];
  total: number;
}

export interface UsersPageRequest {
  pageSize: number;
  search: string;
}

const MOCK_USERS: PlatformUser[] = [
  {
    id: 'USR-001', firstName: 'Sarah', lastName: 'Jenkins',
    email: 'sarah.j@geotrack.com', avatarInitials: 'SJ', avatarColor: '#3b82f6',
    role: 'Regional Manager', status: 'Active',
    assignedRegions: ['Šumadija'],
    lastLogin: '2026-05-10T12:30:00Z', createdAt: '2023-09-01',
  },
  {
    id: 'USR-002', firstName: 'Michael', lastName: 'Roberts',
    email: 'm.roberts@geotrack.com', avatarInitials: 'MR', avatarColor: '#8b5cf6',
    role: 'Viewer', status: 'Active',
    assignedRegions: ['Banat'],
    lastLogin: '2026-05-05T09:15:00Z', createdAt: '2023-10-15',
  },
  {
    id: 'USR-003', firstName: 'Emily', lastName: 'Chen',
    email: 'echen@geotrack.com', avatarInitials: 'EC', avatarColor: '#10b981',
    role: 'Viewer', status: 'Inactive',
    assignedRegions: ['Bačka', 'Mačva'],
    lastLogin: '2026-03-01T17:45:00Z', createdAt: '2023-11-20',
  },
  {
    id: 'USR-004', firstName: 'Nikola', lastName: 'Petrović',
    email: 'n.petrovic@geotrack.com', avatarInitials: 'NP', avatarColor: '#f59e0b',
    role: 'Regional Manager', status: 'Active',
    assignedRegions: ['Pomoravlje'],
    lastLogin: '2026-05-09T08:00:00Z', createdAt: '2023-08-12',
  },
  {
    id: 'USR-005', firstName: 'Ana', lastName: 'Kovačević',
    email: 'a.kovacevic@geotrack.com', avatarInitials: 'AK', avatarColor: '#ef4444',
    role: 'Regional Manager', status: 'Active',
    assignedRegions: ['Raška'],
    lastLogin: '2026-05-08T14:20:00Z', createdAt: '2023-07-30',
  },
  {
    id: 'USR-006', firstName: 'Stefan', lastName: 'Đorđević',
    email: 's.djordjevic@geotrack.com', avatarInitials: 'SĐ', avatarColor: '#06b6d4',
    role: 'Regional Manager', status: 'Active',
    assignedRegions: ['Podunavlje'],
    lastLogin: '2026-05-07T11:10:00Z', createdAt: '2023-06-18',
  },
  {
    id: 'USR-007', firstName: 'Milica', lastName: 'Stanković',
    email: 'm.stankovic@geotrack.com', avatarInitials: 'MS', avatarColor: '#84cc16',
    role: 'Viewer', status: 'Active',
    assignedRegions: [],
    lastLogin: '2026-05-06T16:55:00Z', createdAt: '2024-01-05',
  },
  {
    id: 'USR-008', firstName: 'Jovan', lastName: 'Milošević',
    email: 'j.milosevic@geotrack.com', avatarInitials: 'JM', avatarColor: '#f97316',
    role: 'Viewer', status: 'Inactive',
    assignedRegions: [],
    lastLogin: '2026-02-14T10:30:00Z', createdAt: '2024-02-01',
  },
  {
    id: 'USR-009', firstName: 'Dragana', lastName: 'Nikolić',
    email: 'd.nikolic@geotrack.com', avatarInitials: 'DN', avatarColor: '#ec4899',
    role: 'Regional Manager', status: 'Active',
    assignedRegions: ['Zlatiborski okrug', 'Mačva'],
    lastLogin: '2026-05-10T07:45:00Z', createdAt: '2023-12-10',
  },
  {
    id: 'USR-010', firstName: 'Ivan', lastName: 'Vasić',
    email: 'i.vasic@geotrack.com', avatarInitials: 'IV', avatarColor: '#14b8a6',
    role: 'Viewer', status: 'Active',
    assignedRegions: ['Timočka Krajina'],
    lastLogin: '2026-05-04T13:00:00Z', createdAt: '2024-03-20',
  },
  {
    id: 'USR-011', firstName: 'Katarina', lastName: 'Vuković',
    email: 'k.vukovic@geotrack.com', avatarInitials: 'KV', avatarColor: '#a855f7',
    role: 'Viewer', status: 'Active',
    assignedRegions: [],
    lastLogin: '2026-05-03T09:20:00Z', createdAt: '2024-04-01',
  },
  {
    id: 'USR-012', firstName: 'Petar', lastName: 'Jovanović',
    email: 'p.jovanovic@geotrack.com', avatarInitials: 'PJ', avatarColor: '#0ea5e9',
    role: 'Viewer', status: 'Inactive',
    assignedRegions: ['Nišavski okrug'],
    lastLogin: '2026-01-20T15:10:00Z', createdAt: '2024-01-15',
  },
  {
    id: 'USR-013', firstName: 'Bojana', lastName: 'Lazić',
    email: 'b.lazic@geotrack.com', avatarInitials: 'BL', avatarColor: '#f43f5e',
    role: 'Regional Manager', status: 'Active',
    assignedRegions: ['Banat', 'Bačka'],
    lastLogin: '2026-05-09T17:30:00Z', createdAt: '2023-10-01',
  },
  {
    id: 'USR-014', firstName: 'Tamara', lastName: 'Nikolov',
    email: 't.nikolov@geotrack.com', avatarInitials: 'TN', avatarColor: '#64748b',
    role: 'Viewer', status: 'Active',
    assignedRegions: [],
    lastLogin: '2026-05-02T08:45:00Z', createdAt: '2024-05-01',
  },
  {
    id: 'USR-015', firstName: 'Luka', lastName: 'Popović',
    email: 'l.popovic@geotrack.com', avatarInitials: 'LP', avatarColor: '#22c55e',
    role: 'Viewer', status: 'Active',
    assignedRegions: ['Raška'],
    lastLogin: '2026-05-01T12:00:00Z', createdAt: '2024-02-28',
  },
];

const ALL_REGIONS = [
  'Šumadija', 'Banat', 'Pomoravlje', 'Raška', 'Bačka',
  'Mačva', 'Podunavlje', 'Timočka Krajina', 'Zlatiborski okrug', 'Nišavski okrug',
];

@Injectable({ providedIn: 'root' })
export class UsersService {

  readonly lastPage$ = new BehaviorSubject<UsersPage | null>(null);

  getAllRegions(): string[] {
    return ALL_REGIONS;
  }

  getPage$(req: UsersPageRequest): Observable<UsersPage> {
    let filtered = [...MOCK_USERS];

    if (req.search.trim()) {
      const q = req.search.toLowerCase();
      filtered = filtered.filter(u =>
        `${u.firstName} ${u.lastName}`.toLowerCase().includes(q) ||
        u.email.toLowerCase().includes(q) ||
        u.role.toLowerCase().includes(q) ||
        u.assignedRegions.some(r => r.toLowerCase().includes(q))
      );
    }

    const total = filtered.length;
    const items = filtered.slice(0, req.pageSize);
    const result = { items, total };

    return of(result).pipe(
      delay(300),
      tap(page => this.lastPage$.next(page)),
    );
  }

  getById(id: string): PlatformUser | null {
    return MOCK_USERS.find(u => u.id === id) ?? null;
  }

  updateUser$(id: string, patch: Partial<PlatformUser>): Observable<PlatformUser | null> {
    const idx = MOCK_USERS.findIndex(u => u.id === id);
    if (idx === -1) return of(null).pipe(delay(300));
    Object.assign(MOCK_USERS[idx], patch);
    return of({ ...MOCK_USERS[idx] }).pipe(delay(300));
  }

  promoteToManager$(id: string): Observable<PlatformUser | null> {
    return this.updateUser$(id, { role: 'Regional Manager', status: 'Active' });
  }

  removeManagerRole$(id: string): Observable<PlatformUser | null> {
    return this.updateUser$(id, { role: 'Viewer', status: 'Inactive' });
  }

  addRegion$(id: string, region: string): Observable<PlatformUser | null> {
    const user = MOCK_USERS.find(u => u.id === id);
    if (!user) return of(null).pipe(delay(300));
    if (!user.assignedRegions.includes(region)) {
      user.assignedRegions = [...user.assignedRegions, region];
    }
    return of({ ...user }).pipe(delay(300));
  }

  removeRegion$(id: string, region: string): Observable<PlatformUser | null> {
    const user = MOCK_USERS.find(u => u.id === id);
    if (!user) return of(null).pipe(delay(300));
    user.assignedRegions = user.assignedRegions.filter(r => r !== region);
    return of({ ...user }).pipe(delay(300));
  }

  createUser$(data: Omit<PlatformUser, 'id' | 'avatarInitials' | 'avatarColor' | 'lastLogin' | 'createdAt'>): Observable<PlatformUser> {
    const colors = ['#3b82f6', '#8b5cf6', '#10b981', '#f59e0b', '#ef4444', '#06b6d4', '#ec4899', '#f97316'];
    const initials = (data.firstName[0] + data.lastName[0]).toUpperCase();
    const newUser: PlatformUser = {
      ...data,
      id: 'USR-' + String(MOCK_USERS.length + 1).padStart(3, '0'),
      avatarInitials: initials,
      avatarColor: colors[MOCK_USERS.length % colors.length],
      lastLogin: '—',
      createdAt: new Date().toISOString().split('T')[0],
    };
    MOCK_USERS.push(newUser);
    return of(newUser).pipe(delay(400));
  }

  formatLastLogin(iso: string): string {
    if (iso === '—') return '—';
    const d = new Date(iso);
    if (isNaN(d.getTime())) return iso;
    const now = new Date();
    const diffMs = now.getTime() - d.getTime();
    const diffH = Math.floor(diffMs / 3600000);
    if (diffH < 1) return 'Just now';
    if (diffH < 24) return `${diffH} hr${diffH > 1 ? 's' : ''} ago`;
    const diffD = Math.floor(diffH / 24);
    if (diffD < 30) return `${diffD} day${diffD > 1 ? 's' : ''} ago`;
    const diffM = Math.floor(diffD / 30);
    return `${diffM} mo${diffM > 1 ? 's' : ''} ago`;
  }
}