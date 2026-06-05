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

  it('normalizes alternate notification response wrappers without creating fake rows', () => {
    service.getNotifications$({ includeRead: true, skip: 0, take: 25 }).subscribe(page => {
      expect(page.items.length).toBe(1);
      expect(page.items[0].id).toBe(12);
      expect(page.items[0].title).toBe('Pascal case notification');
      expect(page.items[0].isRead).toBe(false);
      expect(page.items[0].targetRoute).toBe('/admin-dashboard');
      expect(page.unreadCount).toBe(1);
      expect(page.totalCount).toBe(1);
      expect(page.hasMore).toBe(false);
    });

    const request = http.expectOne('/api/admin/notifications?includeRead=true&skip=0&take=25');
    expect(request.request.method).toBe('GET');
    request.flush({
      Data: {
        Results: [
          {
            Id: '12',
            Type: 'System',
            Title: 'Pascal case notification',
            Message: 'The response shape is normalized.',
            Severity: 'Info',
            IsRead: 'false',
            CreatedAt: '2026-05-26T20:00:00Z',
            TargetType: 'System',
            TargetId: '99',
            TargetRoute: '/admin-dashboard',
          },
        ],
      },
      UnreadCount: '1',
      Total: '1',
      Skip: '0',
      Take: '25',
      NextSkip: null,
      HasMore: 'false',
    });

    expect(service.unreadCount()).toBe(1);
  });

  it('normalizes unread-count response casing', () => {
    service.getUnreadCount$().subscribe(response => {
      expect(response.unreadCount).toBe(4);
    });

    const request = http.expectOne('/api/admin/notifications/unread-count');
    expect(request.request.method).toBe('GET');
    request.flush({ UnreadCount: '4' });

    expect(service.unreadCount()).toBe(4);
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

  it('passes the active audience role for Territory Manager notifications', () => {
    service.getNotifications$({
      audienceRole: 'TerritoryManager',
      includeRead: true,
      skip: 0,
      take: 25,
    }).subscribe();

    const request = http.expectOne(req =>
      req.url === '/api/admin/notifications' &&
      req.params.get('audienceRole') === 'TerritoryManager');
    expect(request.request.method).toBe('GET');
    request.flush({
      items: [],
      unreadCount: 0,
      totalCount: 0,
      skip: 0,
      take: 25,
      nextSkip: null,
      hasMore: false,
    });
  });

  it('passes the active region for Regional Manager notification requests and mutations', () => {
    service.getNotifications$({
      audienceRole: 'RegionalManager',
      regionId: 7,
      includeRead: true,
      skip: 0,
      take: 25,
    }).subscribe();

    const listRequest = http.expectOne(req =>
      req.url === '/api/admin/notifications' &&
      req.params.get('audienceRole') === 'RegionalManager' &&
      req.params.get('regionId') === '7');
    expect(listRequest.request.method).toBe('GET');
    listRequest.flush({
      items: [],
      unreadCount: 2,
      totalCount: 0,
      skip: 0,
      take: 25,
      nextSkip: null,
      hasMore: false,
    });

    service.getUnreadCount$('RegionalManager', 7).subscribe();
    const countRequest = http.expectOne(req =>
      req.url === '/api/admin/notifications/unread-count' &&
      req.params.get('audienceRole') === 'RegionalManager' &&
      req.params.get('regionId') === '7');
    expect(countRequest.request.method).toBe('GET');
    countRequest.flush({ unreadCount: 2 });

    service.markRead$(11, 'RegionalManager', 7).subscribe();
    const readRequest = http.expectOne(req =>
      req.url === '/api/admin/notifications/11/read' &&
      req.params.get('audienceRole') === 'RegionalManager' &&
      req.params.get('regionId') === '7');
    expect(readRequest.request.method).toBe('POST');
    readRequest.flush({ unreadCount: 1, readAt: '2026-05-26T21:00:00Z' });

    service.markAllRead$('RegionalManager', 7).subscribe();
    const markAllRequest = http.expectOne(req =>
      req.url === '/api/admin/notifications/mark-all-read' &&
      req.params.get('audienceRole') === 'RegionalManager' &&
      req.params.get('regionId') === '7');
    expect(markAllRequest.request.method).toBe('POST');
    markAllRequest.flush({ unreadCount: 0, readAt: '2026-05-26T21:00:00Z' });
  });
});
