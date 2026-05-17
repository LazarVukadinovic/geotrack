import { Injectable } from '@angular/core';
import { Observable, BehaviorSubject, of } from 'rxjs';
import { delay, tap } from 'rxjs/operators';
import {
  AdminReview, AdminReviewsPage, AdminReviewsPageRequest,
  ReviewNotification,
} from '../../models/administrator/admin-review.model';

const COLORS = ['#3b82f6', '#10b981', '#8b5cf6', '#f59e0b', '#ef4444', '#06b6d4', '#ec4899', '#14b8a6'];
const color = (i: number) => COLORS[i % COLORS.length];

const REPORT_REASONS = [
  'Inappropriate language', 'Spam or advertising', 'Harassment or hate speech',
  'False information', 'Off-topic content', 'Personal attack',
];

let ALL_REVIEWS: AdminReview[] = [
  { id: 'REV-001', targetType: 'Destination', targetName: 'Kalemegdan Fortress', rating: 5, content: 'Amazing place with breathtaking views of the Danube and Sava confluence. A must-visit!', authorId: 'AU-007', authorName: 'Petar Lukić', authorEmail: 'petar.lukic@yahoo.com', authorInitials: 'PL', authorAvatarColor: color(6), status: 'Active', reportCount: 0, reportReason: null, createdAt: '2026-04-10T11:00:00Z', reportedAt: null },
  { id: 'REV-002', targetType: 'Place', targetName: 'Skadarlija', rating: 4, content: 'Charming bohemian quarter with great restaurants.', authorId: 'AU-002', authorName: 'Milica Petrović', authorEmail: 'm.petrovic@mail.com', authorInitials: 'MP', authorAvatarColor: color(1), status: 'Active', reportCount: 0, reportReason: null, createdAt: '2026-04-12T14:00:00Z', reportedAt: null },
  { id: 'REV-003', targetType: 'Destination', targetName: 'Đerdap National Park', rating: 1, content: 'Total waste of money. The staff are completely useless idiots. AVOID!!!!', authorId: 'AU-005', authorName: 'Marko Savić', authorEmail: 'm.savic@outlook.com', authorInitials: 'MS', authorAvatarColor: color(4), status: 'Reported', reportCount: 4, reportReason: REPORT_REASONS[2], createdAt: '2026-03-20T09:00:00Z', reportedAt: '2026-03-21T10:15:00Z' },
  { id: 'REV-004', targetType: 'Event', targetName: 'Exit Music Festival', rating: 5, content: 'Best festival experience of my life. World class lineup!', authorId: 'AU-013', authorName: 'Aleksandar Simić', authorEmail: 'a.simic@webmail.rs', authorInitials: 'AS', authorAvatarColor: color(4), status: 'Active', reportCount: 0, reportReason: null, createdAt: '2026-04-22T18:00:00Z', reportedAt: null },
  { id: 'REV-005', targetType: 'Location', targetName: 'Tara National Park', rating: 2, content: 'Buy followers here: spamlink.biz/followers GET 10K FOLLOWERS NOW!!!', authorId: 'AU-005', authorName: 'Marko Savić', authorEmail: 'm.savic@outlook.com', authorInitials: 'MS', authorAvatarColor: color(4), status: 'Reported', reportCount: 6, reportReason: REPORT_REASONS[1], createdAt: '2026-03-22T10:00:00Z', reportedAt: '2026-03-22T12:00:00Z' },
  { id: 'REV-006', targetType: 'Place', targetName: 'Ada Ciganlija', rating: 4, content: 'Great beach for swimming. Gets very crowded on weekends though.', authorId: 'AU-008', authorName: 'Ivana Popović', authorEmail: 'ivana.popovic@live.com', authorInitials: 'IP', authorAvatarColor: color(7), status: 'Active', reportCount: 0, reportReason: null, createdAt: '2026-04-30T12:00:00Z', reportedAt: null },
  { id: 'REV-007', targetType: 'Destination', targetName: 'Novi Sad City Center', rating: 3, content: 'Decent city but nothing special. Overpriced for what it is.', authorId: 'AU-003', authorName: 'Stefan Nikolić', authorEmail: 's.nikolic@webmail.rs', authorInitials: 'SN', authorAvatarColor: color(2), status: 'Reported', reportCount: 2, reportReason: REPORT_REASONS[4], createdAt: '2026-04-05T15:00:00Z', reportedAt: '2026-04-06T09:00:00Z' },
  { id: 'REV-008', targetType: 'Event', targetName: 'Nišville Jazz Festival', rating: 5, content: 'Wonderful atmosphere and great musicians from around the world.', authorId: 'AU-016', authorName: 'Tatjana Mitić', authorEmail: 't.mitic@mail.com', authorInitials: 'TM', authorAvatarColor: color(7), status: 'Active', reportCount: 0, reportReason: null, createdAt: '2026-05-01T20:00:00Z', reportedAt: null },
  { id: 'REV-009', targetType: 'Place', targetName: 'Zlatibor Mountain', rating: 1, content: 'Disgusting place run by [ethnic slur removed] who don\'t know what hospitality means!', authorId: 'AU-009', authorName: 'Dragan Vasić', authorEmail: 'd.vasic@webmail.com', authorInitials: 'DV', authorAvatarColor: color(0), status: 'Reported', reportCount: 8, reportReason: REPORT_REASONS[2], createdAt: '2026-02-10T08:00:00Z', reportedAt: '2026-02-10T09:30:00Z' },
  { id: 'REV-010', targetType: 'Destination', targetName: 'Studenica Monastery', rating: 5, content: 'Spiritual and architecturally stunning. One of Serbia\'s crown jewels.', authorId: 'AU-011', authorName: 'Vladimir Kovačević', authorEmail: 'v.kovacevic@mail.com', authorInitials: 'VK', authorAvatarColor: color(2), status: 'Active', reportCount: 0, reportReason: null, createdAt: '2026-04-18T10:00:00Z', reportedAt: null },
  { id: 'REV-011', targetType: 'Location', targetName: 'Uvac Special Nature Reserve', rating: 4, content: 'Stunning meanders and we saw griffon vultures up close. Worth every bit.', authorId: 'AU-019', authorName: 'Nemanja Todorović', authorEmail: 'n.todorovic@gmail.com', authorInitials: 'NT', authorAvatarColor: color(2), status: 'Active', reportCount: 0, reportReason: null, createdAt: '2026-05-02T09:00:00Z', reportedAt: null },
  { id: 'REV-012', targetType: 'Place', targetName: 'Petrovaradin Fortress', rating: 2, content: 'Completely false reviews on this site. This place is nothing. DO NOT TRUST GEOTRACK REVIEWS.', authorId: 'AU-015', authorName: 'Bojan Đurić', authorEmail: 'bojan.djuric@gmail.com', authorInitials: 'BĐ', authorAvatarColor: color(6), status: 'Reported', reportCount: 5, reportReason: REPORT_REASONS[3], createdAt: '2026-01-15T11:00:00Z', reportedAt: '2026-01-16T14:00:00Z' },
  { id: 'REV-013', targetType: 'Destination', targetName: 'Kopaonik National Park', rating: 5, content: 'World class ski resort with excellent infrastructure and beautiful nature.', authorId: 'AU-013', authorName: 'Aleksandar Simić', authorEmail: 'a.simic@webmail.rs', authorInitials: 'AS', authorAvatarColor: color(4), status: 'Active', reportCount: 0, reportReason: null, createdAt: '2026-03-10T14:00:00Z', reportedAt: null },
  { id: 'REV-014', targetType: 'Event', targetName: 'Belgrade Beer Fest', rating: 4, content: 'Great event, nice atmosphere and decent beer selection.', authorId: 'AU-001', authorName: 'Nikola Jovanović', authorEmail: 'n.jovanovic@mail.com', authorInitials: 'NJ', authorAvatarColor: color(0), status: 'Reported', reportCount: 3, reportReason: REPORT_REASONS[0], createdAt: '2026-04-28T19:00:00Z', reportedAt: '2026-04-29T10:00:00Z' },
  { id: 'REV-015', targetType: 'Place', targetName: 'Đavolja Varoš', rating: 5, content: 'Truly unique rock formations unlike anything else I have seen.', authorId: 'AU-020', authorName: 'Dragana Vučić', authorEmail: 'd.vucic@mail.com', authorInitials: 'DV', authorAvatarColor: color(3), status: 'Active', reportCount: 0, reportReason: null, createdAt: '2026-05-05T12:00:00Z', reportedAt: null },
  { id: 'REV-016', targetType: 'Destination', targetName: 'Niš Fortress', rating: 3, content: 'Historic but poorly maintained. Needs more investment and better signage.', authorId: 'AU-017', authorName: 'Goran Marinković', authorEmail: 'g.marinkovic@rs.com', authorInitials: 'GM', authorAvatarColor: color(0), status: 'Active', reportCount: 1, reportReason: null, createdAt: '2026-04-15T13:00:00Z', reportedAt: null },
  { id: 'REV-017', targetType: 'Location', targetName: 'Fruška Gora', rating: 4, content: 'Beautiful monasteries and vineyards. A peaceful escape from the city.', authorId: 'AU-012', authorName: 'Nataša Radović', authorEmail: 'n.radovic@email.rs', authorInitials: 'NR', authorAvatarColor: color(3), status: 'Active', reportCount: 0, reportReason: null, createdAt: '2026-04-20T11:00:00Z', reportedAt: null },
  { id: 'REV-018', targetType: 'Place', targetName: 'Skadarlija', rating: 1, content: 'Overpriced tourist trap. Nothing authentic about it. Total scam!', authorId: 'AU-009', authorName: 'Dragan Vasić', authorEmail: 'd.vasic@webmail.com', authorInitials: 'DV', authorAvatarColor: color(0), status: 'Reported', reportCount: 4, reportReason: REPORT_REASONS[4], createdAt: '2026-02-05T10:00:00Z', reportedAt: '2026-05-15T14:30:00Z' },
  { id: 'REV-019', targetType: 'Destination', targetName: 'Ada Bojana', rating: 5, content: 'Hidden gem! Pristine nature, amazing seafood and wonderful people.', authorId: 'AU-004', authorName: 'Ana Đorđević', authorEmail: 'ana.djordjevic@gmail.com', authorInitials: 'AĐ', authorAvatarColor: color(3), status: 'Active', reportCount: 0, reportReason: null, createdAt: '2026-05-08T15:00:00Z', reportedAt: null },
  { id: 'REV-020', targetType: 'Event', targetName: 'Guča Trumpet Festival', rating: 4, content: 'Incredible cultural experience. Lively, loud and unforgettable!', authorId: 'AU-006', authorName: 'Jelena Stanković', authorEmail: 'j.stankovic@email.rs', authorInitials: 'JS', authorAvatarColor: color(5), status: 'Reported', reportCount: 2, reportReason: REPORT_REASONS[0], createdAt: '2026-05-15T09:00:00Z', reportedAt: '2026-05-15T11:00:00Z' },
];


