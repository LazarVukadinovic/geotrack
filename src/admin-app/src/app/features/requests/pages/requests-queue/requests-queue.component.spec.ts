import { ComponentFixture, TestBed } from '@angular/core/testing';

import { RequestsQueueComponent } from './requests-queue.component';

describe('RequestsQueueComponent', () => {
  let component: RequestsQueueComponent;
  let fixture: ComponentFixture<RequestsQueueComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RequestsQueueComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(RequestsQueueComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
