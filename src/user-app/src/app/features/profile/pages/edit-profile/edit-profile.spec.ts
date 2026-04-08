import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { EditProfile } from './edit-profile';

describe('EditProfile', () => {
  let component: EditProfile;
  let fixture: ComponentFixture<EditProfile>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [EditProfile],
      providers: [provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(EditProfile);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should load saved profile from localStorage', () => {
    localStorage.setItem('userProfile', JSON.stringify({
      fullName: 'Test User',
      email: 'test@example.com',
      phone: '+381601234567',
      location: 'Belgrade, Serbia',
      bio: 'Hello world',
    }));

    const f2 = TestBed.createComponent(EditProfile);
    const c2 = f2.componentInstance;

    expect(c2.fullName).toBe('Test User');
    expect(c2.email).toBe('test@example.com');
    expect(c2.location).toBe('Belgrade, Serbia');

    localStorage.removeItem('userProfile');
  });

  it('should set isSaving to true when saveChanges is called', () => {
    component.saveChanges();
    expect(component.isSaving).toBe(true);
  });

  it('should not call saveChanges again while already saving', () => {
    component.isSaving = true;
    const spy = spyOn<Storage>(window.localStorage, 'setItem');
    component.saveChanges();
    expect(spy).not.toHaveBeenCalled();
  });
});