let NOTIFICATIONS: ReviewNotification[] = [
  { id: 'NOTIF-001', reviewId: 'REV-020', authorName: 'Jelena Stanković', targetName: 'Guča Trumpet Festival', targetType: 'Event', reportedAt: '2026-05-15T11:00:00Z', isRead: false },
  { id: 'NOTIF-002', reviewId: 'REV-018', authorName: 'Dragan Vasić', targetName: 'Skadarlija', targetType: 'Place', reportedAt: '2026-05-15T14:30:00Z', isRead: false },
  { id: 'NOTIF-003', reviewId: 'REV-014', authorName: 'Nikola Jovanović', targetName: 'Belgrade Beer Fest', targetType: 'Event', reportedAt: '2026-04-29T10:00:00Z', isRead: true },
  { id: 'NOTIF-004', reviewId: 'REV-007', authorName: 'Stefan Nikolić', targetName: 'Novi Sad City Center', targetType: 'Destination', reportedAt: '2026-04-06T09:00:00Z', isRead: true },
  { id: 'NOTIF-005', reviewId: 'REV-012', authorName: 'Bojan Đurić', targetName: 'Petrovaradin Fortress', targetType: 'Place', reportedAt: '2026-01-16T14:00:00Z', isRead: true },
];

@Injectable({ providedIn: 'root' })
export class AdminReviewsService {
  readonly lastPage$ = new BehaviorSubject<AdminReviewsPage | null>(null);
  readonly notifications$ = new BehaviorSubject<ReviewNotification[]>(NOTIFICATIONS);

