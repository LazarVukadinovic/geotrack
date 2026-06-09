import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ContributionDraft } from '../../submit-contribution.component';
import { StepPreviewComponent } from './step-preview.component';

describe('StepPreviewComponent', () => {
  let component: StepPreviewComponent;
  let fixture: ComponentFixture<StepPreviewComponent>;
  const draft: ContributionDraft = {
    basics: {
      name: 'Sunny Seaside Villa',
      placeType: 'Stay',
      stayType: 'Villa',
      address: '1 Ocean Road',
      city: 'Malibu',
      region: 'California',
      country: 'USA',
      imageUrl: '',
      cardImageUrl: '',
      placeTypeId: null,
      galleryImageUrls: [],
      lat: null,
      lng: null,
      imgBgPosition: '50% 50%',
      imgBgSize: 'cover',
    },
    features: { tags: ['Beachfront'], amenities: ['wifi'] },
    details: {
      description: 'A test contribution.',
      openingHoursEnabled: false,
      openingHours: '',
      operatingHours: [],
      phone: '',
      contactEmail: '',
      website: '',
      bookingPartners: [],
    },
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [StepPreviewComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(StepPreviewComponent);
    fixture.componentRef.setInput('draft', draft);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
