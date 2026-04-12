import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CollectionDetail } from './collection-detail';

describe('CollectionDetail', () => {
  let component: CollectionDetail;
  let fixture: ComponentFixture<CollectionDetail>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CollectionDetail],
    }).compileComponents();

    fixture = TestBed.createComponent(CollectionDetail);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
