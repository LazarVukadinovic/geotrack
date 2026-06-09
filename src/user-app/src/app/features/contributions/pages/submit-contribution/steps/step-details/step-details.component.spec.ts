import { ComponentFixture, TestBed } from '@angular/core/testing';

import { StepDetailsComponent } from './step-details.component';

describe('StepDetailsComponent', () => {
  let component: StepDetailsComponent;
  let fixture: ComponentFixture<StepDetailsComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [StepDetailsComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(StepDetailsComponent);
    fixture.componentRef.setInput('isAccommodation', true);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('rejects malformed website URLs', () => {
    fixture.componentRef.setInput('isAccommodation', false);
    component.form.website = 'javascript:alert(1)';

    component.onContinue();

    expect(component.isValid()).toBe(false);
    expect(component.websiteError()).toBe('Website URL must be a valid https URL.');
    expect(component.submitted).toBe(true);
  });

  it('requires selected booking partners to have valid https URLs', () => {
    component.togglePartner('booking');

    component.onContinue();

    expect(component.isValid()).toBe(false);
    expect(component.partnerUrlError('booking')).toBe('Booking partner URL is required.');

    component.updatePartnerUrl('booking', 'https://booking.example/stays/validation-villa');

    expect(component.isValid()).toBe(true);
  });
});
