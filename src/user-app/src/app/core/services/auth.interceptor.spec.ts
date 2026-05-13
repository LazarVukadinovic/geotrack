import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';

import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';
import { TokenRefreshService } from './token-refresh.service';
import { TokenStorageService } from './token-storage.service';

describe('authInterceptor', () => {
  const tokenStorage = {
    hasSession: vi.fn(),
    getAccessToken: vi.fn(),
    isAccessTokenNearExpiry: vi.fn(),
  };
  const tokenRefreshService = {
    refresh: vi.fn(),
  };
  const authService = {
    clearSession: vi.fn(),
  };

  beforeEach(() => {
    vi.clearAllMocks();
    tokenStorage.hasSession.mockReturnValue(true);
    tokenStorage.getAccessToken.mockReturnValue('access-token');
    tokenStorage.isAccessTokenNearExpiry.mockReturnValue(false);

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: TokenStorageService, useValue: tokenStorage },
        { provide: TokenRefreshService, useValue: tokenRefreshService },
        { provide: AuthService, useValue: authService },
      ],
    });
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  it('attaches the bearer token to protected user API requests', () => {
    const http = TestBed.inject(HttpClient);

    http.get('/api/user/me').subscribe();

    const request = TestBed.inject(HttpTestingController).expectOne('/api/user/me');
    expect(request.request.headers.get('Authorization')).toBe('Bearer access-token');

    request.flush({});
  });

  it('does not attach bearer auth to login requests', () => {
    const http = TestBed.inject(HttpClient);

    http.post('/api/auth/login', { email: 'marko.p@example.com', password: 'password123' }).subscribe();

    const request = TestBed.inject(HttpTestingController).expectOne('/api/auth/login');
    expect(request.request.headers.has('Authorization')).toBe(false);

    request.flush({ accessToken: 'next-token', expiresIn: 900, sessionPersistent: false });
  });

  it('does not attach auth when there is no local session hint', () => {
    tokenStorage.hasSession.mockReturnValue(false);
    const http = TestBed.inject(HttpClient);

    http.get('/api/user/me').subscribe();

    const request = TestBed.inject(HttpTestingController).expectOne('/api/user/me');
    expect(request.request.headers.has('Authorization')).toBe(false);

    request.flush({});
  });
});
