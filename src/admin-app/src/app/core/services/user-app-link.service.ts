import { Inject, Injectable, InjectionToken } from '@angular/core';
import { environment } from '../../../environments/environment';

export const USER_APP_BASE_URL = new InjectionToken<string>('USER_APP_BASE_URL', {
  providedIn: 'root',
  factory: () => environment.userAppBaseUrl,
});

@Injectable({ providedIn: 'root' })
export class UserAppLinkService {
  constructor(@Inject(USER_APP_BASE_URL) private readonly userAppBaseUrl: string) {}

  toUserAppUrl(publicDetailRoute?: string | null): string | null {
    const route = this.normalizePublicRoute(publicDetailRoute);
    if (!route) {
      return null;
    }

    const baseUrl = (this.userAppBaseUrl ?? '').trim();
    if (!baseUrl || baseUrl === '/') {
      return route;
    }

    return `${baseUrl.replace(/\/+$/, '')}${route}`;
  }

  private normalizePublicRoute(publicDetailRoute?: string | null): string | null {
    const route = publicDetailRoute?.trim();
    if (!route || !route.startsWith('/') || route.startsWith('//') || route.includes('\\')) {
      return null;
    }

    const lowerRoute = route.toLowerCase();
    if (
      lowerRoute === '/admin' ||
      lowerRoute.startsWith('/admin/') ||
      lowerRoute.startsWith('/admin?') ||
      lowerRoute === '/api' ||
      lowerRoute.startsWith('/api/')
    ) {
      return null;
    }

    return route;
  }
}
