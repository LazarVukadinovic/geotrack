import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { vi } from 'vitest';

import { DestinationDetailComponent } from './destination-detail.component';

describe('DestinationDetailComponent', () => {
  let component: DestinationDetailComponent;
  let fixture: ComponentFixture<DestinationDetailComponent>;
  let router: Router;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DestinationDetailComponent],
      providers: [provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(DestinationDetailComponent);
    component = fixture.componentInstance;
    router = TestBed.inject(Router);
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('navigates to add event with destination parent prefill', () => {
    const navigateSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    component.item = {
      id: '9',
      name: 'Old Town',
      summary: '',
      region: 'Region',
      status: 'Published',
      lastUpdated: 'Today',
      sortTimestamp: 0,
      imageBg: '',
      category: 'Destination',
      regionId: 7,
      regionDisplayName: 'Region',
      destinationTypeId: 1,
    };

    component.addEvent();

    expect(navigateSpy).toHaveBeenCalledWith(['/events/new'], {
      state: {
        regionId: 7,
        eventPrefill: {
          venueType: 'destination',
          destinationId: 9,
          locationId: null,
          placeId: null,
        },
        returnTo: '/destinations/detail/9',
      },
    });
  });
});