  getPage$(req: AdminReviewsPageRequest): Observable<AdminReviewsPage> {
    let filtered = ALL_REVIEWS.filter(r => r.status !== 'Deleted').filter(r => {
      const matchSearch = !req.search || [r.authorName, r.targetName, r.content]
        .join(' ').toLowerCase().includes(req.search.toLowerCase());
      const matchStatus = req.statusFilter === 'all'
        || (req.statusFilter === 'reported' && r.status === 'Reported')
        || (req.statusFilter === 'active' && r.status === 'Active');
      const matchType = !req.typeFilter?.length || req.typeFilter.includes(r.targetType);
      return matchSearch && matchStatus && matchType;
    });

    filtered = filtered.sort((a, b) => {
      let cmp = 0;
      switch (req.sortField) {
        case 'createdAt': cmp = a.createdAt.localeCompare(b.createdAt); break;
        case 'reportedAt': cmp = (a.reportedAt ?? '').localeCompare(b.reportedAt ?? ''); break;
        case 'rating': cmp = a.rating - b.rating; break;
        case 'reportCount': cmp = a.reportCount - b.reportCount; break;
      }
      return req.sortDir === 'asc' ? cmp : -cmp;
    });

    const total = filtered.length;
    const reportedCount = ALL_REVIEWS.filter(r => r.status === 'Reported').length;
    const items = filtered.slice(0, req.pageSize);
    const page = { items, total, reportedCount };
    return of(page).pipe(
      delay(350),
      tap(p => this.lastPage$.next(p)),
    );
  }

