export function buildAvatarDisplayUrl(
  avatarUrl: string | null | undefined,
  avatarVersion: number | string | null | undefined,
): string {
  const trimmedUrl = avatarUrl?.trim();
  if (!trimmedUrl) {
    return '';
  }

  if (avatarVersion === null || avatarVersion === undefined || avatarVersion === '') {
    return trimmedUrl;
  }

  const separator = trimmedUrl.includes('?') ? '&' : '?';
  return `${trimmedUrl}${separator}v=${encodeURIComponent(String(avatarVersion))}`;
}
