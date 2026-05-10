import { ComponentFixture, TestBed } from '@angular/core/testing';

import { AddDestinationComponent } from './add-destination.component';

describe('AddDestinationComponent', () => {
  let component: AddDestinationComponent;
  let fixture: ComponentFixture<AddDestinationComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AddDestinationComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(AddDestinationComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
