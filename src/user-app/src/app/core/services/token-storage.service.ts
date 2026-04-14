import { Injectable, signal, computed } from '@angular/core';

const TOKEN_KEY = 'auth_token';

@Injectable({ providedIn: 'root' })
export class TokenStorageService {
  private readonly _token = signal<string | null>(this.readFromStorage());

  readonly token = computed(() => this._token());
  readonly hasToken = computed(() => !!this._token());

  getToken(): string | null {
    return this._token();
  }

  /**
   * Store the auth token.
   * @param persistent  true → localStorage (survives browser restart)
   *                    false → sessionStorage (cleared on browser close)
   */
  setToken(token: string, persistent = true): void {
    // Clear any previous token from the other storage
    localStorage.removeItem(TOKEN_KEY);
    sessionStorage.removeItem(TOKEN_KEY);

    if (persistent) {
      localStorage.setItem(TOKEN_KEY, token);
    } else {
      sessionStorage.setItem(TOKEN_KEY, token);
    }
    this._token.set(token);
  }

  clearToken(): void {
    localStorage.removeItem(TOKEN_KEY);
    sessionStorage.removeItem(TOKEN_KEY);
    this._token.set(null);
  }

  /** Check localStorage first (persistent), then sessionStorage (session-only). */
  private readFromStorage(): string | null {
    return localStorage.getItem(TOKEN_KEY) ?? sessionStorage.getItem(TOKEN_KEY);
  }
}
