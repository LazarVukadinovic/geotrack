import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { AuthService } from './auth.service';
import { AppNotificationsService } from './app-notifications.service';

describe('AppNotificationsService', () => {
  let service: AppNotificationsService;
  let httpMock: HttpTestingController;
  let loggedIn: boolean;

  beforeEach(() => {
    loggedIn = false;

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { isLoggedIn: () => loggedIn } },
      ],
    });

    service = TestBed.inject(AppNotificationsService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    TestBed.resetTestingModule();
  });

  it('loads notifications from the backend list endpoint', () => {
    loggedIn = true;

    service.loadInitial(true);

    const req = httpMock.expectOne(request =>
      request.url === '/api/notifications' &&
      request.params.get('skip') === '0' &&
      request.params.get('take') === '20',
    );
    expect(req.request.method).toBe('GET');

    req.flush({
      items: [
        {
          id: 7,
          type: 'contribution_approved',
          title: 'Contribution approved',
          message: 'Your contribution has been approved and published.',
          targetType: 'contribution',
          targetId: 42,
          targetRoute: '/places/42',
          createdAt: '2026-05-30T10:00:00Z',
          readAt: null,
          isRead: false,
        },
      ],
      unreadCount: 1,
      totalCount: 1,
      skip: 0,
      take: 20,
      nextSkip: 1,
      hasMore: false,
    });

    expect(service.items()).toHaveLength(1);
    expect(service.items()[0].actionUrl).toBe('/places/42');
    expect(service.items()[0].relatedEntityType).toBe('contribution');
    expect(service.unreadCount()).toBe(1);
    expect(service.hasMore()).toBe(false);
  });

  it('renders an empty loaded state without mock notifications', () => {
    loggedIn = true;

    service.loadInitial(true);

    const req = httpMock.expectOne('/api/notifications?skip=0&take=20');
    req.flush({
      items: [],
      unreadCount: 0,
      totalCount: 0,
      skip: 0,
      take: 20,
      nextSkip: 0,
      hasMore: false,
    });

    expect(service.items()).toEqual([]);
    expect(service.unreadCount()).toBe(0);
  });

  it('marks a notification as read and updates the unread count', () => {
    loggedIn = true;

    service.loadInitial(true);
    httpMock.expectOne('/api/notifications?skip=0&take=20').flush({
      notifications: [
        {
          id: 12,
          type: 'contribution_approved',
          title: 'Contribution approved',
          message: 'Your contribution has been approved and published.',
          relatedEntityType: 'contribution',
          relatedEntityId: 12,
          actionUrl: '/places/12',
          createdAt: '2026-05-30T10:00:00Z',
          readAt: null,
          isRead: false,
        },
      ],
      unreadCount: 1,
      skip: 0,
      take: 20,
      nextSkip: 1,
      hasMore: false,
    });

    service.markRead(12);

    const readReq = httpMock.expectOne('/api/notifications/12/read');
    expect(readReq.request.method).toBe('PUT');
    readReq.flush(null);

    expect(service.items()[0].isRead).toBe(true);
    expect(service.unreadCount()).toBe(0);
  });
});
