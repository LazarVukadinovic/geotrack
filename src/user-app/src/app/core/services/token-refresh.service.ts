import { Injectable, inject } from '@angular/core';
import { HttpBackend, HttpClient } from '@angular/common/http';
import { Observable, throwError, share, finalize, tap, map } from 'rxjs';
import { TokenStorageService } from './token-storage.service';

interface RefreshResponse {
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
}

/**
 * Handles the silent token-refresh flow.
 *
 * Key design points:
 *  - Uses HttpBackend directly (bypasses all HTTP interceptors) so the
 *    /api/auth/refresh call never triggers another refresh attempt.
 *  - Deduplicates concurrent refresh calls: if a refresh is already in-flight,
 *    every simultaneous caller receives the same shared Observable — only one
 *    HTTP request is made.
 */
@Injectable({ providedIn: 'root' })
export class TokenRefreshService {
  private readonly tokenStorage = inject(TokenStorageService);
  // Bypass all interceptors to avoid circular refresh loops.
  private readonly http = new HttpClient(inject(HttpBackend));

  private refreshInFlight$: Observable<string> | null = null;

  /**
   * Exchange the stored refresh token for a new access + refresh token pair.
   * Emits the new access token string, then completes.
   * Concurrent callers share the same in-flight request.
   */
  refresh(): Observable<string> {
    if (this.refreshInFlight$) {
      return this.refreshInFlight$;
    }

    const refreshToken = this.tokenStorage.getRefreshToken();
    if (!refreshToken) {
      return throwError(() => new Error('No refresh token available'));
    }

    this.refreshInFlight$ = this.http
      .post<RefreshResponse>('/api/auth/refresh', { refreshToken })
      .pipe(
        tap(response => {
          // Persist the rotated refresh token + store the new access token.
          this.tokenStorage.setRefreshToken(response.refreshToken);
          this.tokenStorage.setAccessToken(response.accessToken);
        }),
        map(response => response.accessToken),
        // share() multicasts: all subscribers waiting on this in-flight call
        // receive the same emission without triggering another HTTP request.
        share(),
        finalize(() => {
          this.refreshInFlight$ = null;
        }),
      );

    return this.refreshInFlight$;
  }
}
