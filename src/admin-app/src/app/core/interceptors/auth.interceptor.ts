import { Injectable, Injector } from '@angular/core';
import { HttpErrorResponse, HttpEvent, HttpHandler, HttpInterceptor, HttpRequest } from '@angular/common/http';
import { Observable, catchError, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';
import { environment } from '../../../environments/environment';

const apiPathPrefix = '/api';

@Injectable()
export class AuthInterceptor implements HttpInterceptor {
  constructor(private readonly injector: Injector) {}

  intercept(request: HttpRequest<unknown>, next: HttpHandler): Observable<HttpEvent<unknown>> {
    if (!this.shouldHandle(request.url)) {
      return next.handle(request);
    }

    const auth = this.injector.get(AuthService);
    const token = auth.getAccessToken();
    const headers = token && this.shouldAttachToken(request.url)
      ? request.headers.set('Authorization', `Bearer ${token}`)
      : request.headers;

    const authorizedRequest = request.clone({
      headers,
      withCredentials: true,
    });

    return next.handle(authorizedRequest).pipe(
      catchError(error => {
        if (error instanceof HttpErrorResponse && error.status === 401 && !this.isAuthEndpoint(request.url)) {
          auth.clearSession();
        }

        return throwError(() => error);
      }),
    );
  }

  private shouldHandle(url: string): boolean {
    return this.isConfiguredApiRequest(url);
  }

  private shouldAttachToken(url: string): boolean {
    const path = this.apiPath(url);

    return !path.startsWith('/api/auth/') &&
      !path.startsWith('/api/admin/auth/login') &&
      !path.startsWith('/api/admin/auth/refresh') &&
      !path.startsWith('/api/admin/auth/logout');
  }

  private isAuthEndpoint(url: string): boolean {
    const path = this.apiPath(url);
    return path.startsWith('/api/admin/auth/') || path.startsWith('/api/auth/');
  }

  private isConfiguredApiRequest(url: string): boolean {
    if (this.isRelativeApiPath(url)) {
      return true;
    }

    try {
      const origin = typeof window === 'undefined' ? 'http://localhost' : window.location.origin;
      const parsedUrl = new URL(url, origin);
      const configuredApiUrl = new URL(environment.apiUrl.replace(/\/+$/, ''), origin);
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

  private apiPath(url: string): string {
    try {
      const origin = typeof window === 'undefined' ? 'http://localhost' : window.location.origin;
      return new URL(url, origin).pathname;
    } catch {
      return url;
    }
  }

  private isRelativeApiPath(url: string): boolean {
    return url === apiPathPrefix ||
      url.startsWith(`${apiPathPrefix}/`) ||
      url.startsWith(`${apiPathPrefix}?`);
  }
}
