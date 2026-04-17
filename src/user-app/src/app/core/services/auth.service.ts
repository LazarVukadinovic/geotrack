import { Injectable, computed, inject } from '@angular/core';
import { HttpBackend, HttpClient } from '@angular/common/http';
import { TokenStorageService } from './token-storage.service';

/**
 * Public auth API used throughout the app.
 *
 * isLoggedIn is derived from the presence of a refresh token in storage.
 * This means it remains true while a silent refresh is in-flight — preventing
 * premature "logged out" flicker on the AppShell's auth-guard effect.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly tokenStorage = inject(TokenStorageService);
  // Use HttpBackend to skip interceptors; logout must work even when the
  // access token is already expired.
  private readonly http = new HttpClient(inject(HttpBackend));

  /** True while a refresh token exists in storage (session is restorable). */
  readonly isLoggedIn = computed(() => this.tokenStorage.hasRefreshToken());

  /**
   * Store both tokens after a successful login or registration.
   * @param accessToken  Short-lived JWT (in-memory only).
   * @param refreshToken Opaque long-lived token (persisted per rememberMe).
   * @param rememberMe   true → localStorage; false → sessionStorage.
   */
  login(accessToken: string, refreshToken: string, rememberMe = true): void {
    this.tokenStorage.setTokens(accessToken, refreshToken, rememberMe);
  }

  /**
   * Revoke the refresh token on the backend (best-effort fire-and-forget),
   * then immediately clear all local session state.
   *
   * Local state is cleared synchronously so the UI reacts instantly.
   * The backend call happens in the background; if it fails, the token
   * will simply expire naturally after 7 days.
   */
  logout(): void {
    const refreshToken = this.tokenStorage.getRefreshToken();
    this.tokenStorage.clearAll();

    if (refreshToken) {
      this.http
        .post('/api/auth/logout', { refreshToken })
        .subscribe({ error: () => {} }); // silent — local state already cleared
    }
  }

  /**
   * Clear local session state without calling the backend.
   * Used when a refresh attempt failed (the token is already invalid server-side).
   */
  clearSession(): void {
    this.tokenStorage.clearAll();
  }
}
