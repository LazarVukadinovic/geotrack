import { afterEach } from 'vitest';
import { buildAvatarDisplayUrl } from './avatar-url';
import { environment } from '../../environments/environment';

describe('buildAvatarDisplayUrl', () => {
  const originalMediaBaseUrl = environment.mediaBaseUrl;

  afterEach(() => {
    environment.mediaBaseUrl = originalMediaBaseUrl;
  });

  it('adds a cache-busting version without changing the stored avatar path', () => {
    expect(buildAvatarDisplayUrl('/media/uploads/avatars/user.png', 123))
      .toBe('/media/uploads/avatars/user.png?v=123');
  });

  it('keeps empty and unversioned avatar URLs stable', () => {
    expect(buildAvatarDisplayUrl('', 123)).toBe('');
    expect(buildAvatarDisplayUrl('/media/uploads/avatars/user.png', null))
      .toBe('/media/uploads/avatars/user.png');
  });

  it('resolves relative avatar paths before adding a cache-busting version', () => {
    environment.mediaBaseUrl = 'https://api.example.test/media';

    expect(buildAvatarDisplayUrl('uploads/avatars/user.png', 123))
      .toBe('https://api.example.test/media/uploads/avatars/user.png?v=123');
  });

  it('keeps absolute avatar URLs stable', () => {
    expect(buildAvatarDisplayUrl('https://cdn.example.test/user.png?size=small', 123))
      .toBe('https://cdn.example.test/user.png?size=small&v=123');
  });
});
