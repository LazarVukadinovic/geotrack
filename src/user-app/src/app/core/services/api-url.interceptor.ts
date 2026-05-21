import { HttpInterceptorFn } from '@angular/common/http';
import { environment } from '../../../environments/environment';

const apiPrefix = '/api';

export const apiUrlInterceptor: HttpInterceptorFn = (req, next) => {
  if (!isApiRequest(req.url)) {
    return next(req);
  }

  return next(req.clone({
    url: toConfiguredApiUrl(req.url),
    withCredentials: true,
  }));
};

export function toConfiguredApiUrl(url: string): string {
  if (!isRelativeApiUrl(url)) {
    return url;
  }

  const suffix = url.slice(apiPrefix.length);
  return `${normalizedApiUrl()}${suffix}`;
}

export function isApiRequest(url: string): boolean {
  return isRelativeApiUrl(url) || isConfiguredApiUrl(url);
}

function isRelativeApiUrl(url: string): boolean {
  return url === apiPrefix || url.startsWith(`${apiPrefix}/`) || url.startsWith(`${apiPrefix}?`);
}

function isConfiguredApiUrl(url: string): boolean {
  try {
    const origin = typeof window === 'undefined' ? 'http://localhost' : window.location.origin;
    const parsedUrl = new URL(url, origin);
    const parsedApiUrl = new URL(normalizedApiUrl(), origin);

    return parsedUrl.origin === parsedApiUrl.origin &&
      (
        parsedUrl.pathname === parsedApiUrl.pathname ||
        parsedUrl.pathname.startsWith(`${parsedApiUrl.pathname}/`)
      );
  } catch {
    return false;
  }
}

function normalizedApiUrl(): string {
  return environment.apiUrl.replace(/\/+$/, '');
}
