import { ComponentFixture, TestBed } from '@angular/core/testing';

import { NewCollectionSheetComponent } from './new-collection-sheet.component';

describe('NewCollectionSheet', () => {
  let component: NewCollectionSheetComponent;
  let fixture: ComponentFixture<NewCollectionSheetComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [NewCollectionSheetComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(NewCollectionSheetComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
