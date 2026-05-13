import { ReviewRequest } from './review-request.model';

describe('ReviewRequest model', () => {
  it('supports the unified regional moderation queue shape', () => {
    const request: ReviewRequest = {
      requestKey: 'review-report:12',
      requestId: 12,
      displayRequestId: 'REP-0012',
      source: 'ReviewReport',
      title: 'Reported review: Example place',
      entityType: 'Review',
      contributionType: 'PlaceReviewReport',
      regionId: 7,
      regionDisplayName: 'Backend Region',
      submittedByUserId: 3,
      submittedByDisplayName: 'Reporter',
      submittedByInitials: 'R',
      submittedAt: '2026-05-12T10:00:00Z',
      status: 'Pending',
      thumbnailImageUrl: null,
      previewText: 'Review excerpt',
      detailRoute: '/request/detail/review-report/12',
    };

    expect(request.requestKey).toBe('review-report:12');
    expect(request.source).toBe('ReviewReport');
    expect(request.entityType).toBe('Review');
  });
});
