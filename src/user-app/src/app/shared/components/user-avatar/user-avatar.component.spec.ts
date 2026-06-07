import { ComponentFixture, TestBed } from '@angular/core/testing';
import { describe, expect, it, beforeEach } from 'vitest';
import { UserAvatarComponent } from './user-avatar.component';

describe('UserAvatarComponent', () => {
  let fixture: ComponentFixture<UserAvatarComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [UserAvatarComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(UserAvatarComponent);
  });

  it('renders the image when a valid imageUrl exists', () => {
    fixture.componentRef.setInput('imageUrl', '/media/uploads/avatars/user.png');
    fixture.componentRef.setInput('displayName', 'Mina Traveler');
    fixture.detectChanges();

    const image = fixture.nativeElement.querySelector('img') as HTMLImageElement | null;

    expect(image).toBeTruthy();
    expect(image?.getAttribute('src')).toBe('/media/uploads/avatars/user.png');
    expect(image?.getAttribute('alt')).toBe('Profile photo of Mina Traveler');
  });

  it('renders initials fallback when imageUrl is missing', () => {
    fixture.componentRef.setInput('displayName', 'Mina Traveler');
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('img')).toBeNull();
    expect(fixture.nativeElement.querySelector('.user-avatar__initials')?.textContent?.trim()).toBe('MT');
  });

  it('renders fallback when image loading fails', () => {
    fixture.componentRef.setInput('imageUrl', '/media/uploads/avatars/missing.png');
    fixture.componentRef.setInput('displayName', 'Broken User');
    fixture.detectChanges();

    const image = fixture.nativeElement.querySelector('img') as HTMLImageElement;
    image.dispatchEvent(new Event('error'));
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('img')).toBeNull();
    expect(fixture.nativeElement.querySelector('.user-avatar__initials')?.textContent?.trim()).toBe('BU');
  });

  it('falls back to the default icon when no name or email exists', () => {
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('img')).toBeNull();
    expect(fixture.nativeElement.querySelector('.user-avatar__icon')).toBeTruthy();
  });
});
