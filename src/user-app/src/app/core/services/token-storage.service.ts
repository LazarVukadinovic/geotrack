import { Injectable, computed, signal } from '@angular/core';

const ACCESS_KEY = 'auth_access_token';
const LEGACY_REFRESH_KEY = 'auth_refresh_token';
const SESSION_HINT_KEY = 'auth_session_hint';

type SessionHint = 'persistent' | 'session';

@Injectable({ providedIn: 'root' })
export class TokenStorageService {
  private readonly _accessToken = signal<string | null>(null);
  private readonly _sessionHint = signal<SessionHint | null>(this.readSessionHintFromStorage());

  readonly hasSession = computed(() => !!this._sessionHint());

  constructor() {
    if (typeof window !== 'undefined') {
      window.addEventListener('storage', (event) => {
        if (event.key === SESSION_HINT_KEY || event.key === LEGACY_REFRESH_KEY) {
          this._sessionHint.set(this.readSessionHintFromStorage());
        }
      });
    }
  }

  getAccessToken(): string | null {
    return this._accessToken();
  }

  setAccessToken(accessToken: string): void {
    this._accessToken.set(accessToken);
  }

  setSession(accessToken: string, rememberMe: boolean): void {
    this._accessToken.set(accessToken);
    this.persistSessionHint(rememberMe);
    this.clearLegacyRefreshToken();
  }

  hasLegacyRefreshToken(): boolean {
    return !!this.getLegacyRefreshToken();
  }

  getLegacyRefreshToken(): string | null {
    return localStorage.getItem(LEGACY_REFRESH_KEY) ?? sessionStorage.getItem(LEGACY_REFRESH_KEY);
  }

  isPersistentSession(): boolean {
    return this._sessionHint() === 'persistent';
  }

  finalizeSessionMigration(): void {
    if (!this._sessionHint()) {
      const legacyHint = this.readLegacySessionHint();
      if (legacyHint) {
        this.persistSessionHint(legacyHint === 'persistent');
      }
    }

    this.clearLegacyRefreshToken();
  }

  clearAll(): void {
    this._accessToken.set(null);
    this._sessionHint.set(null);

    localStorage.removeItem(SESSION_HINT_KEY);
    sessionStorage.removeItem(SESSION_HINT_KEY);
    this.clearLegacyRefreshToken();

    localStorage.removeItem(ACCESS_KEY);
    sessionStorage.removeItem(ACCESS_KEY);
    localStorage.removeItem('auth_token');
    sessionStorage.removeItem('auth_token');
  }

  isAccessTokenNearExpiry(bufferSeconds = 120): boolean {
    const token = this._accessToken();
    if (!token) return true;

    const exp = parseJwtExp(token);
    if (exp === null) return true;

    return exp * 1000 - Date.now() < bufferSeconds * 1000;
  }

  private persistSessionHint(persistent: boolean): void {
    const value: SessionHint = persistent ? 'persistent' : 'session';

    localStorage.removeItem(SESSION_HINT_KEY);
    sessionStorage.removeItem(SESSION_HINT_KEY);

    if (persistent) {
      localStorage.setItem(SESSION_HINT_KEY, value);
    } else {
      sessionStorage.setItem(SESSION_HINT_KEY, value);
    }

    this._sessionHint.set(value);
  }

  private readSessionHintFromStorage(): SessionHint | null {
    const stored = localStorage.getItem(SESSION_HINT_KEY) ?? sessionStorage.getItem(SESSION_HINT_KEY);
    if (stored === 'persistent' || stored === 'session') {
      return stored;
    }

    return this.readLegacySessionHint();
  }

  private readLegacySessionHint(): SessionHint | null {
    if (localStorage.getItem(LEGACY_REFRESH_KEY)) return 'persistent';
    if (sessionStorage.getItem(LEGACY_REFRESH_KEY)) return 'session';
    return null;
  }

  private clearLegacyRefreshToken(): void {
    localStorage.removeItem(LEGACY_REFRESH_KEY);
    sessionStorage.removeItem(LEGACY_REFRESH_KEY);
  }
}

function parseJwtExp(token: string): number | null {
  try {
    const payload = token.split('.')[1];
    if (!payload) return null;

    const json = atob(payload.replace(/-/g, '+').replace(/_/g, '/'));
    const parsed = JSON.parse(json) as { exp?: number };
    return parsed.exp ?? null;
  } catch {
    return null;
  }
}
