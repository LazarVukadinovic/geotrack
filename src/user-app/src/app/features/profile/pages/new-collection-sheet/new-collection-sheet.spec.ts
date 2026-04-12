import { ComponentFixture, TestBed } from '@angular/core/testing';

import { NewCollectionSheet } from './new-collection-sheet';

describe('NewCollectionSheet', () => {
  let component: NewCollectionSheet;
  let fixture: ComponentFixture<NewCollectionSheet>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [NewCollectionSheet],
    }).compileComponents();

    fixture = TestBed.createComponent(NewCollectionSheet);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
