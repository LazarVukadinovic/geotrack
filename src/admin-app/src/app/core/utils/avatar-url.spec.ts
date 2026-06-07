import { afterEach } from 'vitest';
import { buildAvatarDisplayUrl } from './avatar-url';
import { environment } from '../../environments/environment';

describe('buildAvatarDisplayUrl', () => {
  const originalMediaBaseUrl = environment.mediaBaseUrl;

  afterEach(() => {
    environment.mediaBaseUrl = originalMediaBaseUrl;
  });

  it('adds a cache-busting version without changing the stored avatar path', () => {
    expect(buildAvatarDisplayUrl('/media/uploads/avatars/admin.png', 456))
      .toBe('/media/uploads/avatars/admin.png?v=456');
  });

  it('appends the version to URLs that already have a query string', () => {
    expect(buildAvatarDisplayUrl('/media/uploads/avatars/admin.png?size=small', 456))
      .toBe('/media/uploads/avatars/admin.png?size=small&v=456');
  });

  it('resolves relative avatar paths against the configured media base URL', () => {
    environment.mediaBaseUrl = 'https://api.example.test/media';

    expect(buildAvatarDisplayUrl('uploads/avatars/admin.png', 456))
      .toBe('https://api.example.test/media/uploads/avatars/admin.png?v=456');
  });
});
