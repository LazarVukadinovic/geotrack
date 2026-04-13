import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { EditProfileComponent } from './edit-profile.component';

describe('EditProfile', () => {
  let component: EditProfileComponent;
  let fixture: ComponentFixture<EditProfileComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [EditProfileComponent],
      providers: [provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(EditProfileComponent);
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

    const f2 = TestBed.createComponent(EditProfileComponent);
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