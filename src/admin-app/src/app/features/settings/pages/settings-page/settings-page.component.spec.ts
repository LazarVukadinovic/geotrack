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
  });

  it('submits editable fields only and includes notification preferences', () => {
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
      emailNewReviewEnabled: true,
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
        emailRequestsEnabled: false,
        emailNewReviewEnabled: true,
      },
    });

    expect(component.isSaving()).toBe(false);
    expect(component.saveSuccess()).toBe(true);
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
        emailRequestsEnabled: true,
        emailNewReviewEnabled: true,
      },
    };
  }
});
