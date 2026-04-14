import { TestBed } from '@angular/core/testing';

import { CollectionsService } from './collections';

describe('Collections', () => {
  let service: CollectionsService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(CollectionsService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
