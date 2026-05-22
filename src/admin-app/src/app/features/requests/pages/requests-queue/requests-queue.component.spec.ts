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

  it('loads real queue rows for the active region and does not render old mock rows', () => {
    component.loadAssignedRegions();
    flushRegions();
    flushQueue(7, {
      items: [contributionItem(), reviewReportItem()],
      totalCount: 2,
      pendingCount: 2,
      approvedCount: 1,
      rejectedCount: 0,
      countsByType: { destinations: 0, locations: 0, places: 1, events: 0, reviews: 1 },
      skip: 0,
      take: 10,
    });

    expect(component.activeRegionId).toBe(7);
    expect(component.requests.map(item => item.title)).toContain('Backend Place');
    expect(component.tabs.map(tab => tab.label)).toContain('Reviews');
    expect(component.requests.map(item => item.displayRequestId)).toContain('REP-0005');
    expect(component.requests.map(item => item.title)).not.toContain('Wine & Harvest Festival Stay');
    expect(component.requests.map(item => item.displayRequestId)).not.toContain('REQ-8998');
  });

  it('loads automatically after the assigned region is available and clears first-load loading', () => {
    component.loadAssignedRegions();
    flushRegions();

    expect(component.activeRegionId).toBe(7);
    expect(component.loading).toBe(true);

    flushQueue(7, queueResponse([contributionItem()]));

    expect(component.loading).toBe(false);
    expect(component.requests.map(item => item.title)).toEqual(['Backend Place']);
  });

  it('calls the backend with type and status filters from every tab', () => {
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

    component.selectTab('reviews');
    flushQueue(7, queueResponse([reviewReportItem()]), req =>
      req.params.get('type') === 'review' && req.params.get('status') === 'pending',
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

  it('clears stale place rows immediately when switching to an empty destinations tab', () => {
    component.loadAssignedRegions();
    flushRegions();
    flushQueue(7, queueResponse([contributionItem()], 1, 3));

    component.selectTab('destinations');

    expect(component.loading).toBe(true);
    expect(component.requests).toEqual([]);
    expect(component.tabs.find(tab => tab.key === 'places')?.count).toBe(1);

    flushQueue(7, queueResponse([], 0, 3), req => req.params.get('type') === 'destination');

    expect(component.loading).toBe(false);
    expect(component.requests).toEqual([]);
    expect(component.emptyMessage).toBe('No requests found for this filter.');
  });

  it('loads places and reviews tabs and clears loading for each', () => {
    component.loadAssignedRegions();
    flushRegions();
    flushQueue(7);

    component.selectTab('places');
    flushQueue(7, queueResponse([contributionItem(), secondContributionItem()]), req => req.params.get('type') === 'place');
    expect(component.loading).toBe(false);
    expect(component.requests.map(item => item.entityType)).toEqual(['Place', 'Place']);

    component.selectTab('reviews');
    flushQueue(7, queueResponse([reviewReportItem()]), req => req.params.get('type') === 'review');
    expect(component.loading).toBe(false);
    expect(component.requests.map(item => item.entityType)).toEqual(['Review']);
  });

  it('exposes backend rows, counts, and footer range for the template', () => {
    component.loadAssignedRegions();
    flushRegions();
    flushQueue(7, {
      items: [reviewReportItem(), contributionItem(), secondContributionItem()],
      totalCount: 3,
      pendingCount: 3,
      approvedCount: 0,
      rejectedCount: 0,
      countsByType: { destinations: 0, locations: 0, places: 2, events: 0, reviews: 1 },
      skip: 0,
      take: 10,
    });

    expect(component.loading).toBe(false);
    expect(component.requests.map(item => item.title)).toEqual([
      'Reported review: Backend Place',
      'Backend Place',
      'Second Backend Place',
    ]);
    expect(component.tabs.find(tab => tab.key === 'all')?.count).toBe(3);
    expect(component.tabs.find(tab => tab.key === 'places')?.count).toBe(2);
    expect(component.tabs.find(tab => tab.key === 'reviews')?.count).toBe(1);
    expect(component.showingStart).toBe(1);
    expect(component.showingEnd).toBe(3);
    expect(component.totalForCurrentView).toBe(3);
    expect(component.requests.map(item => item.title)).not.toContain('Wine & Harvest Festival Stay');
  });

  it('renders backend rows and footer after queue loading completes', () => {
    fixture.detectChanges();
    flushRegions();
    flushQueue(7, {
      items: [reviewReportItem(), contributionItem(), secondContributionItem()],
      totalCount: 3,
      pendingCount: 3,
      approvedCount: 0,
      rejectedCount: 0,
      countsByType: { destinations: 0, locations: 0, places: 2, events: 0, reviews: 1 },
      skip: 0,
      take: 10,
    });

    fixture.detectChanges();
    const text = normalizedText();

    expect(text).not.toContain('Loading request queue...');
    expect(text).toContain('Reported review: Backend Place');
    expect(text).toContain('Backend Place');
    expect(text).toContain('Second Backend Place');
    expect(text).toContain('Showing 1 - 3 of 3 requests');
  });

  it('exposes an empty state instead of loading for a zero-result tab', () => {
    component.loadAssignedRegions();
    flushRegions();
    flushQueue(7, queueResponse([contributionItem()]));

    component.selectTab('destinations');
    flushQueue(7, queueResponse([], 0, 1), req => req.params.get('type') === 'destination');

    expect(component.loading).toBe(false);
    expect(component.requests).toEqual([]);
    expect(component.emptyMessage).toBe('No requests found for this filter.');
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

  it('debounces backend search and sends the query to the API', () => {
    vi.useFakeTimers();
    component.loadAssignedRegions();
    flushRegions();
    flushQueue(7);

    component.searchQuery = 'Reporter';
    component.onSearchChange();
    vi.advanceTimersByTime(250);

    flushQueue(7, queueResponse([reviewReportItem()]), req => req.params.get('search') === 'Reporter');
  });

  it('sorts and paginates through backend parameters', () => {
    component.loadAssignedRegions();
    flushRegions();
    flushQueue(7, queueResponse([contributionItem()], 11));

    component.setSort('oldest');
    flushQueue(7, queueResponse([contributionItem()], 11), req => req.params.get('sort') === 'oldest');

    component.nextPage();
    flushQueue(7, queueResponse([reviewReportItem()], 11), req =>
      req.params.get('skip') === '10' && req.params.get('take') === '10',
    );

    expect(component.showingStart).toBe(11);
    expect(component.showingEnd).toBe(11);
    expect(component.totalForCurrentView).toBe(11);
  });

  it('clears loading and shows an error state when the queue API fails', () => {
    component.loadAssignedRegions();
    flushRegions();

    const request = http.expectOne(
      req => req.url === '/api/admin/regional/requests' && req.params.get('regionId') === '7',
    );
    request.flush({ message: 'Server error' }, { status: 500, statusText: 'Server Error' });

    expect(component.loading).toBe(false);
    expect(component.errorMessage).toBe('Unable to load request queue.');
    expect(component.requests).toEqual([]);
  });

  it('clears loading and shows the empty state for an empty response', () => {
    component.loadAssignedRegions();
    flushRegions();
    flushQueue(7, queueResponse([], 0, 0, 0, 0));

    expect(component.loading).toBe(false);
    expect(component.requests).toEqual([]);
    expect(component.emptyMessage).toBe('No requests found for this filter.');
  });

  it('approves a row and refreshes the queue', () => {
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
  });

  it('opens a reject reason modal and rejects a row', () => {
    component.loadAssignedRegions();
    flushRegions();
    flushQueue(7, queueResponse([contributionItem()]));

    const item = contributionItem();
    component.openRejectRequest(item);
    component.rejectReason = 'Duplicate';
    component.confirmReject();

    const reject = http.expectOne('/api/admin/regional/requests/contribution/12/reject');
    expect(reject.request.method).toBe('POST');
    expect(reject.request.body.regionId).toBe(7);
    expect(reject.request.body.reason).toBe('Duplicate');
    reject.flush({ requestKey: 'contribution:12', success: true, status: 'Rejected' });
    flushQueue(7, queueResponse([], 0));
    flushSidebarSummary(7, 0);

    expect(component.feedbackMessage).toBe('Request rejected.');
    expect(sidebarState.pendingRequestsCount()).toBe(0);
  });

  it('selects only pending visible rows for bulk approve and reject', () => {
    component.loadAssignedRegions();
    flushRegions();
    flushQueue(7, queueResponse([contributionItem(), { ...reviewReportItem(), status: 'Approved' }]));

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

  it('renders counts from the backend response without recomputing them from the current items', () => {
    component.loadAssignedRegions();
    flushRegions();
    flushQueue(7, {
      ...queueResponse([], 0, 3, 1, 2),
      countsByType: { destinations: 0, locations: 0, places: 2, events: 0, reviews: 1 },
    });

    expect(component.tabs.map(tab => [tab.key, tab.count])).toEqual([
      ['all', 3],
      ['destinations', 0],
      ['locations', 0],
      ['places', 2],
      ['events', 0],
      ['reviews', 1],
      ['approved', 1],
      ['rejected', 2],
    ]);
  });

  it('clears selected rows when the filter changes', () => {
    component.loadAssignedRegions();
    flushRegions();
    flushQueue(7, queueResponse([contributionItem()]));

    component.selectedIds.add('contribution:12');
    component.selectTab('places');

    expect(component.selectedPendingCount).toBe(0);
    expect(component.selectedIds.size).toBe(0);

    flushQueue(7, queueResponse([contributionItem()]), req => req.params.get('type') === 'place');
  });

  it('navigates preview with source and active region context', () => {
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    component.loadAssignedRegions();
    flushRegions();
    flushQueue(7, queueResponse([reviewReportItem()]));

    component.previewRequest(reviewReportItem());

    expect(navigateSpy).toHaveBeenCalledWith(['/request/detail', 'review-report', 5], {
      queryParams: { regionId: 7 },
    });
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
        reviews: items.filter(item => item.entityType === 'Review' && item.status === 'Pending').length,
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

  function reviewReportItem(): ReviewRequest {
    return {
      requestKey: 'review-report:5',
      requestId: 5,
      displayRequestId: 'REP-0005',
      source: 'ReviewReport',
      title: 'Reported review: Backend Place',
      entityType: 'Review',
      contributionType: 'PlaceReviewReport',
      regionId: 7,
      regionDisplayName: 'Backend Region',
      submittedByUserId: 3,
      submittedByDisplayName: 'Reporter',
      submittedByInitials: 'R',
      submittedAt: '2026-05-12T09:00:00Z',
      status: 'Pending',
    };
  }
});
