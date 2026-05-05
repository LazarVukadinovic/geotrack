import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';

import { StepBasicsComponent } from './step-basics.component';

describe('StepBasicsComponent', () => {
  let component: StepBasicsComponent;
  let fixture: ComponentFixture<StepBasicsComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [StepBasicsComponent],
      providers: [provideHttpClient()],
    })
    .compileComponents();

    fixture = TestBed.createComponent(StepBasicsComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('requires a confirmed map location before continuing', () => {
    component.form.name = 'Validation Villa';
    component.form.city = 'Dubrovnik';
    component.form.country = 'Croatia';

    component.onContinue();

    expect(component.isValid()).toBe(false);
    expect(component.showLocationError()).toBe(true);
  });

  it('accepts valid confirmed coordinates', () => {
    component.form.name = 'Validation Villa';
    component.form.city = 'Dubrovnik';
    component.form.country = 'Croatia';
    component.form.lat = 42.6507;
    component.form.lng = 18.0944;

    expect(component.isValid()).toBe(true);
    expect(component.hasConfirmedLocation()).toBe(true);
  });
});
