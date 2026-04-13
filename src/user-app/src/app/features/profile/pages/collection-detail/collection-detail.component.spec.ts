import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CollectionDetailComponent } from './collection-detail.component';

describe('CollectionDetail', () => {
  let component: CollectionDetailComponent;
  let fixture: ComponentFixture<CollectionDetailComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CollectionDetailComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(CollectionDetailComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
