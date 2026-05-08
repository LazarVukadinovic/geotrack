import { ComponentFixture, TestBed } from '@angular/core/testing';

import { AllDestinationsComponent } from './all-destinations.component';

describe('AllDestinationsComponent', () => {
  let component: AllDestinationsComponent;
  let fixture: ComponentFixture<AllDestinationsComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AllDestinationsComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(AllDestinationsComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
