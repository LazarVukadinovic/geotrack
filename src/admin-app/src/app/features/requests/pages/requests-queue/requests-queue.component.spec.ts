import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { vi } from 'vitest';

import { ReviewQueueComponent } from './requests-queue.component';
import { RegionalRequestQueueResponse, ReviewRequest } from '../../../../core/models/review-request.model';
import { RegionalManagerSidebarStateService } from '../../../../core/services/regional-manager-sidebar-state.service';

describe('ReviewQueueComponent', () => {
  let component: ReviewQueueComponent;
  let fixture: ComponentFixture<ReviewQueueComponent>;
  let http: HttpTestingController;
  let sidebarState: RegionalManagerSidebarStateService;

  beforeEach(async () => {
    sessionStorage.clear();

    await TestBed.configureTestingModule({
      imports: [ReviewQueueComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(ReviewQueueComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    sidebarState = TestBed.inject(RegionalManagerSidebarStateService);
  });

  afterEach(() => {
    http.verify({ ignoreCancelled: true });
    sessionStorage.clear();
    vi.useRealTimers();
  });

  it('renders the content request queue copy without a Reviews tab or review rows', () => {
    fixture.detectChanges();
    flushRegions();
    flushQueue(7, queueResponse([contributionItem(), secondContributionItem()]));
    fixture.detectChanges();

    const text = normalizedText();
    expect(component.activeRegionId).toBe(7);
    expect(component.tabs.map(tab => tab.label)).toEqual([
      'All Requests',
      'Destinations',
      'Locations',
      'Places',
      'Events',
      'Approved',
      'Rejected',
    ]);
    expect(component.requests.map(item => item.title)).toEqual(['Backend Place', 'Second Backend Place']);
    expect(text).toContain('Requests Queue');
    expect(text).toContain('Manage and process incoming content submissions for the active region.');
    expect(text).not.toContain('Reviews');
    expect(text).not.toContain('reported reviews');
    expect(component.requests.map(item => item.displayRequestId)).not.toContain('REP-0005');
  });

  it('calls the backend with type and status filters from content tabs only', () => {
    component.loadAssignedRegions();
    flushRegions();
    flushQueue(7);

    component.selectTab('destinations');
    flushQueue(7, queueResponse([]), req =>
      req.params.get('type') === 'destination' && req.params.get('status') === 'pending',
    );

    component.selectTab('locations');
    flushQueue(7, queueResponse([]), req =>
      req.params.get('type') === 'location' && req.params.get('status') === 'pending',
    );

    component.selectTab('places');
    flushQueue(7, queueResponse([contributionItem()]), req =>
      req.params.get('type') === 'place' && req.params.get('status') === 'pending',
    );

    component.selectTab('events');
    flushQueue(7, queueResponse([]), req =>
      req.params.get('type') === 'event' && req.params.get('status') === 'pending',
    );

    component.selectTab('approved');
    flushQueue(7, queueResponse([], 0, 0, 1, 0), req =>
      req.params.get('type') === 'all' && req.params.get('status') === 'approved',
    );

    component.selectTab('rejected');
    flushQueue(7, queueResponse([], 0, 0, 0, 1), req =>
      req.params.get('type') === 'all' && req.params.get('status') === 'rejected',
    );
  });

  it('exposes backend content rows, counts, and footer range for the template', () => {
    fixture.detectChanges();
    flushRegions();
    flushQueue(7, {
      items: [contributionItem(), secondContributionItem()],
      totalCount: 2,
      pendingCount: 2,
      approvedCount: 1,
      rejectedCount: 1,
      countsByType: { destinations: 0, locations: 0, places: 2, events: 0 },
      skip: 0,
      take: 10,
    });
    fixture.detectChanges();

    const text = normalizedText();
    expect(text).not.toContain('Loading request queue...');
    expect(text).toContain('Backend Place');
    expect(text).toContain('Second Backend Place');
    expect(text).toContain('Showing 1 - 2 of 2 requests');
    expect(component.tabs.find(tab => tab.key === 'all')?.count).toBe(2);
    expect(component.tabs.find(tab => tab.key === 'places')?.count).toBe(2);
  });

  it('renders an empty state instead of loading for zero-count filters', () => {
    fixture.detectChanges();
    flushRegions();
    flushQueue(7, queueResponse([contributionItem()]));

    component.selectTab('destinations');
    flushQueue(7, queueResponse([], 0, 1), req => req.params.get('type') === 'destination');
    fixture.detectChanges();

    const text = normalizedText();
    expect(text).not.toContain('Loading request queue...');
    expect(text).toContain('No requests found for this filter.');
    expect(text).toContain('Showing 0 - 0 of 0 requests');
  });

  it('debounces backend search and sends the query to the content queue API', () => {
    vi.useFakeTimers();
    component.loadAssignedRegions();
    flushRegions();
    flushQueue(7);

    component.searchQuery = 'Backend';
    component.onSearchChange();
    vi.advanceTimersByTime(250);

    flushQueue(7, queueResponse([contributionItem()]), req => req.params.get('search') === 'Backend');
  });

  it('sorts and paginates through backend parameters', () => {
    component.loadAssignedRegions();
    flushRegions();
    flushQueue(7, queueResponse([contributionItem()], 11));

    component.setSort('oldest');
    flushQueue(7, queueResponse([contributionItem()], 11), req => req.params.get('sort') === 'oldest');

    component.nextPage();
    flushQueue(7, queueResponse([secondContributionItem()], 11), req =>
      req.params.get('skip') === '10' && req.params.get('take') === '10',
    );

    expect(component.showingStart).toBe(11);
    expect(component.showingEnd).toBe(11);
    expect(component.totalForCurrentView).toBe(11);
  });

  it('approves and rejects content rows and refreshes the Requests Queue badge', () => {
    component.loadAssignedRegions();
    flushRegions();
    flushQueue(7, queueResponse([contributionItem()]));

    component.approveRequest(contributionItem());

    const approve = http.expectOne('/api/admin/regional/requests/contribution/12/approve');
    expect(approve.request.method).toBe('POST');
    expect(approve.request.body.regionId).toBe(7);
    approve.flush({ requestKey: 'contribution:12', success: true, status: 'Approved' });
    flushQueue(7, queueResponse([], 0));
    flushSidebarSummary(7, 0);

    expect(component.feedbackMessage).toBe('Request approved.');
    expect(sidebarState.pendingRequestsCount()).toBe(0);

    component.queue = queueResponse([secondContributionItem()]);
    component.openRejectRequest(secondContributionItem());
    component.rejectReason = 'Duplicate';
    component.confirmReject();

    const reject = http.expectOne('/api/admin/regional/requests/contribution/13/reject');
    expect(reject.request.method).toBe('POST');
    expect(reject.request.body.regionId).toBe(7);
    expect(reject.request.body.reason).toBe('Duplicate');
    reject.flush({ requestKey: 'contribution:13', success: true, status: 'Rejected' });
    flushQueue(7, queueResponse([], 0));
    flushSidebarSummary(7, 0);

    expect(component.feedbackMessage).toBe('Request rejected.');
  });

  it('selects only pending visible content rows for bulk approve and reject', () => {
    const approved = { ...secondContributionItem(), status: 'Approved' as const };
    component.loadAssignedRegions();
    flushRegions();
    flushQueue(7, queueResponse([contributionItem(), approved]));

    component.toggleSelectAll({ target: { checked: true } } as unknown as Event);
    expect(component.selectedPendingCount).toBe(1);

    component.approveSelected();
    const approve = http.expectOne('/api/admin/regional/requests/bulk-approve');
    expect(approve.request.body.regionId).toBe(7);
    expect(approve.request.body.requestKeys).toEqual(['contribution:12']);
    approve.flush({ results: [], succeededCount: 1, failedCount: 0 });
    flushQueue(7, queueResponse([contributionItem()]));
    flushSidebarSummary(7, 1);

    component.selectedIds.add('contribution:12');
    component.openRejectSelected();
    component.rejectReason = 'Not relevant';
    component.confirmReject();
    const reject = http.expectOne('/api/admin/regional/requests/bulk-reject');
    expect(reject.request.body.requestKeys).toEqual(['contribution:12']);
    expect(reject.request.body.reason).toBe('Not relevant');
    reject.flush({ results: [], succeededCount: 1, failedCount: 0 });
    flushQueue(7, queueResponse([], 0));
    flushSidebarSummary(7, 0);
  });

  it('renders counts from the backend response without recomputing them from the current items', () => {
    component.loadAssignedRegions();
    flushRegions();
    flushQueue(7, {
      ...queueResponse([], 0, 3, 1, 2),
      countsByType: { destinations: 0, locations: 0, places: 2, events: 0 },
    });

    expect(component.tabs.map(tab => [tab.key, tab.count])).toEqual([
      ['all', 3],
      ['destinations', 0],
      ['locations', 0],
      ['places', 2],
      ['events', 0],
      ['approved', 1],
      ['rejected', 2],
    ]);
  });

  it('navigates preview to contribution details with active region context', () => {
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    component.loadAssignedRegions();
    flushRegions();
    flushQueue(7, queueResponse([contributionItem()]));

    component.previewRequest(contributionItem());

    expect(navigateSpy).toHaveBeenCalledWith(['/request/detail', 'contribution', 12], {
      queryParams: { regionId: 7 },
    });
  });

  it('shows a no-active-region state without loading queue rows', () => {
    component.loadAssignedRegions();
    const regionsRequest = http.expectOne('/api/admin/regional/regions');
    regionsRequest.flush([]);

    expect(component.activeRegionId).toBeNull();
    expect(component.assignedRegions.length).toBe(0);
    expect(component.queue).toBeNull();
    expect(component.loading).toBe(false);
    expect(component.regionLoading).toBe(false);
    expect(component.emptyMessage).toBe('No active assigned region.');
  });

  function flushRegions(): void {
    const request = http.expectOne('/api/admin/regional/regions');
    expect(request.request.method).toBe('GET');
    request.flush([{ id: 7, name: 'Backend Region', displayName: 'Backend Region' }]);
  }

  function flushQueue(
    regionId: number,
    response: RegionalRequestQueueResponse = queueResponse([]),
    extra?: (req: import('@angular/common/http').HttpRequest<unknown>) => boolean,
  ): void {
    const request = http.expectOne(
      req =>
        req.url === '/api/admin/regional/requests' &&
        req.params.get('regionId') === String(regionId) &&
        (!extra || extra(req)),
    );
    expect(request.request.method).toBe('GET');
    request.flush(response);
  }

  function flushSidebarSummary(regionId: number, pendingRequestsCount: number): void {
    const request = http.expectOne(
      req =>
        req.url === '/api/admin/regional/sidebar-summary' &&
        req.params.get('regionId') === String(regionId),
    );
    expect(request.request.method).toBe('GET');
    request.flush({
      assignedRegions: [{ id: regionId, name: 'Backend Region', displayName: 'Backend Region' }],
      activeRegion: { id: regionId, name: 'Backend Region', displayName: 'Backend Region' },
      pendingRequestsCount,
    });
  }

  function normalizedText(): string {
    return (fixture.nativeElement.textContent as string).replace(/\s+/g, ' ').trim();
  }

  function queueResponse(
    items: ReviewRequest[],
    totalCount = items.length,
    pendingCount = totalCount,
    approvedCount = 0,
    rejectedCount = 0,
  ): RegionalRequestQueueResponse {
    return {
      items,
      totalCount,
      pendingCount,
      approvedCount,
      rejectedCount,
      countsByType: {
        destinations: 0,
        locations: 0,
        places: items.filter(item => item.entityType === 'Place' && item.status === 'Pending').length,
        events: 0,
      },
      skip: component.skip,
      take: component.pageSize,
    };
  }

  function contributionItem(): ReviewRequest {
    return {
      requestKey: 'contribution:12',
      requestId: 12,
      displayRequestId: 'CON-0012',
      source: 'Contribution',
      title: 'Backend Place',
      entityType: 'Place',
      contributionType: 'CreatePlace',
      regionId: 7,
      regionDisplayName: 'Backend Region',
      submittedByUserId: 2,
      submittedByDisplayName: 'Backend Contributor',
      submittedByInitials: 'BC',
      submittedAt: '2026-05-12T10:00:00Z',
      status: 'Pending',
    };
  }

  function secondContributionItem(): ReviewRequest {
    return {
      ...contributionItem(),
      requestKey: 'contribution:13',
      requestId: 13,
      displayRequestId: 'CON-0013',
      title: 'Second Backend Place',
    };
  }
});
