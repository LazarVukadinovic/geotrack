import { ComponentFixture, TestBed } from '@angular/core/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { SettingsComponent } from './settings.component';

describe('Settings', () => {
  let component: SettingsComponent;
  let fixture: ComponentFixture<SettingsComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SettingsComponent, RouterTestingModule],
    }).compileComponents();

    fixture = TestBed.createComponent(SettingsComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should load user profile from localStorage', () => {
    localStorage.setItem('userProfile', JSON.stringify({
      fullName: 'Test User',
      email: 'test@example.com',
    }));
    const f2 = TestBed.createComponent(SettingsComponent);
    const c2 = f2.componentInstance;
    expect(c2.fullName).toBe('Test User');
    expect(c2.email).toBe('test@example.com');
    localStorage.removeItem('userProfile');
  });

  it('should clear localStorage on logOut', () => {
    localStorage.setItem('test', 'value');
    component.logOut();
    expect(localStorage.getItem('test')).toBeNull();
  });
});