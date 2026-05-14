import { Injectable, inject } from '@angular/core';
import { HttpBackend, HttpClient } from '@angular/common/http';
import { Observable, finalize, map, share, tap, throwError } from 'rxjs';
import { TokenStorageService } from './token-storage.service';
import { apiUrl } from './api-url';

interface RefreshResponse {
  accessToken: string;
  expiresIn: number;
  sessionPersistent: boolean;
}

@Injectable({ providedIn: 'root' })
export class TokenRefreshService {
  private readonly tokenStorage = inject(TokenStorageService);
  private readonly http = new HttpClient(inject(HttpBackend));

  private refreshInFlight$: Observable<string> | null = null;

  refresh(): Observable<string> {
    if (this.refreshInFlight$) {
      return this.refreshInFlight$;
    }

    if (!this.tokenStorage.hasSession()) {
      return throwError(() => new Error('No refresh token available'));
    }

    const legacyRefreshToken = this.tokenStorage.getLegacyRefreshToken();

    this.refreshInFlight$ = this.http
      .post<RefreshResponse>(
        apiUrl('/api/auth/refresh'),
        legacyRefreshToken ? { refreshToken: legacyRefreshToken } : {},
        { withCredentials: true },
      )
      .pipe(
        tap((response) => {
          this.tokenStorage.setAccessToken(response.accessToken);
          this.tokenStorage.finalizeSessionMigration();
        }),
        map((response) => response.accessToken),
        share(),
        finalize(() => {
          this.refreshInFlight$ = null;
        }),
      );

    return this.refreshInFlight$;
  }
}
