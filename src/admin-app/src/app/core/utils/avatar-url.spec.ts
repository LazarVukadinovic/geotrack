import { buildAvatarDisplayUrl } from './avatar-url';

describe('buildAvatarDisplayUrl', () => {
  it('adds a cache-busting version without changing the stored avatar path', () => {
    expect(buildAvatarDisplayUrl('/media/uploads/avatars/admin.png', 456))
      .toBe('/media/uploads/avatars/admin.png?v=456');
  });

  it('appends the version to URLs that already have a query string', () => {
    expect(buildAvatarDisplayUrl('/media/uploads/avatars/admin.png?size=small', 456))
      .toBe('/media/uploads/avatars/admin.png?size=small&v=456');
  });
});
