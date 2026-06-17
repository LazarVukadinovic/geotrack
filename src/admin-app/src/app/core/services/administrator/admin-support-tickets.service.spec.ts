import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { AdminSupportTicketsService } from './admin-support-tickets.service';

describe('AdminSupportTicketsService', () => {
  let service: AdminSupportTicketsService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AdminSupportTicketsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('loads a paginated support ticket page from the admin endpoint', () => {
    let total = 0;

    service.getPage$({
      status: 'open',
      category: 'all',
      priority: 'all',
      search: ' map ',
      sort: 'newest',
      skip: 0,
      take: 25,
    }).subscribe(page => {
      total = page.totalCount;
      expect(page.items[0].status).toBe('Open');
      expect(page.items[0].reporter.displayName).toBe('Support Reporter');
    });

    const request = http.expectOne(req =>
      req.url === '/api/admin/support-tickets' &&
      req.params.get('status') === 'open' &&
      req.params.get('category') === 'all' &&
      req.params.get('priority') === 'all' &&
      req.params.get('search') === 'map' &&
      req.params.get('skip') === '0' &&
      req.params.get('take') === '25',
    );
    expect(request.request.method).toBe('GET');
    request.flush({
      items: [
        {
          ticketId: 42,
          referenceCode: 'GT-000042',
          reporter: {
            userId: 7,
            displayName: 'Support Reporter',
            email: 'reporter@example.com',
            initials: 'SR',
            isAnonymous: false,
          },
          category: 'location-and-maps',
          categoryLabel: 'Location and maps',
          subject: 'Map issue',
          messagePreview: 'The map stayed in the wrong place.',
          status: 'Open',
          priority: 'Normal',
          createdAt: '2026-05-26T20:00:00Z',
          hasAttachment: false,
          permissions: {
            canView: true,
            canResolve: true,
            canDismiss: true,
            canReopen: false,
            canAddNote: true,
          },
        },
      ],
      totalCount: 1,
      skip: 0,
      take: 25,
      nextSkip: null,
      hasMore: false,
      counts: {
        openCount: 1,
        resolvedCount: 0,
        dismissedCount: 0,
        allCount: 1,
      },
    });

    expect(total).toBe(1);
  });

  it('posts status updates without exposing ticket ids outside the route', () => {
    service.resolve$(42, 'Handled internally.').subscribe(ticket => {
      expect(ticket.status).toBe('Resolved');
      expect(ticket.internalNote).toBe('Handled internally.');
    });

    const request = http.expectOne('/api/admin/support-tickets/42/status');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      status: 'Resolved',
      internalNote: 'Handled internally.',
    });
    request.flush({
      ticketId: 42,
      referenceCode: 'GT-000042',
      reporter: {
        userId: 7,
        displayName: 'Support Reporter',
        email: 'reporter@example.com',
        initials: 'SR',
        isAnonymous: false,
      },
      category: 'location-and-maps',
      categoryLabel: 'Location and maps',
      subject: 'Map issue',
      messagePreview: 'The map stayed in the wrong place.',
      message: 'The map stayed in the wrong place.',
      status: 'Resolved',
      priority: 'Normal',
      createdAt: '2026-05-26T20:00:00Z',
      resolvedAt: '2026-05-26T21:00:00Z',
      internalNote: 'Handled internally.',
      hasAttachment: false,
      statusHistory: [],
      permissions: {
        canView: true,
        canResolve: false,
        canDismiss: true,
        canReopen: true,
        canAddNote: true,
      },
    });
  });
});
