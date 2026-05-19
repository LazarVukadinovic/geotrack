import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';

import { NationalDashboardService } from './national-dashboard.service';

describe('NationalDashboardService', () => {
  let service: NationalDashboardService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(NationalDashboardService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
