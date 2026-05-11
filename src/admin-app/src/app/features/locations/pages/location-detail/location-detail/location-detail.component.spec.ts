import { ComponentFixture, TestBed } from '@angular/core/testing';

import { LocationDetailComponent } from './location-detail.component';

describe('LocationDetailComponent', () => {
  let component: LocationDetailComponent;
  let fixture: ComponentFixture<LocationDetailComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [LocationDetailComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(LocationDetailComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
