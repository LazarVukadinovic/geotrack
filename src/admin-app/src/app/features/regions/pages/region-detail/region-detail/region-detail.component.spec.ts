import { ComponentFixture, TestBed } from '@angular/core/testing';

import { RegionDetailComponent } from './region-detail.component';

describe('RegionDetailComponent', () => {
  let component: RegionDetailComponent;
  let fixture: ComponentFixture<RegionDetailComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RegionDetailComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(RegionDetailComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
