import { ComponentFixture, TestBed } from '@angular/core/testing';

import { StepPreviewComponent } from './step-preview.component';

describe('StepPreviewComponent', () => {
  let component: StepPreviewComponent;
  let fixture: ComponentFixture<StepPreviewComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [StepPreviewComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(StepPreviewComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
