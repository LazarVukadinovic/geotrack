import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { AddDestinationComponent } from './add-destination.component';

describe('AddDestinationComponent', () => {
  let component: AddDestinationComponent;
  let fixture: ComponentFixture<AddDestinationComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AddDestinationComponent],
      providers: [provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(AddDestinationComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
