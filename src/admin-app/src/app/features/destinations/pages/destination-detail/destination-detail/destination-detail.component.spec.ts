import { ComponentFixture, TestBed } from '@angular/core/testing';

import { DestinationDetailComponent } from './destination-detail.component';

describe('DestinationDetailComponent', () => {
  let component: DestinationDetailComponent;
  let fixture: ComponentFixture<DestinationDetailComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DestinationDetailComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(DestinationDetailComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
