import { TestBed } from '@angular/core/testing';

import { DestinationsListService } from './destinations-list.service';

describe('DestinationsListService', () => {
  let service: DestinationsListService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(DestinationsListService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
