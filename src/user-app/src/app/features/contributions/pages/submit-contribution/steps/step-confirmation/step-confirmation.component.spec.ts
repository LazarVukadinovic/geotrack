import { ComponentFixture, TestBed } from '@angular/core/testing';

import { StepConfirmationComponent } from './step-confirmation.component';

describe('StepConfirmationComponent', () => {
  let component: StepConfirmationComponent;
  let fixture: ComponentFixture<StepConfirmationComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [StepConfirmationComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(StepConfirmationComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
