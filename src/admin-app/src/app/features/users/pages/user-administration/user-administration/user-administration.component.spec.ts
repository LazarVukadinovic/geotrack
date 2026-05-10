import { ComponentFixture, TestBed } from '@angular/core/testing';

import { UserAdministrationComponent } from './user-administration.component';

describe('UserAdministrationComponent', () => {
  let component: UserAdministrationComponent;
  let fixture: ComponentFixture<UserAdministrationComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [UserAdministrationComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(UserAdministrationComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
