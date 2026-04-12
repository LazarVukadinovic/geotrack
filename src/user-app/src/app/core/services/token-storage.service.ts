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

  setToken(token: string): void {
    localStorage.setItem(TOKEN_KEY, token);
    this._token.set(token);
  }

  clearToken(): void {
    localStorage.removeItem(TOKEN_KEY);
    this._token.set(null);
  }

  private readFromStorage(): string | null {
    return localStorage.getItem(TOKEN_KEY);
  }
}
