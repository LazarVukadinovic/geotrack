import { Injectable, signal, computed } from '@angular/core';

const ACCESS_KEY  = 'auth_access_token';   // kept only for legacy cleanup
const REFRESH_KEY = 'auth_refresh_token';

/**
 * Single source of truth for token storage.
 *
 * Access token  — in-memory signal only (never persisted; obtained fresh via
 *                 refresh on every cold start).
 * Refresh token — localStorage  (Remember Me = on,  survives browser restart)
 *                 sessionStorage (Remember Me = off, cleared on tab close)
 *
 * isLoggedIn is true whenever a refresh token is present in storage, which
 * means the session may still be restorable.  The access token is a transient
 * credential that gets silently refreshed before it expires.
 */
@Injectable({ providedIn: 'root' })
export class TokenStorageService {
  // Access token is always in-memory; never touches disk.
  private readonly _accessToken = signal<string | null>(null);

  // Refresh token is read from whichever storage held it at startup.
  private readonly _refreshToken = signal<string | null>(this.readRefreshFromStorage());

  readonly hasRefreshToken = computed(() => !!this._refreshToken());

  constructor() {
    // Cross-tab sync: when another tab logs out (or rotates its refresh
    // token), the `storage` event fires here with the same key.  Mirror the
    // new value into our in-memory signal so `hasRefreshToken` — and every
    // computed derived from it, including AuthService.isLoggedIn — flips in
    // lock-step.  Same-tab writes do NOT fire this event, so there is no
    // feedback loop.
    if (typeof window !== 'undefined') {
      window.addEventListener('storage', (e) => {
        if (e.key === REFRESH_KEY) {
          this._refreshToken.set(e.newValue);
        }
      });
    }
  }

  getAccessToken(): string | null {
    return this._accessToken();
  }

  getRefreshToken(): string | null {
    return this._refreshToken();
  }

  /**
   * Called after a successful login or token refresh.
   * @param accessToken  Short-lived JWT (15 min) — kept only in memory.
   * @param refreshToken Long-lived opaque token — persisted per rememberMe flag.
   * @param rememberMe   true → localStorage; false → sessionStorage.
   */
  setTokens(accessToken: string, refreshToken: string, rememberMe: boolean): void {
    this._accessToken.set(accessToken);
    this.persistRefreshToken(refreshToken, rememberMe);
    this._refreshToken.set(refreshToken);
  }

  /**
   * Update only the access token (e.g. after a silent refresh where we reuse
   * the same refresh token but get a new access token).
   * In the rotation model the caller also provides a new refresh token, so
   * prefer setTokens(); this helper is here for completeness.
   */
  setAccessToken(accessToken: string): void {
    this._accessToken.set(accessToken);
  }

  /**
   * Update the stored refresh token (called after each successful rotation).
   * Keeps whichever storage location the original token was written to.
   */
  setRefreshToken(refreshToken: string): void {
    // Maintain the same persistence level that was chosen at login.
    const inLocal = !!localStorage.getItem(REFRESH_KEY);
    this.persistRefreshToken(refreshToken, inLocal);
    this._refreshToken.set(refreshToken);
  }

  clearAll(): void {
    this._accessToken.set(null);
    this._refreshToken.set(null);
    localStorage.removeItem(REFRESH_KEY);
    sessionStorage.removeItem(REFRESH_KEY);
    // Remove legacy single-token key if present.
    localStorage.removeItem(ACCESS_KEY);
    sessionStorage.removeItem(ACCESS_KEY);
    localStorage.removeItem('auth_token');
    sessionStorage.removeItem('auth_token');
  }

  /**
   * Returns true when the access token exists and will expire within
   * `bufferSeconds` seconds (default 2 minutes).  Used by the auth
   * interceptor to decide whether to proactively refresh.
   */
  isAccessTokenNearExpiry(bufferSeconds = 120): boolean {
    const token = this._accessToken();
    if (!token) return true;
    const exp = parseJwtExp(token);
    if (exp === null) return true;
    return exp * 1000 - Date.now() < bufferSeconds * 1000;
  }

  // ── Private ──────────────────────────────────────────────────────────────

  private persistRefreshToken(token: string, persistent: boolean): void {
    localStorage.removeItem(REFRESH_KEY);
    sessionStorage.removeItem(REFRESH_KEY);
    if (persistent) {
      localStorage.setItem(REFRESH_KEY, token);
    } else {
      sessionStorage.setItem(REFRESH_KEY, token);
    }
  }

  private readRefreshFromStorage(): string | null {
    return localStorage.getItem(REFRESH_KEY) ?? sessionStorage.getItem(REFRESH_KEY);
  }
}

// ── JWT helpers ───────────────────────────────────────────────────────────────

/** Safely extracts the `exp` Unix timestamp (seconds) from a JWT payload. */
function parseJwtExp(token: string): number | null {
  try {
    const payload = token.split('.')[1];
    if (!payload) return null;
    // base64url → base64 → JSON
    const json = atob(payload.replace(/-/g, '+').replace(/_/g, '/'));
    const parsed = JSON.parse(json) as { exp?: number };
    return parsed.exp ?? null;
  } catch {
    return null;
  }
}
