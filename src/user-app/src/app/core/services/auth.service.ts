import { Injectable, computed, inject } from '@angular/core';
import { HttpBackend, HttpClient } from '@angular/common/http';
import { AnalyticsService } from './analytics.service';
import { DestinationService } from './destination.service';
import { LocationService } from './location.service';
import { TokenStorageService } from './token-storage.service';
import { resolveApiUrl } from './api-url';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly tokenStorage = inject(TokenStorageService);
  private readonly locationService = inject(LocationService);
  private readonly destinationService = inject(DestinationService);
  private readonly analytics = inject(AnalyticsService);
  private readonly http = new HttpClient(inject(HttpBackend));

  readonly isLoggedIn = computed(() => this.tokenStorage.hasSession());

  login(accessToken: string, rememberMe = true): void {
    this.tokenStorage.setSession(accessToken, rememberMe);
  }

  logout(): void {
    // Fire the logout event AND flush before tearing down the session, so the
    // event posts with the still-valid JWT and is correctly attributed to the
    // user. clearClientSessionContext() then wipes local state.
    this.analytics.track('logout_completed');
    this.analytics.flushNow();

    this.clearClientSessionContext();

    this.http
      .post(resolveApiUrl('/api/auth/logout'), {}, { withCredentials: true })
      .subscribe({ error: () => {} });
  }

  clearSession(): void {
    this.clearClientSessionContext();
  }

  private clearClientSessionContext(): void {
    this.tokenStorage.clearAll();
    this.locationService.clearSavedLocation();
    this.destinationService.clear();

    try {
      sessionStorage.removeItem('mapLocation');
      sessionStorage.removeItem('mapFilters');
      sessionStorage.removeItem('mapViewState');
      sessionStorage.removeItem('mapOpenedThisSession');
    } catch {
      // Storage may be unavailable; ignore.
    }
  }
}
