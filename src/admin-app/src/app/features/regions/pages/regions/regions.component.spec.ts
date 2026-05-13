import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { AllRegionsComponent } from './regions.component';

describe('AllRegionsComponent', () => {
  let component: AllRegionsComponent;
  let fixture: ComponentFixture<AllRegionsComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AllRegionsComponent],
      providers: [provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(AllRegionsComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
