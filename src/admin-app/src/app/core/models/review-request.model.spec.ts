import { ReviewRequest } from './review-request.model';

describe('ReviewRequest model', () => {
  it('supports the regional content request queue shape', () => {
    const request: ReviewRequest = {
      requestKey: 'contribution:12',
      requestId: 12,
      displayRequestId: 'CON-0012',
      source: 'Contribution',
      title: 'Example place',
      entityType: 'Place',
      contributionType: 'CreatePlace',
      regionId: 7,
      regionDisplayName: 'Backend Region',
      submittedByUserId: 3,
      submittedByDisplayName: 'Contributor',
      submittedByInitials: 'C',
      submittedAt: '2026-05-12T10:00:00Z',
      status: 'Pending',
      thumbnailImageUrl: null,
      previewText: 'Contribution excerpt',
      detailRoute: '/request/detail/contribution/12',
    };

    expect(request.requestKey).toBe('contribution:12');
    expect(request.source).toBe('Contribution');
    expect(request.entityType).toBe('Place');
  });
});
