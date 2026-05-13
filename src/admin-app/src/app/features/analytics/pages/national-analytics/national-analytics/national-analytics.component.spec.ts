import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { NationalAnalyticsComponent } from './national-analytics.component';

describe('NationalAnalyticsComponent', () => {
  let component: NationalAnalyticsComponent;
  let fixture: ComponentFixture<NationalAnalyticsComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [NationalAnalyticsComponent],
      providers: [provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(NationalAnalyticsComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
