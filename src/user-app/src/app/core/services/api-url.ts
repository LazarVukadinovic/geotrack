import { environment } from '../../../environments/environment';

const apiPathPrefix = '/api';

export function apiUrl(url: string): string {
  if (!isRelativeApiPath(url)) {
    return url;
  }

  return `${normalizedApiBaseUrl()}${url.slice(apiPathPrefix.length)}`;
}

export function isConfiguredApiRequest(url: string): boolean {
  if (isRelativeApiPath(url)) {
    return true;
  }

  try {
    const origin = typeof window === 'undefined' ? 'http://localhost' : window.location.origin;
    const parsedUrl = new URL(url, origin);
    const configuredApiUrl = new URL(normalizedApiBaseUrl(), origin);
    const configuredPath = configuredApiUrl.pathname.replace(/\/+$/, '');

    return parsedUrl.origin === configuredApiUrl.origin &&
      (
        parsedUrl.pathname === configuredPath ||
        parsedUrl.pathname.startsWith(`${configuredPath}/`)
      );
  } catch {
    return false;
  }
}

function normalizedApiBaseUrl(): string {
  return environment.apiUrl.replace(/\/+$/, '');
}

function isRelativeApiPath(url: string): boolean {
  return url === apiPathPrefix ||
    url.startsWith(`${apiPathPrefix}/`) ||
    url.startsWith(`${apiPathPrefix}?`);
}
