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
    expect(component.currentRole).toBe('Regional Manager');
    expect(component.assignedRegion).toBe('Coastal Test Region, Testland, Profile Test Region, Testland');
    expect(component.memberSince).not.toBe('-');

    const emailInput = fixture.nativeElement.querySelector('input[type="email"]') as HTMLInputElement;
    expect(emailInput.disabled).toBe(true);
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
    upload.flush({ avatarUrl: '/media/uploads/avatars/updated.png', updatedAt: '2026-05-13T10:00:00Z' });

    expect(component.avatarUrl()).toBe('/media/uploads/avatars/updated.png');

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
        createdAt: '2024-01-01T00:00:00Z',
        updatedAt: null,
      },
      roles: ['RegionalManager'],
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
});
