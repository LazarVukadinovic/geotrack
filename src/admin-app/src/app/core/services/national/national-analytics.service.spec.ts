import { TestBed } from '@angular/core/testing';

import { NationalAnalyticsService } from './national-analytics.service';

describe('NationalAnalyticsService', () => {
  let service: NationalAnalyticsService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(NationalAnalyticsService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
