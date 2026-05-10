import { TestBed } from '@angular/core/testing';

import { MapPlacesService } from './map-places.service';

describe('MapPlacesService', () => {
  let service: MapPlacesService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(MapPlacesService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
