import { ComponentFixture, TestBed } from '@angular/core/testing';

import { MyContributionsPageComponent } from './my-contributions.component';

describe('MyContributionsPageComponent', () => {
  let component: MyContributionsPageComponent;
  let fixture: ComponentFixture<MyContributionsPageComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [MyContributionsPageComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(MyContributionsPageComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
