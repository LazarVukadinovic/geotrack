import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { Router } from '@angular/router';
import { vi } from 'vitest';
import { ProfileComponent } from './profile.component';
import { CollectionsService } from '../../services/collections';
import { UserService } from '../../../../core/services/user.service';

describe('Profile', () => {
  let component: ProfileComponent;
  let fixture: ComponentFixture<ProfileComponent>;

  const router = {
    navigate: vi.fn(),
  };

  beforeEach(async () => {
    vi.clearAllMocks();

    await TestBed.configureTestingModule({
      imports: [ProfileComponent],
      providers: [
        { provide: Router, useValue: router },
        {
          provide: CollectionsService,
          useValue: {
            collections: signal([]),
          },
        },
        {
          provide: UserService,
          useValue: {
            fullName: signal('Test User'),
            email: signal('test@example.com'),
            avatarUrl: signal('/media/uploads/avatars/test-user.png'),
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ProfileComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
