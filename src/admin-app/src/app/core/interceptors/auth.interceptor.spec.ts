import { HTTP_INTERCEPTORS, HttpClient, provideHttpClient, withInterceptorsFromDi } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { of } from 'rxjs';
import { vi } from 'vitest';

import { AuthInterceptor } from './auth.interceptor';
import { AdminFormDraftService } from '../services/admin-form-draft.service';
import { AuthService } from '../services/auth.service';

describe('AuthInterceptor', () => {
  const authService = {
    getAccessToken: vi.fn(),
    restoreSession: vi.fn(),
    clearSession: vi.fn(),
  };
  const router = {
    url: '/destinations',
    navigate: vi.fn(),
  };
  const draftService = {
    saveActiveDraftOnSessionExpiry: vi.fn(),
  };

  beforeEach(() => {
    vi.clearAllMocks();
    authService.getAccessToken.mockReturnValue('access-token');
    authService.restoreSession.mockReturnValue(of({ id: '7', email: 'regional@example.com' }));
    draftService.saveActiveDraftOnSessionExpiry.mockReturnValue(false);
    router.url = '/destinations';
    router.navigate.mockResolvedValue(true);

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptorsFromDi()),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: authService },
        { provide: AdminFormDraftService, useValue: draftService },
        { provide: Router, useValue: router },
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

  it('refreshes the admin session and retries a protected API request after 401', () => {
    const http = TestBed.inject(HttpClient);
    authService.getAccessToken
      .mockReturnValueOnce('expired-token')
      .mockReturnValueOnce('fresh-token');

    http.get('/api/admin/regional/regions').subscribe();

    const first = TestBed.inject(HttpTestingController).expectOne('/api/admin/regional/regions');
    expect(first.request.headers.get('Authorization')).toBe('Bearer expired-token');
    first.flush({ message: 'Unauthorized' }, { status: 401, statusText: 'Unauthorized' });

    expect(authService.restoreSession).toHaveBeenCalledWith({ forceRefresh: true });

    const retry = TestBed.inject(HttpTestingController).expectOne('/api/admin/regional/regions');
    expect(retry.request.headers.get('Authorization')).toBe('Bearer fresh-token');
    retry.flush([]);

    expect(authService.clearSession).not.toHaveBeenCalled();
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('clears the local session when refresh cannot recover a protected admin 401', () => {
    const http = TestBed.inject(HttpClient);
    authService.restoreSession.mockReturnValue(of(null));

    http.get('/api/admin/regional/regions').subscribe({ error: () => undefined });

    const request = TestBed.inject(HttpTestingController).expectOne('/api/admin/regional/regions');
    request.flush({ message: 'Unauthorized' }, { status: 401, statusText: 'Unauthorized' });

    expect(authService.clearSession).toHaveBeenCalledTimes(1);
    expect(draftService.saveActiveDraftOnSessionExpiry).toHaveBeenCalledTimes(1);
    expect(router.navigate).toHaveBeenCalledWith(['/login'], {
      queryParams: { sessionExpired: '1', returnUrl: '/destinations' },
      replaceUrl: true,
    });
  });

  it('includes a draft-saved flag when a form snapshot was preserved before redirect', () => {
    const http = TestBed.inject(HttpClient);
    authService.restoreSession.mockReturnValue(of(null));
    draftService.saveActiveDraftOnSessionExpiry.mockReturnValue(true);

    http.get('/api/admin/regional/regions').subscribe({ error: () => undefined });

    const request = TestBed.inject(HttpTestingController).expectOne('/api/admin/regional/regions');
    request.flush({ message: 'Unauthorized' }, { status: 401, statusText: 'Unauthorized' });

    expect(router.navigate).toHaveBeenCalledWith(['/login'], {
      queryParams: { sessionExpired: '1', draftSaved: '1', returnUrl: '/destinations' },
      replaceUrl: true,
    });
  });

  it('does not redirect when login returns 401 for bad credentials', () => {
    const http = TestBed.inject(HttpClient);

    http.post('/api/admin/auth/login', { email: 'regional@example.com', password: 'bad' }).subscribe({
      error: () => undefined,
    });

    const request = TestBed.inject(HttpTestingController).expectOne('/api/admin/auth/login');
    request.flush({ message: 'Invalid credentials' }, { status: 401, statusText: 'Unauthorized' });

    expect(authService.clearSession).not.toHaveBeenCalled();
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('does not refresh or redirect when a protected API request returns 403', () => {
    const http = TestBed.inject(HttpClient);

    http.get('/api/admin/regional/regions').subscribe({ error: () => undefined });

    const request = TestBed.inject(HttpTestingController).expectOne('/api/admin/regional/regions');
    request.flush({ message: 'Forbidden' }, { status: 403, statusText: 'Forbidden' });

    expect(authService.restoreSession).not.toHaveBeenCalled();
    expect(authService.clearSession).not.toHaveBeenCalled();
    expect(router.navigate).not.toHaveBeenCalled();
  });
});
