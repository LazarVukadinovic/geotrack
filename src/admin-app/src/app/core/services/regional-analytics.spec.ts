import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { DestinationsListService } from './destinations-list.service';
import { EventsListService } from './events-list.service';
import { LocationsListService } from './locations-list.service';
import { PlacesListService } from './places-list.service';
import { RegionalAnalyticsService } from './regional-analytics';

describe('RegionalAnalyticsService', () => {
  let service: RegionalAnalyticsService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        RegionalAnalyticsService,
        { provide: LocationsListService, useValue: { getAllItems: () => [] } },
        { provide: DestinationsListService, useValue: { getAllItems: () => [] } },
        { provide: EventsListService, useValue: { getAllItems: () => [] } },
        { provide: PlacesListService, useValue: { getAllItems: () => [] } },
      ],
    });

    service = TestBed.inject(RegionalAnalyticsService);
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('builds an analytics payload from the list services', async () => {
    const data = await firstValueFrom(service.getData$());

    expect(data.entityRows).toEqual([]);
    expect(data.metrics.all).toHaveLength(6);
    expect(data.distribution.all).toHaveLength(4);
  });
});
