import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { EditProfileComponent } from './edit-profile.component';
import { UserService } from '../../../../core/services/user.service';

describe('EditProfile', () => {
  let component: EditProfileComponent;
  let fixture: ComponentFixture<EditProfileComponent>;
  let httpMock: HttpTestingController;

  const router = {
    navigate: vi.fn(),
  };

  const userService = {
    load: vi.fn(() =>
      of({
        id: 1,
        username: 'marko',
        email: 'marko.p@example.com',
        firstName: 'Marko',
        lastName: 'Petrovic',
      }),
    ),
    patchProfile: vi.fn(),
  };

  const profileResponse = {
    id: 1,
    username: 'marko',
    email: 'marko.p@example.com',
    firstName: 'Marko',
    lastName: 'Petrovic',
    phoneNumber: '+381601234567',
    avatarUrl: '/media/uploads/avatars/current-avatar.png',
    location: 'Belgrade, Serbia',
  };

  beforeEach(async () => {
    vi.clearAllMocks();

    await TestBed.configureTestingModule({
      imports: [EditProfileComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: Router, useValue: router },
        { provide: ActivatedRoute, useValue: { queryParams: of({}) } },
        { provide: UserService, useValue: userService },
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(EditProfileComponent);
    component = fixture.componentInstance;

    httpMock.expectOne('/api/user/me').flush(profileResponse);
    await fixture.whenStable();
  });

  afterEach(() => {
    httpMock.verify();
    vi.useRealTimers();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should load the current profile from the API', () => {
    expect(component.fullName).toBe('Marko Petrovic');
    expect(component.email).toBe('marko.p@example.com');
    expect(component.phone).toBe('+381601234567');
    expect(component.location).toBe('Belgrade, Serbia');
    expect(component.avatarUrl).toBe('/media/uploads/avatars/current-avatar.png');
  });

  it('should save the split name and refresh UserService before navigating', () => {
    vi.useFakeTimers();
    userService.load.mockClear();
    router.navigate.mockClear();

    component.fullName = 'Ana Markovic';
    component.phone = '+38160111222';
    component.location = 'Novi Sad, Serbia';

    component.saveChanges();

    const request = httpMock.expectOne('/api/user/me');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({
      firstName: 'Ana',
      lastName: 'Markovic',
      phoneNumber: '+38160111222',
      location: 'Novi Sad, Serbia',
    });

    request.flush(profileResponse);

    expect(userService.load).toHaveBeenCalledTimes(1);
    vi.advanceTimersByTime(600);
    expect(router.navigate).toHaveBeenCalledWith(['/profile']);
  });

  it('should ignore save requests while a save is already in progress', () => {
    userService.load.mockClear();
    component.isSaving = true;

    component.saveChanges();

    httpMock.expectNone('/api/user/me');
    expect(userService.load).not.toHaveBeenCalled();
  });

  it('should upload an avatar and patch the shared UserService state', () => {
    userService.patchProfile.mockClear();

    const file = new File([new Uint8Array([1, 2, 3])], 'avatar.png', { type: 'image/png' });

    component.onAvatarSelected({
      target: {
        files: [file],
        value: 'C:\\fakepath\\avatar.png',
      },
    } as unknown as Event);

    const request = httpMock.expectOne('/api/user/me/avatar');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toBeInstanceOf(FormData);

    request.flush({
      avatarUrl: '/media/uploads/avatars/updated-avatar.png',
    });

    expect(component.avatarUrl).toBe('/media/uploads/avatars/updated-avatar.png');
    expect(component.isUploadingAvatar).toBe(false);
    expect(component.avatarError).toBe('');
    expect(userService.patchProfile).toHaveBeenCalledWith({
      avatarUrl: '/media/uploads/avatars/updated-avatar.png',
    });
  });
});
