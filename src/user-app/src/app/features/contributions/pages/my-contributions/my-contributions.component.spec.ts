import { ComponentFixture, TestBed } from '@angular/core/testing';

import { MyContributionsComponent } from './my-contributions.component';

describe('MyContributionsComponent', () => {
  let component: MyContributionsComponent;
  let fixture: ComponentFixture<MyContributionsComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [MyContributionsComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(MyContributionsComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
