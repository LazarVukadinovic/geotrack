import { Injectable, computed, signal } from '@angular/core';

/**
 * Frontend-only auth state for development.
 *
 * BACKEND WIRING: When real auth is ready, replace `_isLoggedIn` signal with
 * a derived signal from an HTTP session/token store, e.g.:
 *   private readonly _isLoggedIn = computed(() => !!this.sessionService.user());
 *
 * Nothing outside this service needs to change — consumers read `isLoggedIn`
 * and call `login()` / `logout()` which you swap for real API calls.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly _isLoggedIn = signal<boolean>(false);

  readonly isLoggedIn = computed(() => this._isLoggedIn());

  login(): void {
    this._isLoggedIn.set(true);
  }

  logout(): void {
    this._isLoggedIn.set(false);
  }
}
