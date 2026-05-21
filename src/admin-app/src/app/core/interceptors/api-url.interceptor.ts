import { Injectable } from '@angular/core';
import { HttpEvent, HttpHandler, HttpInterceptor, HttpRequest } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

const apiPrefix = '/api';

@Injectable()
export class ApiUrlInterceptor implements HttpInterceptor {
  intercept(request: HttpRequest<unknown>, next: HttpHandler): Observable<HttpEvent<unknown>> {
    if (!this.isApiRequest(request.url)) {
      return next.handle(request);
    }

    return next.handle(request.clone({
      url: this.toConfiguredApiUrl(request.url),
      withCredentials: true,
    }));
  }

  private toConfiguredApiUrl(url: string): string {
    if (!this.isRelativeApiUrl(url)) {
      return url;
    }

    const suffix = url.slice(apiPrefix.length);
    return `${this.normalizedApiUrl()}${suffix}`;
  }

  private isApiRequest(url: string): boolean {
    return this.isRelativeApiUrl(url) || this.isConfiguredApiUrl(url);
  }

  private isRelativeApiUrl(url: string): boolean {
    return url === apiPrefix || url.startsWith(`${apiPrefix}/`) || url.startsWith(`${apiPrefix}?`);
  }

  private isConfiguredApiUrl(url: string): boolean {
    try {
      const origin = typeof window === 'undefined' ? 'http://localhost' : window.location.origin;
      const parsedUrl = new URL(url, origin);
      const parsedApiUrl = new URL(this.normalizedApiUrl(), origin);

      return parsedUrl.origin === parsedApiUrl.origin &&
        (
          parsedUrl.pathname === parsedApiUrl.pathname ||
          parsedUrl.pathname.startsWith(`${parsedApiUrl.pathname}/`)
        );
    } catch {
      return false;
    }
  }

  private normalizedApiUrl(): string {
    return environment.apiUrl.replace(/\/+$/, '');
  }
}
