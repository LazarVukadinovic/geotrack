import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { authErrorInterceptor } from './auth-error.interceptor';
import { AuthService } from './auth.service';
import { TokenRefreshService } from './token-refresh.service';

describe('authErrorInterceptor', () => {
  const tokenRefreshService = {
    refresh: vi.fn(),
  };
  const authService = {
    clearSession: vi.fn(),
  };

  beforeEach(() => {
    vi.clearAllMocks();
    tokenRefreshService.refresh.mockReturnValue(of('fresh-token'));

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authErrorInterceptor])),
        provideHttpClientTesting(),
        { provide: TokenRefreshService, useValue: tokenRefreshService },
        { provide: AuthService, useValue: authService },
      ],
    });
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  it('refreshes and retries the original authorized request after a protected 401', () => {
    const http = TestBed.inject(HttpClient);

    http.get('/api/user/me', { headers: { Authorization: 'Bearer expired-token' } }).subscribe();

    const first = TestBed.inject(HttpTestingController).expectOne('/api/user/me');
    expect(first.request.headers.get('Authorization')).toBe('Bearer expired-token');
    first.flush({ message: 'Unauthorized' }, { status: 401, statusText: 'Unauthorized' });

    expect(tokenRefreshService.refresh).toHaveBeenCalledTimes(1);

    const retry = TestBed.inject(HttpTestingController).expectOne('/api/user/me');
    expect(retry.request.headers.get('Authorization')).toBe('Bearer fresh-token');
    retry.flush({});

    expect(authService.clearSession).not.toHaveBeenCalled();
  });

  it('clears local session once when refresh fails after a protected 401', () => {
    const http = TestBed.inject(HttpClient);
    tokenRefreshService.refresh.mockReturnValue(throwError(() => new Error('refresh failed')));

    http.get('/api/user/me', { headers: { Authorization: 'Bearer expired-token' } }).subscribe({
      error: () => undefined,
    });

    const first = TestBed.inject(HttpTestingController).expectOne('/api/user/me');
    first.flush({ message: 'Unauthorized' }, { status: 401, statusText: 'Unauthorized' });

    expect(tokenRefreshService.refresh).toHaveBeenCalledTimes(1);
    expect(authService.clearSession).toHaveBeenCalledTimes(1);
  });

  it('does not refresh 403 responses', () => {
    const http = TestBed.inject(HttpClient);

    http.get('/api/user/me', { headers: { Authorization: 'Bearer access-token' } }).subscribe({
      error: () => undefined,
    });

    const request = TestBed.inject(HttpTestingController).expectOne('/api/user/me');
    request.flush({ message: 'Forbidden' }, { status: 403, statusText: 'Forbidden' });

    expect(tokenRefreshService.refresh).not.toHaveBeenCalled();
    expect(authService.clearSession).not.toHaveBeenCalled();
  });

  it('does not recursively refresh auth endpoint 401 responses', () => {
    const http = TestBed.inject(HttpClient);

    http.post('/api/auth/refresh', {}, { headers: { Authorization: 'Bearer expired-token' } }).subscribe({
      error: () => undefined,
    });

    const request = TestBed.inject(HttpTestingController).expectOne('/api/auth/refresh');
    request.flush({ message: 'Invalid refresh token' }, { status: 401, statusText: 'Unauthorized' });

    expect(tokenRefreshService.refresh).not.toHaveBeenCalled();
  });
});
