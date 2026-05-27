import { buildAvatarDisplayUrl } from './avatar-url';

describe('buildAvatarDisplayUrl', () => {
  it('adds a cache-busting version without changing the stored avatar path', () => {
    expect(buildAvatarDisplayUrl('/media/uploads/avatars/user.png', 123))
      .toBe('/media/uploads/avatars/user.png?v=123');
  });

  it('keeps empty and unversioned avatar URLs stable', () => {
    expect(buildAvatarDisplayUrl('', 123)).toBe('');
    expect(buildAvatarDisplayUrl('/media/uploads/avatars/user.png', null))
      .toBe('/media/uploads/avatars/user.png');
  });
});
