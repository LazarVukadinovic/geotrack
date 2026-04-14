import { ComponentFixture, TestBed } from '@angular/core/testing';

import { MyCollectionsPageComponent } from './my-collections-page.component';

describe('MyCollectionsPage', () => {
  let component: MyCollectionsPageComponent;
  let fixture: ComponentFixture<MyCollectionsPageComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [MyCollectionsPageComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(MyCollectionsPageComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
