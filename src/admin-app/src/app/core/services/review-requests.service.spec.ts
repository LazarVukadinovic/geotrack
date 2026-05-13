import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ReviewRequestsService } from './review-requests.service';
import { ReviewRequest } from '../models/review-request.model';

describe('ReviewRequestsService', () => {
  let service: ReviewRequestsService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(ReviewRequestsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('loads the queue from the regional moderation API with backend filters', () => {
    service
      .getQueue({
        regionId: 10,
        type: 'review',
        status: 'pending',
        search: 'reported review',
        sort: 'oldest',
        skip: 20,
        take: 10,
      })
      .subscribe(response => {
        expect(response.totalCount).toBe(1);
        expect(response.items[0].requestKey).toBe('review-report:5');
      });

    const request = http.expectOne(req => req.url === '/api/admin/regional/requests');
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('regionId')).toBe('10');
    expect(request.request.params.get('type')).toBe('review');
    expect(request.request.params.get('status')).toBe('pending');
    expect(request.request.params.get('search')).toBe('reported review');
    expect(request.request.params.get('sort')).toBe('oldest');
    expect(request.request.params.get('skip')).toBe('20');
    expect(request.request.params.get('take')).toBe('10');
    request.flush({
      items: [reviewReportItem()],
      totalCount: 1,
      pendingCount: 1,
      approvedCount: 0,
      rejectedCount: 0,
      countsByType: { destinations: 0, locations: 0, places: 0, events: 0, reviews: 1 },
      skip: 20,
      take: 10,
    });
  });

  it('loads contribution and review-report detail routes without mock lookups', () => {
    service.getDetail(10, 'ReviewReport', 5).subscribe(detail => {
      expect(detail.source).toBe('ReviewReport');
      expect(detail.images).toEqual([]);
    });

    const reportRequest = http.expectOne(
      req =>
        req.url === '/api/admin/regional/requests/review-report/5' &&
        req.params.get('regionId') === '10',
    );
    expect(reportRequest.request.method).toBe('GET');
    reportRequest.flush({ ...reviewReportItem(), description: 'Full detail', images: null });

    service.getDetail(10, 'Contribution', 12).subscribe(detail => {
      expect(detail.source).toBe('Contribution');
      expect(detail.requestKey).toBe('contribution:12');
      expect(detail.hasOwnershipProofDocument).toBe(true);
      expect(detail.ownershipProofDocumentUrl).toBe('/media/uploads/contributions/proof.pdf');
      expect(detail.ownershipProofOriginalFileName).toBe('proof.pdf');
    });

    const contributionRequest = http.expectOne(
      req =>
        req.url === '/api/admin/regional/requests/contribution/12' &&
        req.params.get('regionId') === '10',
    );
    expect(contributionRequest.request.method).toBe('GET');
    contributionRequest.flush({
      ...contributionItem(),
      description: 'Contribution detail',
      ownershipProofDocumentUrl: '/media/uploads/contributions/proof.pdf',
      ownershipProofOriginalFileName: 'proof.pdf',
      ownershipProofContentType: 'application/pdf',
      ownershipProofUploadedAt: '2026-05-12T10:05:00Z',
    });
  });

  it('rejects invalid detail sources instead of silently calling a contribution route', () => {
    expect(() => service.getDetail(10, 'invalid-source', 1)).toThrow('Invalid request source.');
  });

  it('posts row approve and reject actions with region scope', () => {
    const item = contributionItem();

    service.approve(10, item, 'Ready').subscribe(result => {
      expect(result.status).toBe('Approved');
    });

    const approve = http.expectOne('/api/admin/regional/requests/contribution/12/approve');
    expect(approve.request.method).toBe('POST');
    expect(approve.request.body).toEqual({ regionId: 10, note: 'Ready' });
    approve.flush({ requestKey: item.requestKey, success: true, status: 'Approved' });

    service.reject(10, item, 'Missing details', 'Needs address').subscribe(result => {
      expect(result.status).toBe('Rejected');
    });

    const reject = http.expectOne('/api/admin/regional/requests/contribution/12/reject');
    expect(reject.request.method).toBe('POST');
    expect(reject.request.body).toEqual({
      regionId: 10,
      reason: 'Missing details',
      note: 'Needs address',
    });
    reject.flush({ requestKey: item.requestKey, success: true, status: 'Rejected' });
  });

  it('posts bulk approve and reject request keys', () => {
    service.bulkApprove(10, ['contribution:1', 'review-report:2']).subscribe(result => {
      expect(result.succeededCount).toBe(2);
    });

    const approve = http.expectOne('/api/admin/regional/requests/bulk-approve');
    expect(approve.request.body).toEqual({
      regionId: 10,
      requestKeys: ['contribution:1', 'review-report:2'],
      note: undefined,
    });
    approve.flush({ results: [], succeededCount: 2, failedCount: 0 });

    service.bulkReject(10, ['contribution:3'], 'Duplicate').subscribe(result => {
      expect(result.failedCount).toBe(0);
    });

    const reject = http.expectOne('/api/admin/regional/requests/bulk-reject');
    expect(reject.request.body).toEqual({
      regionId: 10,
      requestKeys: ['contribution:3'],
      reason: 'Duplicate',
      note: undefined,
    });
    reject.flush({ results: [], succeededCount: 1, failedCount: 0 });
  });

  it('parses stable composite request keys', () => {
    expect(service.parseRequestKey('contribution:12')).toEqual({
      source: 'Contribution',
      requestId: 12,
    });
    expect(service.parseRequestKey('review-report:5')).toEqual({
      source: 'ReviewReport',
      requestId: 5,
    });
    expect(service.parseRequestKey('unknown:5')).toBeNull();
  });

  function contributionItem(): ReviewRequest {
    return {
      requestKey: 'contribution:12',
      requestId: 12,
      displayRequestId: 'CON-0012',
      source: 'Contribution',
      title: 'Backend Place',
      entityType: 'Place',
      contributionType: 'CreatePlace',
      regionId: 10,
      regionDisplayName: 'Backend Region',
      submittedByUserId: 2,
      submittedByDisplayName: 'Contributor',
      submittedAt: '2026-05-12T10:00:00Z',
      status: 'Pending',
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
      regionId: 10,
      regionDisplayName: 'Backend Region',
      submittedByUserId: 3,
      submittedByDisplayName: 'Reporter',
      submittedAt: '2026-05-12T10:00:00Z',
      status: 'Pending',
    };
  }
});
