import { ComponentFixture, TestBed } from '@angular/core/testing';

import { AllLocationsComponent } from './all-locations.component';

describe('AllLocationsComponent', () => {
  let component: AllLocationsComponent;
  let fixture: ComponentFixture<AllLocationsComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AllLocationsComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(AllLocationsComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
