import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { AdminNotificationsService } from './admin-notifications.service';

describe('AdminNotificationsService', () => {
  let service: AdminNotificationsService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AdminNotificationsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('loads notifications from the admin endpoint and updates unread count', () => {
    service.getNotifications$({ includeRead: true, skip: 0, take: 25 }).subscribe(page => {
      expect(page.items.length).toBe(1);
      expect(page.items[0].title).toBe('New support report');
      expect(page.items[0].targetRoute).toBe('/admin-support-tickets?ticketId=42');
      expect(page.unreadCount).toBe(3);
    });

    const request = http.expectOne(req =>
      req.url === '/api/admin/notifications' &&
      req.params.get('includeRead') === 'true' &&
      req.params.get('skip') === '0' &&
      req.params.get('take') === '25',
    );
    expect(request.request.method).toBe('GET');
    request.flush({
      items: [
        {
          id: 11,
          type: 'SupportTicketCreated',
          title: 'New support report',
          message: 'Location and maps: Map is stuck',
          severity: 'Warning',
          isRead: false,
          createdAt: '2026-05-26T20:00:00Z',
          targetType: 'SupportTicket',
          targetId: 42,
          targetRoute: '/admin-support-tickets?ticketId=42',
        },
      ],
      unreadCount: 3,
      totalCount: 1,
      skip: 0,
      take: 25,
      nextSkip: null,
      hasMore: false,
    });

    expect(service.unreadCount()).toBe(3);
  });

  it('marks one notification as read and stores the backend unread count', () => {
    service.markRead$(11).subscribe(result => {
      expect(result.unreadCount).toBe(2);
      expect(result.readAt).toBe('2026-05-26T21:00:00Z');
    });

    const request = http.expectOne('/api/admin/notifications/11/read');
    expect(request.request.method).toBe('POST');
    request.flush({ unreadCount: 2, readAt: '2026-05-26T21:00:00Z' });

    expect(service.unreadCount()).toBe(2);
  });

  it('marks all notifications as read', () => {
    service.markAllRead$().subscribe(result => {
      expect(result.unreadCount).toBe(0);
    });

    const request = http.expectOne('/api/admin/notifications/mark-all-read');
    expect(request.request.method).toBe('POST');
    request.flush({ unreadCount: 0, readAt: '2026-05-26T21:00:00Z' });

    expect(service.unreadCount()).toBe(0);
  });
});
