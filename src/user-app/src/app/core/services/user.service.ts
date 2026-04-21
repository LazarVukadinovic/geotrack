import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { finalize, shareReplay, tap } from 'rxjs/operators';
import { AuthService } from './auth.service';

export interface UserProfile {
  id: number;
  username: string;
  email: string;
  firstName: string;
  lastName: string;
  phoneNumber?: string;
  avatarUrl?: string;
  location?: string;
  needsInterestsOnboarding: boolean;
}

/**
 * Single source of truth for the current authenticated user's profile data.
 *
 * Populated by provideAppInitializer on every cold start (before any route renders),
 * so consumers can read fullName/email synchronously from signals without waiting
 * for an additional HTTP round-trip.
 *
 * Clears automatically when the user logs out (isLoggedIn becomes false).
 */
@Injectable({ providedIn: 'root' })
export class UserService {
  private readonly http        = inject(HttpClient);
  private readonly authService = inject(AuthService);

  private readonly _profile = signal<UserProfile | null>(null);
  private loadInFlight$: Observable<UserProfile> | null = null;
  readonly profile = computed(() => this._profile());
  readonly needsInterestsOnboarding = computed(() => this._profile()?.needsInterestsOnboarding ?? false);
  readonly avatarUrl = computed(() => this._profile()?.avatarUrl ?? '');

  /** Display name: "First Last", falling back to email if names are blank. */
  readonly fullName = computed(() => {
    const p = this._profile();
    if (!p) return '';
    return `${p.firstName} ${p.lastName}`.trim() || p.email;
  });

  readonly email = computed(() => this._profile()?.email ?? '');

  constructor() {
    // When the user logs out, clear the cached profile so stale data
    // is never shown if a different account logs in during the same session.
    effect(() => {
      if (!this.authService.isLoggedIn()) {
        this._profile.set(null);
      }
    });
  }

  /**
   * Fetch the current user from the backend and store the result in the signal.
   * Called by provideAppInitializer on startup; components do not need to call it.
   */
  load(): Observable<UserProfile> {
    return this.http.get<UserProfile>('/api/user/me').pipe(
      tap(profile => this._profile.set(profile))
    );
  }

  patchProfile(patch: Partial<UserProfile>): void {
    const existingProfile = this._profile();
    if (!existingProfile) return;

    this._profile.set({
      ...existingProfile,
      ...patch,
    });
  }

  /**
   * Guard-friendly current-user hydration.
   * Reuses the cached profile when present and de-duplicates concurrent loads.
   */
  ensureLoaded(): Observable<UserProfile> {
    const existingProfile = this._profile();
    if (existingProfile) {
      return of(existingProfile);
    }

    if (this.loadInFlight$) {
      return this.loadInFlight$;
    }

    this.loadInFlight$ = this.load().pipe(
      finalize(() => {
        this.loadInFlight$ = null;
      }),
      shareReplay(1),
    );

    return this.loadInFlight$;
  }
}
