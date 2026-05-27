import { ChangeDetectorRef } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { vi } from 'vitest';

import { RequestDetailComponent } from './request-detail.component';
import { ReviewRequestDetail } from '../../../../core/models/review-request.model';
import { RegionalAssignedRegion } from '../../../../core/models/dashboard.model';

type MutableActivatedRoute = {
  snapshot: {
    paramMap: ReturnType<typeof convertToParamMap>;
    queryParamMap: ReturnType<typeof convertToParamMap>;
  };
};

describe('RequestDetailComponent', () => {
  let component: RequestDetailComponent;
  let fixture: ComponentFixture<RequestDetailComponent>;
  let http: HttpTestingController;
  let activatedRoute: MutableActivatedRoute;

  beforeEach(async () => {
    sessionStorage.clear();
    activatedRoute = {
      snapshot: {
        paramMap: convertToParamMap({ source: 'contribution', id: '12' }),
        queryParamMap: convertToParamMap({ regionId: '7' }),
      },
    };

    await TestBed.configureTestingModule({
      imports: [RequestDetailComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: activatedRoute },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(RequestDetailComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify({ ignoreCancelled: true });
    sessionStorage.clear();
  });

  it('loads and renders contribution preview detail for the active assigned region', () => {
    fixture.detectChanges();
    flushRegions();
    flushDetail('contribution', 12, 7, contributionDetail());
    fixture.detectChanges();

    const text = normalizedText();
    expect(component.isLoading).toBe(false);
    expect(component.request?.requestKey).toBe('contribution:12');
    expect(text).toContain('Request Details');
    expect(text).toContain('Backend Place');
    expect(text).toContain('Pending');
    expect(text).toContain('Backend Region');
    expect(text).toContain('Backend Contributor');
    expect(text).toContain('Review Decision');
    expect(text).toContain('Approve Contribution');
    expect(text).toContain('Reject Contribution');
    expect(text).toContain('This note is visible only to admins/moderators.');
    expect(text).not.toContain('Report Decision');
    expect(text).not.toContain('Remove Review');
    expect(text).not.toContain('Keep Review');
    expect(text).not.toContain('Loading request details...');

    const note = fixture.nativeElement.querySelector('textarea') as HTMLTextAreaElement | null;
    expect(note?.placeholder).toBe('Optional internal moderation note');
  });

  it('renders ownership proof metadata for contribution details', () => {
    fixture.detectChanges();
    flushRegions();
    flushDetail('contribution', 12, 7, contributionDetail({
      hasOwnershipProofDocument: true,
      ownershipProofDocumentUrl: '/media/uploads/contributions/proof.pdf',
      ownershipProofOriginalFileName: 'proof.pdf',
      ownershipProofContentType: 'application/pdf',
      ownershipProofUploadedAt: '2026-05-12T10:05:00Z',
    }));
    fixture.detectChanges();

    const text = normalizedText();
    const link = fixture.nativeElement.querySelector('a[href="/media/uploads/contributions/proof.pdf"]') as HTMLAnchorElement | null;

    expect(text).toContain('Proof of Ownership');
    expect(text).toContain('proof.pdf');
    expect(link?.target).toBe('_blank');
    expect(link?.rel).toContain('noopener');
  });

  it('rejects review-report route params without calling the regional detail API', () => {
    setRoute({ source: 'review-report', id: '5' }, { regionId: '7' });

    component.ngOnInit();
    flushRegions();

    http.expectNone(req => req.url.startsWith('/api/admin/regional/requests/'));
    expect(component.isLoading).toBe(false);
    expect(component.request).toBeNull();
    expect(component.notFound).toBe(true);
  });

  it('restores direct contribution navigation without a region query by trying assigned regions safely', () => {
    setRoute({ source: 'contribution', id: '12' }, {});

    component.ngOnInit();
    flushRegions([
      { id: 7, name: 'First Region', displayName: 'First Region' },
      { id: 8, name: 'Backend Region', displayName: 'Backend Region' },
    ]);

    const firstRegionRequest = expectDetailRequest('contribution', 12, 7);
    firstRegionRequest.flush({ message: 'Request not found.' }, { status: 404, statusText: 'Not Found' });
    flushDetail('contribution', 12, 8, contributionDetail({ regionId: 8 }));

    expect(component.isLoading).toBe(false);
    expect(component.activeRegionId).toBe(8);
    expect(component.regionDisplayName).toBe('Backend Region');
    expect(component.request?.requestKey).toBe('contribution:12');
  });

  it('clears loading and shows a no-active-region state when no assigned regions exist', () => {
    component.ngOnInit();
    flushRegions([]);

    expect(component.isLoading).toBe(false);
    expect(component.regionLoading).toBe(false);
    expect(component.activeRegionId).toBeNull();
    expect(component.errorMessage).toBe('No active assigned region.');
  });

  it('clears loading after 404, 403, 401, and 500 detail errors', () => {
    component.ngOnInit();
    flushRegions();
    expectDetailRequest('contribution', 12, 7).flush(
      { message: 'Request not found.' },
      { status: 404, statusText: 'Not Found' },
    );
    expect(component.isLoading).toBe(false);
    expect(component.notFound).toBe(true);

    component.activeRegionId = 99;
    component.onRegionSelected(7);
    expectDetailRequest('contribution', 12, 7).flush(
      { message: 'Forbidden.' },
      { status: 403, statusText: 'Forbidden' },
    );
    expect(component.isLoading).toBe(false);
    expect(component.errorMessage).toBe('This request is not available or you do not have access.');

    component.activeRegionId = 99;
    component.onRegionSelected(7);
    expectDetailRequest('contribution', 12, 7).flush(
      { message: 'Unauthorized.' },
      { status: 401, statusText: 'Unauthorized' },
    );
    expect(component.isLoading).toBe(false);
    expect(component.authRequired).toBe(true);

    component.activeRegionId = 99;
    component.onRegionSelected(7);
    expectDetailRequest('contribution', 12, 7).flush(
      { message: 'Server error.' },
      { status: 500, statusText: 'Server Error' },
    );
    expect(component.isLoading).toBe(false);
    expect(component.errorMessage).toBe('Unable to load request details.');
  });

  it('marks the view for check after async state changes', () => {
    const cdr = (component as unknown as { cdr: ChangeDetectorRef }).cdr;
    const markSpy = vi.spyOn(cdr, 'markForCheck');

    component.ngOnInit();
    flushRegions();
    flushDetail('contribution', 12, 7, contributionDetail());

    expect(markSpy).toHaveBeenCalled();
  });

  it('approves or rejects contribution requests through regional queue actions', () => {
    component.ngOnInit();
    flushRegions();
    flushDetail('contribution', 12, 7, contributionDetail());

    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    component.reviewerNotes = 'Reviewed.';
    component.startApproveDecision();
    const approve = http.expectOne('/api/admin/regional/requests/contribution/12/approve');
    expect(approve.request.body).toEqual({ regionId: 7, note: 'Reviewed.' });
    approve.flush({ requestKey: 'contribution:12', success: true, status: 'Approved' });
    flushSidebarSummary(7, 0);
    expect(navigateSpy).toHaveBeenCalledWith(['/requests'], {
      queryParams: { regionId: 7 },
      state: { feedbackMessage: 'Contribution approved.' },
    });

    component.request = { ...contributionDetail(), status: 'Pending' };
    component.reviewerNotes = 'Needs edits.';
    component.startRejectDecision();
    const reject = http.expectOne('/api/admin/regional/requests/contribution/12/reject');
    expect(reject.request.body.regionId).toBe(7);
    expect(reject.request.body.reason).toBeUndefined();
    expect(reject.request.body.note).toBe('Needs edits.');
    reject.flush({ requestKey: 'contribution:12', success: true, status: 'Rejected' });
    flushSidebarSummary(7, 0);
    expect(navigateSpy).toHaveBeenCalledWith(['/requests'], {
      queryParams: { regionId: 7 },
      state: { feedbackMessage: 'Contribution rejected.' },
    });
  });

  function setRoute(params: Record<string, string>, queryParams: Record<string, string>): void {
    activatedRoute.snapshot.paramMap = convertToParamMap(params);
    activatedRoute.snapshot.queryParamMap = convertToParamMap(queryParams);
  }

  function flushRegions(regions: RegionalAssignedRegion[] = [
    { id: 7, name: 'Backend Region', displayName: 'Backend Region' },
  ]): void {
    const request = http.expectOne('/api/admin/regional/regions');
    expect(request.request.method).toBe('GET');
    request.flush(regions);
  }

  function flushDetail(
    source: string,
    id: number,
    regionId: number,
    response: ReviewRequestDetail,
  ): void {
    const request = expectDetailRequest(source, id, regionId);
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

  function expectDetailRequest(source: string, id: number, regionId: number) {
    return http.expectOne(
      req =>
        req.url === `/api/admin/regional/requests/${source}/${id}` &&
        req.params.get('regionId') === String(regionId),
    );
  }

  function normalizedText(): string {
    return (fixture.nativeElement.textContent as string).replace(/\s+/g, ' ').trim();
  }

  function contributionDetail(overrides: Partial<ReviewRequestDetail> = {}): ReviewRequestDetail {
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
      description: 'Full backend contribution body.',
      destinationName: 'Backend Destination',
      locationName: 'Backend Location',
      images: [],
      tags: [],
      amenities: [],
      stayAmenities: [],
      bookingPartners: [],
      ...overrides,
    };
  }
});
