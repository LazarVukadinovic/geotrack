import { HTTP_INTERCEPTORS, HttpClient, provideHttpClient, withInterceptorsFromDi } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';

import { AuthInterceptor } from './auth.interceptor';
import { AuthService } from '../services/auth.service';

describe('AuthInterceptor', () => {
  const authService = {
    getAccessToken: vi.fn(),
    clearSession: vi.fn(),
  };

  beforeEach(() => {
    vi.clearAllMocks();
    authService.getAccessToken.mockReturnValue('access-token');

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptorsFromDi()),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: authService },
        { provide: HTTP_INTERCEPTORS, useClass: AuthInterceptor, multi: true },
      ],
    });
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  it('attaches bearer auth and credentials to protected admin API requests', () => {
    const http = TestBed.inject(HttpClient);

    http.get('/api/admin/regional/regions').subscribe();

    const request = TestBed.inject(HttpTestingController).expectOne('/api/admin/regional/regions');
    expect(request.request.headers.get('Authorization')).toBe('Bearer access-token');
    expect(request.request.withCredentials).toBe(true);

    request.flush([]);
  });

  it('sends admin login with credentials but without an Authorization header', () => {
    const http = TestBed.inject(HttpClient);

    http.post('/api/admin/auth/login', { email: 'regional@example.com', password: 'Password123!' }).subscribe();

    const request = TestBed.inject(HttpTestingController).expectOne('/api/admin/auth/login');
    expect(request.request.headers.has('Authorization')).toBe(false);
    expect(request.request.withCredentials).toBe(true);

    request.flush({ accessToken: 'next-token', user: { roles: ['RegionalManager'] } });
  });

  it('clears the local session when a protected admin API request returns 401', () => {
    const http = TestBed.inject(HttpClient);

    http.get('/api/admin/regional/regions').subscribe({ error: () => undefined });

    const request = TestBed.inject(HttpTestingController).expectOne('/api/admin/regional/regions');
    request.flush({ message: 'Unauthorized' }, { status: 401, statusText: 'Unauthorized' });

    expect(authService.clearSession).toHaveBeenCalledTimes(1);
  });
});
