import { TestBed } from '@angular/core/testing';

import { PlacesListService } from './places-list.service';

describe('PlacesListService', () => {
  let service: PlacesListService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(PlacesListService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
