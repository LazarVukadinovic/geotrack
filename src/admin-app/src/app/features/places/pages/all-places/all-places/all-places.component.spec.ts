import { ComponentFixture, TestBed } from '@angular/core/testing';

import { AllPlacesComponent } from './all-places.component';

describe('AllPlacesComponent', () => {
  let component: AllPlacesComponent;
  let fixture: ComponentFixture<AllPlacesComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AllPlacesComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(AllPlacesComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
