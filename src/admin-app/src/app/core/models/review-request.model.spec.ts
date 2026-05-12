import { ReviewRequest } from './review-request.model';

describe('ReviewRequest model', () => {
  it('supports review request data', () => {
    const request: ReviewRequest = {
      id: '1',
      title: 'Place request',
      type: 'Place',
      region: 'Dalmatia',
      reqId: 'REQ-1',
      author: 'Contributor',
      authorInitials: 'C',
      authorBg: '#bfdbfe',
      date: 'Jan 1, 2026',
      status: 'Pending',
      imageBg: '#0ea5e9',
    };

    expect(request.status).toBe('Pending');
  });
});
