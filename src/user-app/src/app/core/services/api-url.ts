import { environment } from '../../environments/environment';

const API_PREFIX = '/api';
const MEDIA_PREFIX = '/media';

export function resolveApiUrl(url: string): string {
  return rewritePrefixedUrl(url, API_PREFIX, environment.apiBaseUrl);
}

export function resolveMediaUrl(url: string | null | undefined): string | null {
  const normalizedUrl = normalizeMediaInput(url);
  if (!normalizedUrl) {
    return null;
  }

  if (isAbsoluteHttpUrl(normalizedUrl)) {
    return normalizedUrl;
  }

  return rewritePrefixedUrl(normalizedUrl, MEDIA_PREFIX, environment.mediaBaseUrl);
}

export function isConfiguredApiUrl(url: string): boolean {
  return isConfiguredPrefixUrl(url, API_PREFIX, environment.apiBaseUrl);
}

export function isApiAuthEndpoint(url: string): boolean {
  return getPathname(url).startsWith('/api/auth/');
}

export function isApiPath(url: string): boolean {
  return isPathMatch(url, API_PREFIX);
}

export function isMediaPath(url: string): boolean {
  return isPathMatch(url, MEDIA_PREFIX);
}

function rewritePrefixedUrl(url: string, prefix: string, configuredBaseUrl: string): string {
  if (!isPathMatch(url, prefix)) {
    return url;
  }

  const baseUrl = normalizeBaseUrl(configuredBaseUrl, prefix);
  return `${baseUrl}${url.slice(prefix.length)}`;
}

function normalizeMediaInput(url: string | null | undefined): string | null {
  const trimmed = url?.trim();
  if (!trimmed) {
    return null;
  }

  if (isAbsoluteHttpUrl(trimmed)) {
    return trimmed;
  }

  const withoutApiPrefix = stripLeadingPrefix(trimmed, API_PREFIX);
  const withMediaPrefix = withoutApiPrefix.startsWith(`${MEDIA_PREFIX}/`) || withoutApiPrefix === MEDIA_PREFIX
    ? withoutApiPrefix
    : `/${withoutApiPrefix.replace(/^\/+/, '')}`;

  if (withMediaPrefix === MEDIA_PREFIX || withMediaPrefix.startsWith(`${MEDIA_PREFIX}/`)) {
    return withMediaPrefix;
  }

  return `${MEDIA_PREFIX}${withMediaPrefix}`;
}

function stripLeadingPrefix(value: string, prefix: string): string {
  if (value === prefix) {
    return '';
  }

  if (value.startsWith(`${prefix}/`) || value.startsWith(`${prefix}?`)) {
    return value.slice(prefix.length);
  }

  const relativePrefix = prefix.replace(/^\/+/, '');
  if (value === relativePrefix) {
    return '';
  }

  if (value.startsWith(`${relativePrefix}/`) || value.startsWith(`${relativePrefix}?`)) {
    return value.slice(relativePrefix.length);
  }

  return value;
}

function isAbsoluteHttpUrl(value: string): boolean {
  return /^https?:\/\//i.test(value);
}

function isConfiguredPrefixUrl(url: string, prefix: string, configuredBaseUrl: string): boolean {
  const parsedUrl = parseUrl(url);
  const parsedBase = parseUrl(normalizeBaseUrl(configuredBaseUrl, prefix));
  if (!parsedUrl || !parsedBase || parsedUrl.origin !== parsedBase.origin) {
    return false;
  }

  return parsedUrl.pathname === parsedBase.pathname
    || parsedUrl.pathname.startsWith(`${parsedBase.pathname}/`);
}

function isPathMatch(url: string, prefix: string): boolean {
  return url === prefix
    || url.startsWith(`${prefix}/`)
    || url.startsWith(`${prefix}?`);
}

function getPathname(url: string): string {
  return parseUrl(url)?.pathname ?? url;
}

function parseUrl(url: string): URL | null {
  try {
    return new URL(url, getCurrentOrigin());
  } catch {
    return null;
  }
}

function normalizeBaseUrl(value: string | undefined, fallback: string): string {
  const trimmed = value?.trim() || fallback;
  return trimmed.replace(/\/+$/, '');
}

function getCurrentOrigin(): string {
  return typeof window !== 'undefined' && window.location?.origin
    ? window.location.origin
    : 'http://localhost';
}
