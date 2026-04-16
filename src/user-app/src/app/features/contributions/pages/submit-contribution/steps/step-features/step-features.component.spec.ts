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
});
