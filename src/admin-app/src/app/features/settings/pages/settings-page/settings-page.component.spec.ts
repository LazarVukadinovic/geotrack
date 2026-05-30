import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { SettingsComponent } from './settings-page.component';
import { AdminProfile } from '../../../../core/models/admin-profile.model';

describe('SettingsComponent', () => {
  let component: SettingsComponent;
  let fixture: ComponentFixture<SettingsComponent>;
  let http: HttpTestingController;

  beforeEach(async () => {
    sessionStorage.clear();

    await TestBed.configureTestingModule({
      imports: [SettingsComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(SettingsComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => {
    http.verify();
    sessionStorage.clear();
  });

  it('loads profile fields and renders read-only admin assignment data', () => {
    flushProfile();
    fixture.detectChanges();

    expect(component.firstName()).toBe('Elena');
    expect(component.lastName()).toBe('Rossi');
    expect(component.email()).toBe('elena@example.com');
    expect(component.bio()).toBe('Regional manager bio');
    expect(component.avatarImageUrl()).toBe('/media/uploads/avatars/current.png?v=101');
    expect(component.currentRole).toBe('Regional Manager');
    expect(component.assignmentSectionTitle).toBe('Regional Manager Assignment');
    expect(component.assignmentLabel).toBe('Assigned Region');
    expect(component.assignedRegion).toBe('Coastal Test Region, Testland, Profile Test Region, Testland');
    expect(component.memberSince).not.toBe('-');

    const emailInput = fixture.nativeElement.querySelector('input[type="email"]') as HTMLInputElement;
    expect(emailInput.disabled).toBe(true);

    const assignmentSection = fixture.nativeElement.querySelector('[data-testid="assignment-section"]') as HTMLElement;
    expect(assignmentSection.textContent).toContain('Regional Manager Assignment');
    expect(assignmentSection.textContent).toContain('Assigned Region');
    expectEmailNotificationsUnavailable();
  });

  it('renders territory manager assignment labels and values from the backend DTO', () => {
    flushProfile(territoryProfileResponse());
    fixture.detectChanges();

    expect(component.currentRole).toBe('Territory Manager');
    expect(component.assignmentSectionTitle).toBe('Territory Manager Assignment');
    expect(component.assignmentLabel).toBe('Assigned Territory');
    expect(component.assignmentDisplayValue).toBe('Croatia');
    expect(component.memberSince).not.toBe('-');

    const assignmentSection = fixture.nativeElement.querySelector('[data-testid="assignment-section"]') as HTMLElement;
    const assignmentText = assignmentSection.textContent ?? '';
    expect(assignmentText).toContain('Territory Manager Assignment');
    expect(assignmentText).toContain('Assigned Territory');
    expect(assignmentText).toContain('Croatia');
    expect(assignmentText).not.toContain('Regional Manager Assignment');
    expect(assignmentText).not.toContain('Assigned Region');
    expect(assignmentText).not.toContain('No assigned regions');
    expectEmailNotificationsUnavailable();
  });

  it('renders administrator profile settings with system access instead of region assignment labels', () => {
    flushProfile(administratorProfileResponse());
    fixture.detectChanges();

    expect(component.currentRole).toBe('Administrator');
    expect(component.assignmentSectionTitle).toBe('System Access');
    expect(component.assignmentLabel).toBe('Scope');
    expect(component.assignmentDisplayValue).toBe('Platform-wide');
    expect(component.assignmentAccessLevel).toBe('Full access');
    expect(component.memberSince).not.toBe('-');

    const text = fixture.nativeElement.textContent as string;
    const assignmentSection = fixture.nativeElement.querySelector('[data-testid="assignment-section"]') as HTMLElement;
    const assignmentText = assignmentSection.textContent ?? '';
    expect(text).not.toContain('Settings unavailable');
    expect(text).toContain('Public Profile');
    expect(assignmentText).toContain('System Access');
    expect(assignmentText).toContain('Administrator');
    expect(assignmentText).toContain('Scope');
    expect(assignmentText).toContain('Platform-wide');
    expect(assignmentText).toContain('Access Level');
    expect(assignmentText).toContain('Full access');
    expect(text).toContain('Reports');
    expect(text).toContain('Email delivery for administrator reports is not available yet.');
    expect(text).toContain('Reviews');
    expect(assignmentText).not.toContain('Assigned Region');
    expect(assignmentText).not.toContain('Assigned Country');
    expect(assignmentText).not.toContain('No assigned regions');
    expectEmailNotificationsUnavailable();
  });

  it('renders content moderator settings with moderation scope and notifications', () => {
    flushProfile(contentModeratorProfileResponse());
    fixture.detectChanges();

    expect(component.currentRole).toBe('Content Moderator');
    expect(component.assignmentSectionTitle).toBe('Content Moderation Access');
    expect(component.assignmentLabel).toBe('Scope');
    expect(component.assignmentDisplayValue).toBe('Content moderation');
    expect(component.assignmentAccessLevel).toBe('Review moderation');
    expect(component.memberSince).not.toBe('-');

    const text = fixture.nativeElement.textContent as string;
    const assignmentSection = fixture.nativeElement.querySelector('[data-testid="assignment-section"]') as HTMLElement;
    const assignmentText = assignmentSection.textContent ?? '';
    expect(text).not.toContain('Settings unavailable');
    expect(assignmentText).toContain('Content Moderation Access');
    expect(assignmentText).toContain('Content Moderator');
    expect(assignmentText).toContain('Scope');
    expect(assignmentText).toContain('Content moderation');
    expect(assignmentText).toContain('Access Level');
    expect(assignmentText).toContain('Review moderation');
    expect(text).toContain('Review reports');
    expect(text).toContain('Email delivery for review reports is not available yet.');
    expect(text).toContain('Moderation updates');
    expect(text).not.toContain('Assigned Region');
    expect(text).not.toContain('Assigned Territory');
    expectEmailNotificationsUnavailable();
  });


  it('submits editable fields only and keeps unavailable email preferences disabled', () => {
    flushProfile();

    component.firstName.set('Updated');
    component.lastName.set('Manager');
    component.bio.set('Updated bio');
    component.language.set('sr');
    component.timezone.set('Europe/Belgrade');
    component.notifyRequests.set(false);
    component.notifyNewReview.set(true);
    component.onSave();

    const request = http.expectOne('/api/admin/profile');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({
      firstName: 'Updated',
      lastName: 'Manager',
      shortBio: 'Updated bio',
      interfaceLanguage: 'sr',
      timezone: 'Europe/Belgrade',
      emailRequestsEnabled: false,
      emailNewReviewEnabled: false,
    });
    expect(request.request.body.email).toBeUndefined();
    expect(request.request.body.roles).toBeUndefined();
    request.flush({
      ...profileResponse(),
      user: {
        ...profileResponse().user,
        firstName: 'Updated',
        lastName: 'Manager',
        shortBio: 'Updated bio',
      },
      preferences: {
        interfaceLanguage: 'sr',
        timezone: 'Europe/Belgrade',
        emailNotificationsAvailable: false,
        emailRequestsEnabled: false,
        emailNewReviewEnabled: false,
      },
    });

    expect(component.isSaving()).toBe(false);
    expect(component.saveSuccess()).toBe(true);
  });

  it('keeps save disabled until profile settings have changes', () => {
    flushProfile();
    fixture.detectChanges();

    const saveButton = fixture.nativeElement.querySelector('.btn-save') as HTMLButtonElement;
    expect(component.hasChanges).toBe(false);
    expect(saveButton.disabled).toBe(true);

    component.firstName.set('Updated');
    fixture.detectChanges();

    expect(component.hasChanges).toBe(true);
    expect(saveButton.disabled).toBe(false);
  });

  it('cancels unsaved profile edits by resetting to the last loaded profile', () => {
    flushProfile();

    component.firstName.set('Changed');
    component.lastName.set('Changed');
    component.bio.set('Changed bio');
    component.onCancel();

    expect(component.firstName()).toBe('Elena');
    expect(component.lastName()).toBe('Rossi');
    expect(component.bio()).toBe('Regional manager bio');
  });

  it('uploads and removes the avatar through backend endpoints', () => {
    flushProfile();

    const input = document.createElement('input');
    const file = new File([new Uint8Array([1, 2, 3])], 'avatar.png', { type: 'image/png' });
    Object.defineProperty(input, 'files', { value: [file] });

    component.onAvatarChange({ target: input } as unknown as Event);

    const upload = http.expectOne('/api/admin/profile/avatar');
    expect(upload.request.method).toBe('POST');
    expect(upload.request.body instanceof FormData).toBe(true);
    upload.flush({
      avatarUrl: '/media/uploads/avatars/updated.png',
      avatarVersion: 202,
      updatedAt: '2026-05-13T10:00:00Z',
    });

    expect(component.avatarUrl()).toBe('/media/uploads/avatars/updated.png');
    expect(component.avatarImageUrl()).toBe('/media/uploads/avatars/updated.png?v=202');

    component.removeAvatar();

    const remove = http.expectOne('/api/admin/profile/avatar');
    expect(remove.request.method).toBe('DELETE');
    remove.flush({ avatarUrl: '', updatedAt: '2026-05-13T10:01:00Z' });

    expect(component.avatarUrl()).toBe('');
  });

  it('clears loading state on load error', () => {
    const request = http.expectOne('/api/admin/profile');
    request.flush({ message: 'Nope' }, { status: 500, statusText: 'Server Error' });

    expect(component.isLoading()).toBe(false);
    expect(component.errorMessage()).toBe('Nope');
  });

  function flushProfile(response: AdminProfile = profileResponse()): void {
    const request = http.expectOne('/api/admin/profile');
    expect(request.request.method).toBe('GET');
    request.flush(response);
  }

  function profileResponse(): AdminProfile {
    return {
      user: {
        id: 7,
        firstName: 'Elena',
        lastName: 'Rossi',
        displayName: 'Elena Rossi',
        email: 'elena@example.com',
        shortBio: 'Regional manager bio',
        avatarUrl: '/media/uploads/avatars/current.png',
        avatarVersion: 101,
        createdAt: '2024-01-01T00:00:00Z',
        updatedAt: null,
      },
      roles: ['RegionalManager'],
      role: {
        roleCode: 'RegionalManager',
        roleDisplayName: 'Regional Manager',
      },
      assignment: {
        assignmentType: 'Region',
        sectionTitle: 'Regional Manager Assignment',
        label: 'Assigned Region',
        displayValue: 'Coastal Test Region, Testland, Profile Test Region, Testland',
        memberSince: '2024-01-05T09:30:00Z',
        assignedItems: [
          {
            id: 10,
            name: 'Coastal Test Region',
            displayName: 'Coastal Test Region, Testland',
            assignedAt: '2024-01-05T09:30:00Z',
          },
          {
            id: 11,
            name: 'Profile Test Region',
            displayName: 'Profile Test Region, Testland',
            assignedAt: '2024-02-10T12:00:00Z',
          },
        ],
      },
      regionalManager: {
        memberSince: '2024-01-05T09:30:00Z',
        assignedRegions: [
          {
            id: 10,
            name: 'Coastal Test Region',
            displayName: 'Coastal Test Region, Testland',
            territoryName: 'Testland',
            assignedAt: '2024-01-05T09:30:00Z',
          },
          {
            id: 11,
            name: 'Profile Test Region',
            displayName: 'Profile Test Region, Testland',
            territoryName: 'Testland',
            assignedAt: '2024-02-10T12:00:00Z',
          },
        ],
      },
      preferences: {
        interfaceLanguage: 'en-US',
        timezone: 'UTC',
        emailNotificationsAvailable: false,
        emailRequestsEnabled: true,
        emailNewReviewEnabled: false,
      },
    };
  }

  function territoryProfileResponse(): AdminProfile {
    return {
      user: {
        id: 9,
        firstName: 'Mika',
        lastName: 'Mikic',
        displayName: 'Mika Mikic',
        email: 'mika@gmail.com',
        shortBio: 'Territory manager bio',
        avatarUrl: '/media/uploads/avatars/mika.png',
        avatarVersion: 303,
        createdAt: '2024-03-01T00:00:00Z',
        updatedAt: null,
      },
      roles: ['TerritoryManager'],
      role: {
        roleCode: 'TerritoryManager',
        roleDisplayName: 'Territory Manager',
      },
      assignment: {
        assignmentType: 'Territory',
        sectionTitle: 'Territory Manager Assignment',
        label: 'Assigned Territory',
        displayValue: 'Croatia',
        memberSince: '2024-03-15T08:00:00Z',
        assignedItems: [
          {
            id: 1,
            name: 'Croatia',
            displayName: 'Croatia',
            assignedAt: '2024-03-15T08:00:00Z',
          },
        ],
      },
      regionalManager: {
        memberSince: null,
        assignedRegions: [],
      },
      preferences: {
        interfaceLanguage: 'en-US',
        timezone: 'UTC',
        emailNotificationsAvailable: false,
        emailRequestsEnabled: true,
        emailNewReviewEnabled: true,
      },
    };
  }

  function administratorProfileResponse(): AdminProfile {
    return {
      user: {
        id: 46,
        firstName: 'Zika',
        lastName: 'Admin',
        displayName: 'Zika Admin',
        email: 'zika@gmail.com',
        shortBio: 'Administrator profile bio',
        avatarUrl: null,
        avatarVersion: null,
        createdAt: '2023-11-20T10:15:00Z',
        updatedAt: null,
      },
      roles: ['Admin'],
      role: {
        roleCode: 'Admin',
        roleDisplayName: 'Administrator',
      },
      assignment: {
        assignmentType: 'System',
        sectionTitle: 'System Access',
        label: 'Scope',
        displayValue: 'Platform-wide',
        accessLevel: 'Full access',
        memberSince: '2023-11-20T10:15:00Z',
        assignedItems: [],
      },
      regionalManager: {
        memberSince: null,
        assignedRegions: [],
      },
      preferences: {
        interfaceLanguage: 'en-US',
        timezone: 'UTC',
        emailNotificationsAvailable: false,
        emailRequestsEnabled: true,
        emailNewReviewEnabled: true,
      },
    };
  }

  function contentModeratorProfileResponse(): AdminProfile {
    return {
      user: {
        id: 48,
        firstName: 'Content',
        lastName: 'Moderator',
        displayName: 'Content Moderator',
        email: 'moderator@gmail.com',
        shortBio: 'Content moderator profile bio',
        avatarUrl: null,
        avatarVersion: null,
        createdAt: '2023-11-20T10:15:00Z',
        updatedAt: null,
      },
      roles: ['ContentModerator'],
      role: {
        roleCode: 'ContentModerator',
        roleDisplayName: 'Content Moderator',
      },
      assignment: {
        assignmentType: 'ContentModeration',
        sectionTitle: 'Content Moderation Access',
        label: 'Scope',
        displayValue: 'Content moderation',
        accessLevel: 'Review moderation',
        memberSince: '2023-11-20T10:15:00Z',
        assignedItems: [],
      },
      regionalManager: {
        memberSince: null,
        assignedRegions: [],
      },
      preferences: {
        interfaceLanguage: 'en-US',
        timezone: 'UTC',
        emailNotificationsAvailable: false,
        emailRequestsEnabled: true,
        emailNewReviewEnabled: true,
      },
    };
  }

  function expectEmailNotificationsUnavailable(): void {
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Email Notifications');
    expect(text).toContain('Email notifications are not available yet. In-app notifications are currently supported.');

    const switches = Array.from(
      fixture.nativeElement.querySelectorAll('.toggle-track') as NodeListOf<HTMLButtonElement>,
    );
    expect(switches.length).toBe(2);

    for (const toggle of switches) {
      expect(toggle.disabled).toBe(true);
      expect(toggle.classList.contains('on')).toBe(false);
      expect(toggle.getAttribute('aria-checked')).toBe('false');
    }
  }
});
