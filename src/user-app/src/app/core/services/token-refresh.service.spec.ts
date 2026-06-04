import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { TokenRefreshService } from './token-refresh.service';
import { TokenStorageService } from './token-storage.service';

describe('TokenRefreshService', () => {
  let service: TokenRefreshService;
  let http: HttpTestingController;
  let tokenStorage: TokenStorageService;

  beforeEach(() => {
    sessionStorage.clear();
    localStorage.clear();

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        TokenRefreshService,
        TokenStorageService,
      ],
    });

    service = TestBed.inject(TokenRefreshService);
    http = TestBed.inject(HttpTestingController);
    tokenStorage = TestBed.inject(TokenStorageService);
  });

  afterEach(() => {
    http.verify();
    sessionStorage.clear();
    localStorage.clear();
  });

  it('sends refresh with credentials and stores the new access token', () => {
    let refreshedToken = '';

    service.refresh({ allowMissingSessionHint: true }).subscribe(token => {
      refreshedToken = token;
    });

    const request = http.expectOne('/api/auth/refresh');
    expect(request.request.method).toBe('POST');
    expect(request.request.withCredentials).toBe(true);
    request.flush({ accessToken: 'fresh-token', expiresIn: 3600, sessionPersistent: false });

    expect(refreshedToken).toBe('fresh-token');
    expect(tokenStorage.getAccessToken()).toBe('fresh-token');
    expect(sessionStorage.getItem('auth_session_hint')).toBe('session');
  });

  it('coalesces simultaneous refresh calls into one HTTP request', () => {
    const tokens: string[] = [];

    service.refresh({ allowMissingSessionHint: true }).subscribe(token => tokens.push(token));
    service.refresh({ allowMissingSessionHint: true }).subscribe(token => tokens.push(token));

    const request = http.expectOne('/api/auth/refresh');
    request.flush({ accessToken: 'shared-token', expiresIn: 3600, sessionPersistent: true });

    expect(tokens).toEqual(['shared-token', 'shared-token']);
    expect(localStorage.getItem('auth_session_hint')).toBe('persistent');
  });

  it('fails fast when no session hint is present and missing hints are not allowed', () => {
    let message = '';

    service.refresh().subscribe({
      error: error => {
        message = error.message;
      },
    });

    http.expectNone('/api/auth/refresh');
    expect(message).toBe('No refresh token available');
  });
});
