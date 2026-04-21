import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ContributionDetailComponent } from './contribution-detail.component';

describe('ContributionDetailComponent', () => {
  let component: ContributionDetailComponent;
  let fixture: ComponentFixture<ContributionDetailComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ContributionDetailComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(ContributionDetailComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
