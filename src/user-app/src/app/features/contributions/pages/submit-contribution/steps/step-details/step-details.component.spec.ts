import { ComponentFixture, TestBed } from '@angular/core/testing';

import { DetailsData, StepDetailsComponent } from './step-details.component';

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

  it('emits a seven-day midnight schedule when open 24 hours is enabled', () => {
    const savedValues: DetailsData[] = [];
    component.saved.subscribe(value => savedValues.push(value));
    component.form.openingHoursEnabled = true;
    component.form.open24Hours = true;

    component.onContinue();

    expect(savedValues[0].openingHours).toBe('Open 24 hours');
    expect(savedValues[0].operatingHours).toHaveLength(7);
    expect(savedValues[0].operatingHours.every(hour =>
      hour.isOpen && hour.openTime === '00:00' && hour.closeTime === '00:00')).toBe(true);
  });

  it('allows disabling open 24 hours and restores editable day hours', () => {
    const changedValues: DetailsData[] = [];
    component.changed.subscribe(value => changedValues.push(value));
    component.form.openingHoursEnabled = true;
    component.toggleOpen24Hours();
    expect(component.form.open24Hours).toBe(true);

    component.toggleOpen24Hours();

    expect(component.form.open24Hours).toBe(false);
    expect(changedValues.at(-1)?.open24Hours).toBe(false);
    expect(changedValues.at(-1)?.operatingHours.some(hour =>
      hour.openTime === '09:00' && hour.closeTime === '17:00')).toBe(true);
  });

  it('requires opening hours for bars and keeps the section enabled', () => {
    fixture.componentRef.setInput('isAccommodation', false);
    fixture.componentRef.setInput('placeType', 'Bar');
    fixture.detectChanges();

    expect(component.openingHoursRequired()).toBe(true);
    expect(component.form.openingHoursEnabled).toBe(true);

    component.form.openingHoursEnabled = false;
    component.onContinue();

    expect(component.isValid()).toBe(false);
    expect(component.openingHoursError).toBe('Opening hours are required for this place type.');
  });

  it('uses native time inputs so letters cannot be entered as opening hours', () => {
    fixture.componentRef.setInput('isAccommodation', false);
    fixture.componentRef.setInput('placeType', 'Bar');
    fixture.detectChanges();

    const timeInput = fixture.nativeElement.querySelector('input[name="openingHours-Monday-open"]') as HTMLInputElement;

    expect(timeInput).toBeTruthy();
    expect(timeInput.type).toBe('time');
    timeInput.value = 'a';
    expect(timeInput.value).toBe('');
  });
});