  getUnreadCount(): number {
    return NOTIFICATIONS.filter(n => !n.isRead).length;
  }

  getNotifications(): ReviewNotification[] {
    return [...NOTIFICATIONS].sort((a, b) => b.reportedAt.localeCompare(a.reportedAt));
  }

  markAllRead(): void {
    NOTIFICATIONS = NOTIFICATIONS.map(n => ({ ...n, isRead: true }));
    this.notifications$.next([...NOTIFICATIONS]);
  }

  markRead(id: string): void {
    const idx = NOTIFICATIONS.findIndex(n => n.id === id);
    if (idx !== -1) NOTIFICATIONS[idx] = { ...NOTIFICATIONS[idx], isRead: true };
    this.notifications$.next([...NOTIFICATIONS]);
  }

  deleteReview$(id: string): Observable<void> {
    const idx = ALL_REVIEWS.findIndex(r => r.id === id);
    if (idx !== -1) ALL_REVIEWS[idx] = { ...ALL_REVIEWS[idx], status: 'Deleted' };
    this.lastPage$.next(null);
    return of(undefined).pipe(delay(400));
  }

  getTotalCount(): number { return ALL_REVIEWS.filter(r => r.status !== 'Deleted').length; }
  getReportedCount(): number { return ALL_REVIEWS.filter(r => r.status === 'Reported').length; }

  formatDate(iso: string): string {
    try {
      return new Date(iso).toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' });
    } catch {
      return iso;
    }
  }

  formatDatetime(iso: string): string {
    try {
      return new Date(iso).toLocaleString('en-US', {
        year: 'numeric', month: 'short', day: 'numeric',
        hour: '2-digit', minute: '2-digit',
      });
    } catch {
      return iso;
    }
  }
}
