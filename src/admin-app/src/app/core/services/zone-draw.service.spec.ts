import { TestBed } from '@angular/core/testing';

import { ZoneDrawService } from './zone-draw.service';

describe('ZoneDrawService', () => {
  let service: ZoneDrawService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(ZoneDrawService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
