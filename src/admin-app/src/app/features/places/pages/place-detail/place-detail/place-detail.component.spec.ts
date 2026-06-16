import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { vi } from 'vitest';

import { PlaceDetailComponent } from './place-detail.component';

describe('PlaceDetailComponent', () => {
  let component: PlaceDetailComponent;
  let fixture: ComponentFixture<PlaceDetailComponent>;
  let router: Router;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PlaceDetailComponent],
      providers: [provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(PlaceDetailComponent);
    component = fixture.componentInstance;
    router = TestBed.inject(Router);
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('navigates to add event with place parent prefill', () => {
    const navigateSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    component.item = {
      id: '22',
      name: 'City Museum',
      summary: '',
      region: 'Region',
      status: 'Published',
      lastUpdated: 'Today',
      sortTimestamp: 0,
      imageBg: '',
      category: 'Museum',
      regionId: 7,
      regionDisplayName: 'Region',
      destinationId: 4,
      locationId: 15,
    };

    component.addEvent();

    expect(navigateSpy).toHaveBeenCalledWith(['/events/new'], {
      state: {
        regionId: 7,
        eventPrefill: {
          venueType: 'place',
          destinationId: 4,
          locationId: 15,
          placeId: 22,
        },
        returnTo: '/places/detail/22',
      },
    });
  });
});
