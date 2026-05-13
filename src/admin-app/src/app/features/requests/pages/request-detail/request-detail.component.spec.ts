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
        paramMap: convertToParamMap({ source: 'review-report', id: '5' }),
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

  it('loads and renders review-report preview detail for the active assigned region', () => {
    fixture.detectChanges();
    flushRegions();
    flushDetail('review-report', 5, 7, reviewReportDetail());
    fixture.detectChanges();

    const text = normalizedText();
    expect(component.isLoading).toBe(false);
    expect(component.request?.requestKey).toBe('review-report:5');
    expect(text).toContain('Reported review: Backend Place');
    expect(text).toContain('Pending');
    expect(text).toContain('Backend Region');
    expect(text).toContain('Reporter');
    expect(text).toContain('Report comment');
    expect(text).not.toContain('Loading request details...');
    expect(text).not.toContain('Loading...');
  });

  it('reads contribution route params and calls the contribution detail API', () => {
    setRoute({ source: 'contribution', id: '12' }, { regionId: '7' });

    component.ngOnInit();
    flushRegions();
    flushDetail('contribution', 12, 7, contributionDetail());

    expect(component.isLoading).toBe(false);
    expect(component.request?.requestKey).toBe('contribution:12');
    expect(component.request?.title).toBe('Backend Place');
    expect(component.request?.source).toBe('Contribution');
  });

  it('renders ownership proof metadata for contribution details', () => {
    setRoute({ source: 'contribution', id: '12' }, { regionId: '7' });

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

  it('reads review-report route params and calls the review-report detail API', () => {
    component.ngOnInit();
    flushRegions();
    flushDetail('review-report', 5, 7, reviewReportDetail());

    expect(component.isLoading).toBe(false);
    expect(component.request?.requestKey).toBe('review-report:5');
    expect(component.request?.source).toBe('ReviewReport');
  });

  it('restores direct navigation without a region query by trying assigned regions safely', () => {
    setRoute({ source: 'review-report', id: '5' }, {});

    component.ngOnInit();
    flushRegions([
      { id: 7, name: 'First Region', displayName: 'First Region' },
      { id: 8, name: 'Backend Region', displayName: 'Backend Region' },
    ]);

    const firstRegionRequest = expectDetailRequest('review-report', 5, 7);
    firstRegionRequest.flush({ message: 'Request not found.' }, { status: 404, statusText: 'Not Found' });
    flushDetail('review-report', 5, 8, reviewReportDetail({ regionId: 8 }));

    expect(component.isLoading).toBe(false);
    expect(component.activeRegionId).toBe(8);
    expect(component.regionDisplayName).toBe('Backend Region');
    expect(component.request?.requestKey).toBe('review-report:5');
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
    expectDetailRequest('review-report', 5, 7).flush(
      { message: 'Request not found.' },
      { status: 404, statusText: 'Not Found' },
    );
    expect(component.isLoading).toBe(false);
    expect(component.notFound).toBe(true);

    component.activeRegionId = 99;
    component.onRegionSelected(7);
    expectDetailRequest('review-report', 5, 7).flush(
      { message: 'Forbidden.' },
      { status: 403, statusText: 'Forbidden' },
    );
    expect(component.isLoading).toBe(false);
    expect(component.errorMessage).toBe('This request is not available or you do not have access.');

    component.activeRegionId = 99;
    component.onRegionSelected(7);
    expectDetailRequest('review-report', 5, 7).flush(
      { message: 'Unauthorized.' },
      { status: 401, statusText: 'Unauthorized' },
    );
    expect(component.isLoading).toBe(false);
    expect(component.authRequired).toBe(true);

    component.activeRegionId = 99;
    component.onRegionSelected(7);
    expectDetailRequest('review-report', 5, 7).flush(
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
    flushDetail('review-report', 5, 7, reviewReportDetail());

    expect(markSpy).toHaveBeenCalled();
  });

  it('approves and rejects through the regional queue API', () => {
    component.ngOnInit();
    flushRegions();
    flushDetail('review-report', 5, 7, reviewReportDetail());

    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    component.reviewerNotes = 'Reviewed.';
    component.approveRequest();
    const approve = http.expectOne('/api/admin/regional/requests/review-report/5/approve');
    expect(approve.request.body).toEqual({ regionId: 7, note: 'Reviewed.' });
    approve.flush({ requestKey: 'review-report:5', success: true, status: 'Approved' });
    expect(navigateSpy).toHaveBeenCalledWith(['/requests'], { queryParams: { regionId: 7 } });

    component.request = { ...reviewReportDetail(), status: 'Pending' };
    component.rejectReason = 'No violation';
    component.rejectRequest();
    const reject = http.expectOne('/api/admin/regional/requests/review-report/5/reject');
    expect(reject.request.body.regionId).toBe(7);
    expect(reject.request.body.reason).toBe('No violation');
    reject.flush({ requestKey: 'review-report:5', success: true, status: 'Rejected' });
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

  function reviewReportDetail(overrides: Partial<ReviewRequestDetail> = {}): ReviewRequestDetail {
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
      submittedAt: '2026-05-12T10:00:00Z',
      status: 'Pending',
      previewText: 'Report comment',
      reviewComment: 'Report comment',
      reviewRating: 2,
      reviewAuthorDisplayName: 'Review Author',
      reportReason: 'Spam',
      targetEntityName: 'Backend Place',
      images: [],
      tags: [],
      amenities: [],
      stayAmenities: [],
      bookingPartners: [],
      ...overrides,
    };
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
