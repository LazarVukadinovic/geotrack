import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { Router } from '@angular/router';
import { vi } from 'vitest';
import { SettingsComponent } from './settings.component';
import { AuthService } from '../../../../core/services/auth.service';
import { UserService } from '../../../../core/services/user.service';

describe('Settings', () => {
  let component: SettingsComponent;
  let fixture: ComponentFixture<SettingsComponent>;

  const router = {
    navigate: vi.fn(),
  };

  const authService = {
    logout: vi.fn(),
  };

  const userService = {
    fullName: signal('Test User'),
    email: signal('test@example.com'),
    avatarUrl: signal('/media/uploads/avatars/test-user.png'),
  };

  beforeEach(async () => {
    vi.clearAllMocks();

    await TestBed.configureTestingModule({
      imports: [SettingsComponent],
      providers: [
        { provide: Router, useValue: router },
        { provide: AuthService, useValue: authService },
        { provide: UserService, useValue: userService },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SettingsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should render the current user from UserService signals', () => {
    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Test User');
    expect(text).toContain('test@example.com');
  });

  it('should render notifications as unavailable and non-actionable', () => {
    const nativeElement = fixture.nativeElement as HTMLElement;
    const notificationLabel = Array.from(nativeElement.querySelectorAll('.settings-row__label'))
      .find((element) => element.textContent?.trim() === 'Notifications');
    const notificationRow = notificationLabel?.closest('.settings-row') as HTMLElement | null;

    expect(nativeElement.textContent).toContain('Notification preferences are not available yet.');
    expect(notificationRow).toBeTruthy();
    expect(notificationRow?.tagName.toLowerCase()).toBe('div');
    expect(notificationRow?.querySelector('.settings-row__chevron')).toBeNull();

    notificationRow?.click();

    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('should log out and return to the profile tab', () => {
    component.logOut();

    expect(authService.logout).toHaveBeenCalledTimes(1);
    expect(router.navigate).toHaveBeenCalledWith(['/profile']);
  });

  it('should route the actionable settings rows to the new subpages', () => {
    component.changePassword();
    component.openPrivacy();
    component.openAnalytics();
    component.openInterests();
    component.openHelp();
    component.contactSupport();
    component.openTerms();

    expect(router.navigate).toHaveBeenCalledWith(['/settings/change-password']);
    expect(router.navigate).toHaveBeenCalledWith(['/settings/privacy-settings']);
    expect(router.navigate).toHaveBeenCalledWith(['/settings/analytics']);
    expect(router.navigate).toHaveBeenCalledWith(['/settings/interests'], { queryParams: { from: 'settings' } });
    expect(router.navigate).toHaveBeenCalledWith(['/settings/help-center']);
    expect(router.navigate).toHaveBeenCalledWith(['/settings/contact-support']);
    expect(router.navigate).toHaveBeenCalledWith(['/settings/terms-and-privacy']);
  });
});
