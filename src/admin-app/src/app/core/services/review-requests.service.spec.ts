import { TestBed } from '@angular/core/testing';

import { ReviewRequestsService } from './review-requests.service';

describe('ReviewRequestsService', () => {
  let service: ReviewRequestsService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(ReviewRequestsService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
