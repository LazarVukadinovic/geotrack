import { afterEach, describe, expect, it } from 'vitest';
import { environment } from '../../environments/environment';
import { resolveMediaUrl } from './api-url';

describe('resolveMediaUrl', () => {
  const originalMediaBaseUrl = environment.mediaBaseUrl;

  afterEach(() => {
    environment.mediaBaseUrl = originalMediaBaseUrl;
  });

  it('keeps absolute HTTP URLs unchanged', () => {
    expect(resolveMediaUrl('https://cdn.example.test/avatars/user.jpg?size=small'))
      .toBe('https://cdn.example.test/avatars/user.jpg?size=small');
  });

  it('resolves root-relative and relative upload paths against the media base URL', () => {
    environment.mediaBaseUrl = 'https://api.example.test/media';

    expect(resolveMediaUrl('/uploads/avatars/a.jpg')).toBe('https://api.example.test/media/uploads/avatars/a.jpg');
    expect(resolveMediaUrl('uploads/avatars/a.jpg')).toBe('https://api.example.test/media/uploads/avatars/a.jpg');
  });

  it('resolves media paths without duplicating the media prefix', () => {
    environment.mediaBaseUrl = 'https://api.example.test/media';

    expect(resolveMediaUrl('/media/uploads/avatars/a.jpg?etag=1'))
      .toBe('https://api.example.test/media/uploads/avatars/a.jpg?etag=1');
  });

  it('strips an accidental API prefix before resolving media paths', () => {
    environment.mediaBaseUrl = 'https://api.example.test/media';

    expect(resolveMediaUrl('/api/media/uploads/avatars/a.jpg'))
      .toBe('https://api.example.test/media/uploads/avatars/a.jpg');
  });

  it('returns null for empty values', () => {
    expect(resolveMediaUrl(null)).toBeNull();
    expect(resolveMediaUrl(undefined)).toBeNull();
    expect(resolveMediaUrl('   ')).toBeNull();
  });
});
