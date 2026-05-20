import { Injectable } from '@angular/core';
import { Observable, BehaviorSubject, of } from 'rxjs';
import { delay, tap } from 'rxjs/operators';
import {
  AppUser, AppUsersPage, AppUsersPageRequest,
} from '../../models/administrator/app-user.model';

const COLORS = ['#3b82f6', '#10b981', '#8b5cf6', '#f59e0b', '#ef4444', '#06b6d4', '#ec4899', '#14b8a6'];
const color = (i: number) => COLORS[i % COLORS.length];
const initials = (f: string, l: string) => `${f[0]}${l[0]}`.toUpperCase();

let APP_USERS: AppUser[] = [
  { id: 'AU-001', firstName: 'Nikola', lastName: 'Jovanović', email: 'n.jovanovic@mail.com', avatarInitials: 'NJ', avatarColor: color(0), status: 'Active', reviewCount: 24, reportedReviewCount: 3, joinedAt: '2023-05-12T10:00:00Z', lastActiveAt: '2026-05-10T14:23:00Z' },
  { id: 'AU-002', firstName: 'Milica', lastName: 'Petrović', email: 'm.petrovic@mail.com', avatarInitials: 'MP', avatarColor: color(1), status: 'Active', reviewCount: 8, reportedReviewCount: 0, joinedAt: '2023-08-20T09:00:00Z', lastActiveAt: '2026-05-14T11:05:00Z' },
  { id: 'AU-003', firstName: 'Stefan', lastName: 'Nikolić', email: 's.nikolic@webmail.rs', avatarInitials: 'SN', avatarColor: color(2), status: 'Active', reviewCount: 51, reportedReviewCount: 7, joinedAt: '2022-11-03T08:00:00Z', lastActiveAt: '2026-05-15T08:47:00Z' },
  { id: 'AU-004', firstName: 'Ana', lastName: 'Đorđević', email: 'ana.djordjevic@gmail.com', avatarInitials: 'AĐ', avatarColor: color(3), status: 'Active', reviewCount: 3, reportedReviewCount: 0, joinedAt: '2024-01-10T12:00:00Z', lastActiveAt: '2026-04-30T16:00:00Z' },
  { id: 'AU-005', firstName: 'Marko', lastName: 'Savić', email: 'm.savic@outlook.com', avatarInitials: 'MS', avatarColor: color(4), status: 'Suspended', reviewCount: 18, reportedReviewCount: 12, joinedAt: '2023-02-14T07:00:00Z', lastActiveAt: '2026-03-20T10:00:00Z' },
  { id: 'AU-006', firstName: 'Jelena', lastName: 'Stanković', email: 'j.stankovic@email.rs', avatarInitials: 'JS', avatarColor: color(5), status: 'Active', reviewCount: 6, reportedReviewCount: 1, joinedAt: '2023-09-05T14:00:00Z', lastActiveAt: '2026-05-13T19:30:00Z' },
  { id: 'AU-007', firstName: 'Petar', lastName: 'Lukić', email: 'petar.lukic@yahoo.com', avatarInitials: 'PL', avatarColor: color(6), status: 'Active', reviewCount: 30, reportedReviewCount: 0, joinedAt: '2022-06-22T11:00:00Z', lastActiveAt: '2026-05-14T22:10:00Z' },
  { id: 'AU-008', firstName: 'Ivana', lastName: 'Popović', email: 'ivana.popovic@live.com', avatarInitials: 'IP', avatarColor: color(7), status: 'Active', reviewCount: 12, reportedReviewCount: 2, joinedAt: '2023-12-01T09:30:00Z', lastActiveAt: '2026-05-12T13:45:00Z' },
  { id: 'AU-009', firstName: 'Dragan', lastName: 'Vasić', email: 'd.vasic@webmail.com', avatarInitials: 'DV', avatarColor: color(0), status: 'Suspended', reviewCount: 42, reportedReviewCount: 18, joinedAt: '2022-03-17T08:00:00Z', lastActiveAt: '2026-02-10T09:00:00Z' },
  { id: 'AU-010', firstName: 'Sandra', lastName: 'Ilić', email: 's.ilic@gmail.com', avatarInitials: 'SI', avatarColor: color(1), status: 'Active', reviewCount: 5, reportedReviewCount: 0, joinedAt: '2024-03-22T14:00:00Z', lastActiveAt: '2026-05-15T10:00:00Z' },
  { id: 'AU-011', firstName: 'Vladimir', lastName: 'Kovačević', email: 'v.kovacevic@mail.com', avatarInitials: 'VK', avatarColor: color(2), status: 'Active', reviewCount: 19, reportedReviewCount: 1, joinedAt: '2023-07-08T10:00:00Z', lastActiveAt: '2026-05-11T17:20:00Z' },
  { id: 'AU-012', firstName: 'Nataša', lastName: 'Radović', email: 'n.radovic@email.rs', avatarInitials: 'NR', avatarColor: color(3), status: 'Active', reviewCount: 7, reportedReviewCount: 0, joinedAt: '2023-10-15T11:00:00Z', lastActiveAt: '2026-05-08T14:00:00Z' },
  { id: 'AU-013', firstName: 'Aleksandar', lastName: 'Simić', email: 'a.simic@webmail.rs', avatarInitials: 'AS', avatarColor: color(4), status: 'Active', reviewCount: 34, reportedReviewCount: 4, joinedAt: '2022-09-01T09:00:00Z', lastActiveAt: '2026-05-16T08:00:00Z' },
  { id: 'AU-014', firstName: 'Maja', lastName: 'Filipović', email: 'm.filipovic@outlook.com', avatarInitials: 'MF', avatarColor: color(5), status: 'Active', reviewCount: 2, reportedReviewCount: 0, joinedAt: '2024-04-10T13:00:00Z', lastActiveAt: '2026-05-14T09:30:00Z' },
  { id: 'AU-015', firstName: 'Bojan', lastName: 'Đurić', email: 'bojan.djuric@gmail.com', avatarInitials: 'BĐ', avatarColor: color(6), status: 'Suspended', reviewCount: 27, reportedReviewCount: 9, joinedAt: '2022-12-05T10:00:00Z', lastActiveAt: '2026-01-15T11:00:00Z' },
  { id: 'AU-016', firstName: 'Tatjana', lastName: 'Mitić', email: 't.mitic@mail.com', avatarInitials: 'TM', avatarColor: color(7), status: 'Active', reviewCount: 15, reportedReviewCount: 0, joinedAt: '2023-05-28T08:30:00Z', lastActiveAt: '2026-05-15T20:00:00Z' },
  { id: 'AU-017', firstName: 'Goran', lastName: 'Marinković', email: 'g.marinkovic@rs.com', avatarInitials: 'GM', avatarColor: color(0), status: 'Active', reviewCount: 9, reportedReviewCount: 2, joinedAt: '2023-11-12T14:00:00Z', lastActiveAt: '2026-05-10T18:00:00Z' },
  { id: 'AU-018', firstName: 'Zorica', lastName: 'Pavlović', email: 'z.pavlovic@yahoo.com', avatarInitials: 'ZP', avatarColor: color(1), status: 'Active', reviewCount: 4, reportedReviewCount: 0, joinedAt: '2024-02-18T10:00:00Z', lastActiveAt: '2026-05-13T15:45:00Z' },
  { id: 'AU-019', firstName: 'Nemanja', lastName: 'Todorović', email: 'n.todorovic@gmail.com', avatarInitials: 'NT', avatarColor: color(2), status: 'Active', reviewCount: 22, reportedReviewCount: 5, joinedAt: '2023-03-30T09:00:00Z', lastActiveAt: '2026-05-16T07:30:00Z' },
  { id: 'AU-020', firstName: 'Dragana', lastName: 'Vučić', email: 'd.vucic@mail.com', avatarInitials: 'DV', avatarColor: color(3), status: 'Active', reviewCount: 11, reportedReviewCount: 0, joinedAt: '2023-06-14T11:00:00Z', lastActiveAt: '2026-05-09T16:20:00Z' },
];

