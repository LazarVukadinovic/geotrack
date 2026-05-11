import { TestBed } from '@angular/core/testing';

import { NationalDashboardService } from './national-dashboard.service';

describe('NationalDashboardService', () => {
  let service: NationalDashboardService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(NationalDashboardService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
