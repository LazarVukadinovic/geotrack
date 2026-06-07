import { resolveMediaUrl } from '../services/api-url';

export function buildAvatarDisplayUrl(
  avatarUrl: string | null | undefined,
  avatarVersion: number | string | null | undefined,
): string {
  const resolvedUrl = resolveMediaUrl(avatarUrl);
  if (!resolvedUrl) {
    return '';
  }

  if (avatarVersion === null || avatarVersion === undefined || avatarVersion === '') {
    return resolvedUrl;
  }

  const separator = resolvedUrl.includes('?') ? '&' : '?';
  return `${resolvedUrl}${separator}v=${encodeURIComponent(String(avatarVersion))}`;
}
