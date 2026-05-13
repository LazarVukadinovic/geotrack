import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { NationalDashboardComponent } from './national-dashboard.component';

describe('NationalDashboardComponent', () => {
  let component: NationalDashboardComponent;
  let fixture: ComponentFixture<NationalDashboardComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [NationalDashboardComponent],
      providers: [provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(NationalDashboardComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
