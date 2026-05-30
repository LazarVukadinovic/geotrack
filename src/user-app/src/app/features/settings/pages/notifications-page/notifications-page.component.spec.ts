import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { NotificationsPageComponent } from './notifications-page.component';
import { UserSettingsService } from '../../services/user-settings.service';

describe('NotificationsPageComponent', () => {
  let fixture: ComponentFixture<NotificationsPageComponent>;
  let settingsService: {
    getNotificationPreferences: ReturnType<typeof vi.fn>;
    updateNotificationPreferences: ReturnType<typeof vi.fn>;
  };

  beforeEach(async () => {
    settingsService = {
      getNotificationPreferences: vi.fn(() => of({
        generalNotifications: true,
        travelRecommendations: true,
        nearbyEvents: true,
        collectionUpdates: true,
        emailNotifications: true,
      })),
      updateNotificationPreferences: vi.fn(),
    };

    await TestBed.configureTestingModule({
      imports: [NotificationsPageComponent],
      providers: [
        { provide: UserSettingsService, useValue: settingsService },
        { provide: Router, useValue: { navigateByUrl: vi.fn() } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(NotificationsPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();
  });

  it('shows in-app contribution approval notifications as enabled', () => {
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';

    expect(text).not.toContain('Notification preferences are not available yet.');
    expect(text).toContain('In-app notifications');
    expect(text).toContain('Contribution approved');
    expect(text).toContain('Get notified in GeoTrack when your submitted contribution is approved.');
  });

  it('shows email notifications as disabled and unavailable', () => {
    const nativeElement = fixture.nativeElement as HTMLElement;
    const emailButton = Array.from(nativeElement.querySelectorAll('button'))
      .find(button => button.textContent?.includes('Email updates')) as HTMLButtonElement | undefined;

    expect(nativeElement.textContent).toContain('Email notifications are not available yet.');
    expect(nativeElement.textContent).toContain('Coming soon');
    expect(emailButton).toBeTruthy();
    expect(emailButton?.disabled).toBe(true);
    expect(settingsService.updateNotificationPreferences).not.toHaveBeenCalled();
  });
});
