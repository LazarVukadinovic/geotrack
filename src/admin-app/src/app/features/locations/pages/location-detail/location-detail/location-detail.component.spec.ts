import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { vi } from 'vitest';

import { LocationDetailComponent } from './location-detail.component';

describe('LocationDetailComponent', () => {
  let component: LocationDetailComponent;
  let fixture: ComponentFixture<LocationDetailComponent>;
  let router: Router;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [LocationDetailComponent],
      providers: [provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(LocationDetailComponent);
    component = fixture.componentInstance;
    router = TestBed.inject(Router);
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('navigates to add event with location parent prefill', () => {
    const navigateSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    component.item = {
      id: '15',
      name: 'Central Square',
      summary: '',
      region: 'Region',
      status: 'Published',
      lastUpdated: 'Today',
      sortTimestamp: 0,
      imageBg: '',
      category: 'Landmark',
      regionId: 7,
      regionDisplayName: 'Region',
      destinationId: 4,
    };

    component.addEvent();

    expect(navigateSpy).toHaveBeenCalledWith(['/events/new'], {
      state: {
        regionId: 7,
        eventPrefill: {
          venueType: 'location',
          destinationId: 4,
          locationId: 15,
          placeId: null,
        },
        returnTo: '/locations/detail/15',
      },
    });
  });
});
