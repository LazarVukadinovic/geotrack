import { TestBed } from '@angular/core/testing';

import { RegionalAnalytics } from './regional-analytics';

describe('RegionalAnalytics', () => {
  let service: RegionalAnalytics;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(RegionalAnalytics);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