function compareUsers(a: AppUser, b: AppUser, field: string, dir: string): number {
  let cmp = 0;
  switch (field) {
    case 'name': cmp = `${a.firstName} ${a.lastName}`.localeCompare(`${b.firstName} ${b.lastName}`); break;
    case 'joinedAt': cmp = a.joinedAt.localeCompare(b.joinedAt); break;
    case 'reviewCount': cmp = a.reviewCount - b.reviewCount; break;
    case 'reportedReviewCount': cmp = a.reportedReviewCount - b.reportedReviewCount; break;
    case 'status': cmp = a.status.localeCompare(b.status); break;
  }
  return dir === 'asc' ? cmp : -cmp;
}

@Injectable({ providedIn: 'root' })
export class AppUsersService {
  readonly lastPage$ = new BehaviorSubject<AppUsersPage | null>(null);

  getPage$(req: AppUsersPageRequest): Observable<AppUsersPage> {
    let filtered = APP_USERS.filter(u => {
      const matchSearch = !req.search || [u.firstName, u.lastName, u.email]
        .join(' ').toLowerCase().includes(req.search.toLowerCase());
      const matchStatus = req.statusFilter === 'all'
        || (req.statusFilter === 'active' && u.status === 'Active')
        || (req.statusFilter === 'suspended' && u.status === 'Suspended');
      return matchSearch && matchStatus;
    });

    filtered = filtered.sort((a, b) => compareUsers(a, b, req.sortField, req.sortDir));
    const total = filtered.length;
    const items = filtered.slice(0, req.pageSize);
    const page = { items, total };
    return of(page).pipe(
      delay(350),
      tap(p => this.lastPage$.next(p)),
    );
  }

  getTotalCount(): number { return APP_USERS.length; }
  getSuspendedCount(): number { return APP_USERS.filter(u => u.status === 'Suspended').length; }

  delete$(id: string): Observable<void> {
    APP_USERS = APP_USERS.filter(u => u.id !== id);
    this.lastPage$.next(null);
    return of(undefined).pipe(delay(400));
  }

  suspend$(id: string): Observable<AppUser | null> {
    const idx = APP_USERS.findIndex(u => u.id === id);
    if (idx === -1) return of(null).pipe(delay(300));
    APP_USERS[idx] = { ...APP_USERS[idx], status: 'Suspended' };
    return of({ ...APP_USERS[idx] }).pipe(delay(350));
  }

  formatDate(iso: string): string {
    try {
      return new Date(iso).toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' });
    } catch {
      return iso;
    }
  }
}
