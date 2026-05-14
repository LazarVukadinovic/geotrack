import { Injectable } from '@angular/core';
import { HttpEvent, HttpHandler, HttpInterceptor, HttpRequest } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

const apiPathPrefix = '/api';

@Injectable()
export class ApiBaseUrlInterceptor implements HttpInterceptor {
  intercept(request: HttpRequest<unknown>, next: HttpHandler): Observable<HttpEvent<unknown>> {
    if (!this.isConfiguredApiRequest(request.url)) {
      return next.handle(request);
    }

    return next.handle(request.clone({
      url: this.apiUrl(request.url),
      withCredentials: true,
    }));
  }

  private apiUrl(url: string): string {
    if (!this.isRelativeApiPath(url)) {
      return url;
    }

    return `${this.normalizedApiBaseUrl()}${url.slice(apiPathPrefix.length)}`;
  }

  private isConfiguredApiRequest(url: string): boolean {
    if (this.isRelativeApiPath(url)) {
      return true;
    }

    try {
      const origin = typeof window === 'undefined' ? 'http://localhost' : window.location.origin;
      const parsedUrl = new URL(url, origin);
      const configuredApiUrl = new URL(this.normalizedApiBaseUrl(), origin);
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

  private normalizedApiBaseUrl(): string {
    return environment.apiUrl.replace(/\/+$/, '');
  }

  private isRelativeApiPath(url: string): boolean {
    return url === apiPathPrefix ||
      url.startsWith(`${apiPathPrefix}/`) ||
      url.startsWith(`${apiPathPrefix}?`);
  }
}
