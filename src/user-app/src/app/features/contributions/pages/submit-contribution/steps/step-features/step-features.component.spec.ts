import { ComponentFixture, TestBed } from '@angular/core/testing';

import { StepFeaturesComponent } from './step-features.component';

describe('StepFeaturesComponent', () => {
  let component: StepFeaturesComponent;
  let fixture: ComponentFixture<StepFeaturesComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [StepFeaturesComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(StepFeaturesComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('marks backend amenity variants as selected frontend options', () => {
    fixture.componentRef.setInput('data', {
      tags: ['Beachfront'],
      amenities: ['free-wifi', 'breakfast-included', 'On-site Dining', 'air-conditioning'],
    });
    fixture.detectChanges();

    expect(component.isAmenitySelected('wifi')).toBe(true);
    expect(component.isAmenitySelected('breakfast')).toBe(true);
    expect(component.isAmenitySelected('dining')).toBe(true);
    expect(component.isAmenitySelected('ac')).toBe(true);

    const amenityButtons = Array.from(
      fixture.nativeElement.querySelectorAll('button'),
    ) as HTMLButtonElement[];
    const wifiButton = amenityButtons.find(button => button.textContent?.includes('WIFI'));
    expect(wifiButton?.className).toContain('border-[#e8722a]');
    expect(wifiButton?.className).toContain('bg-orange-50');
  });

  it('preserves normalized amenities while toggling selections', () => {
    const saved: unknown[] = [];
    component.saved.subscribe(value => saved.push(value));

    fixture.componentRef.setInput('data', {
      tags: ['Beachfront'],
      amenities: ['free-wifi', 'sea-view'],
    });
    fixture.detectChanges();

    component.toggleAmenity('pool');
    component.toggleAmenity('wifi');
    component.onContinue();

    expect(saved).toEqual([
      { tags: ['Beachfront'], amenities: ['sea-view', 'pool'] },
    ]);
  });
});
