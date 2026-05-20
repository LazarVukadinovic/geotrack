import { Injectable, Optional, computed, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable, catchError, finalize, map, of, shareReplay, tap, throwError } from 'rxjs';
import { User, UserRole } from '../models/user.model';

interface AdminAuthUserResponse {
  id: number;
  email: string;
  firstName?: string;
  lastName?: string;
  displayName: string;
  avatarUrl?: string | null;
  roles: string[];
  canAccessRegionalAdmin: boolean;
  canAccessTerritoryAdmin?: boolean;
}

interface LoginResponse {
  accessToken: string;
  expiresAt: string;
  expiresIn: number;
  sessionPersistent: boolean;
  user: AdminAuthUserResponse;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private static readonly sessionHintKey = 'geotrack_admin_session';
  private static readonly activeRegionStorageKey = 'geotrack_admin_active_region_id';
  private static readonly activeTerritoryStorageKey = 'geotrack_admin_active_territory_id';

  private accessToken: string | null = null;
  private accessTokenExpiresAtMs: number | null = null;
  private refreshRequest$: Observable<User | null> | null = null;
  private readonly _currentUser = signal<User | null>(null);

  readonly currentUser = computed(() => this._currentUser());
  readonly isLoggedIn = computed(() => this._currentUser() !== null);
  readonly isAdministrator = computed(() => this._currentUser()?.role === 'Administrator');
  readonly isTerritoryManager = computed(() => this._currentUser()?.role === 'Territory Manager');
  readonly isTerritoryAssociate = computed(() => this._currentUser()?.role === 'Territory Associate');
  readonly isNationalManager = computed(() => this._currentUser()?.role === 'Territory Manager');
  readonly canAccessTerritoryAdmin = computed(() =>
    this._currentUser()?.role === 'Territory Manager' ||
    this._currentUser()?.role === 'Territory Associate');
  readonly isRegionalManager = computed(() => this._currentUser()?.role === 'Regional Manager');

  constructor(@Optional() private readonly http: HttpClient | null) {}

  getUser(): User | null {
    return this._currentUser();
  }

  getAccessToken(): string | null {
    return this.accessToken;
  }

  hasValidAccessToken(): boolean {
    if (!this.accessToken) return false;
    if (!this.accessTokenExpiresAtMs) return true;
    return Date.now() < this.accessTokenExpiresAtMs;
  }

  get fullName(): string {
    const user = this._currentUser();
    return user ? `${user.firstName} ${user.lastName}`.trim() : '';
  }

  login(email: string, password: string, rememberMe = false): Observable<User> {
    return this.httpClient
      .post<LoginResponse>(
        '/api/admin/auth/login',
        { email, password, rememberMe },
        { withCredentials: true },
      )
      .pipe(
        tap(response => this.setToken(response.accessToken, response.expiresAt, response.expiresIn)),
        map(response => this.mapAdminUser(response.user)),
        tap(user => {
          this._currentUser.set(user);
          this.setSessionHint();
        }),
        catchError(error => {
          this.clearLocalSession();
          return throwError(() => this.toSafeAuthError(error));
        }),
      );
  }

  restoreSession(): Observable<User | null> {
    if (this._currentUser()) {
      return of(this._currentUser());
    }

    if (!this.hasSessionHint()) {
      return of(null);
    }

    if (this.refreshRequest$) {
      return this.refreshRequest$;
    }

    this.refreshRequest$ = this.httpClient
      .post<LoginResponse>('/api/admin/auth/refresh', {}, { withCredentials: true })
      .pipe(
        tap(response => this.setToken(response.accessToken, response.expiresAt, response.expiresIn)),
        map(response => this.mapAdminUser(response.user)),
        tap(user => this._currentUser.set(user)),
        map(user => user as User | null),
        catchError(() => {
          this.clearLocalSession();
          return of(null);
        }),
        finalize(() => {
          this.refreshRequest$ = null;
        }),
        shareReplay(1),
      );

    return this.refreshRequest$;
  }

  updateUser(data: Partial<User>): Observable<User> {
    const current = this._currentUser();
    if (!current) {
      return throwError(() => new Error('Not authenticated'));
    }

    const updated: User = { ...current, ...data };
    this._currentUser.set(updated);
    return of(updated);
  }

  logout(): void {
    this.httpClient.post('/api/admin/auth/logout', {}, { withCredentials: true }).subscribe({
      error: () => undefined,
    });
    this.clearLocalSession();
  }

  clearSession(): void {
    this.clearLocalSession();
  }

  getHomeRoute(): string {
    const role: UserRole | undefined = this._currentUser()?.role;
    switch (role) {
      case 'Administrator':
        return '/admin-dashboard';
      case 'Territory Manager':
      case 'Territory Associate':
        return '/territory/overview';
      case 'Regional Manager':
        return '/dashboard';
      default:
        return '/login';
    }
  }

  loadCurrentUser(): Observable<User> {
    return this.httpClient.get<AdminAuthUserResponse>('/api/admin/auth/me').pipe(
      map(response => this.mapAdminUser(response)),
      tap(user => this._currentUser.set(user)),
    );
  }

  private mapAdminUser(response: AdminAuthUserResponse): User {
    const role = this.resolveAdminRole(response.roles);
    const displayName = response.displayName?.trim() || response.email;
    const nameParts = displayName.split(/\s+/).filter(Boolean);
    const firstName = response.firstName?.trim() || nameParts[0] || displayName;
    const lastName = response.lastName?.trim() || nameParts.slice(1).join(' ');

    return {
      id: String(response.id),
      firstName,
      lastName,
      email: response.email,
      role,
      region: '',
      memberSince: '',
      bio: '',
      language: navigator.language || 'en-US',
      timezone: Intl.DateTimeFormat().resolvedOptions().timeZone || '',
      notifyRequests: true,
      notifyNewReview: true,
      avatarUrl: response.avatarUrl ?? undefined,
    };
  }

  private get httpClient(): HttpClient {
    if (!this.http) {
      throw new Error('HttpClient is required for admin authentication.');
    }

    return this.http;
  }

  private resolveAdminRole(roles: string[]): UserRole {
    if (roles.some(role => ['admin', 'administrator', 'superadmin', 'sysadmin'].includes(this.normalizeRole(role)))) {
      return 'Administrator';
    }

    if (roles.some(role => ['territorymanager', 'nationalmanager'].includes(this.normalizeRole(role)))) {
      return 'Territory Manager';
    }

    if (roles.some(role => this.normalizeRole(role) === 'territoryassociate')) {
      return 'Territory Associate';
    }

    if (roles.some(role => this.normalizeRole(role) === 'regionalmanager')) {
      return 'Regional Manager';
    }

    throw new Error('This account is not assigned to an admin role.');
  }

  private setToken(token: string, expiresAt?: string | null, expiresIn?: number | null): void {
    this.accessToken = token;
    this.accessTokenExpiresAtMs = this.resolveExpiryMs(expiresAt, expiresIn);
  }

  private resolveExpiryMs(expiresAt?: string | null, expiresIn?: number | null): number | null {
    const parsed = expiresAt ? Date.parse(expiresAt) : NaN;
    if (Number.isFinite(parsed)) {
      return parsed;
    }

    if (typeof expiresIn === 'number' && Number.isFinite(expiresIn) && expiresIn > 0) {
      return Date.now() + expiresIn * 1000;
    }

    return null;
  }

  private normalizeRole(role: string): string {
    return role.replace(/[\s_-]/g, '').toLowerCase();
  }

  private toSafeAuthError(error: unknown): Error {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 403) {
        return new Error(error.error?.message ?? 'You do not have access to the admin app.');
      }

      if (error.status === 400) {
        return new Error(error.error?.message ?? 'Email and password are required.');
      }

      return new Error('Invalid email or password.');
    }

    return error instanceof Error ? error : new Error('Invalid email or password.');
  }

  private setSessionHint(): void {
    try {
      sessionStorage.setItem(AuthService.sessionHintKey, '1');
    } catch {
      // Session restore is a convenience only.
    }
  }

  private hasSessionHint(): boolean {
    try {
      return sessionStorage.getItem(AuthService.sessionHintKey) === '1';
    } catch {
      return false;
    }
  }

  private clearLocalSession(): void {
    this.accessToken = null;
    this.accessTokenExpiresAtMs = null;
    this._currentUser.set(null);
    try {
      sessionStorage.removeItem(AuthService.sessionHintKey);
      sessionStorage.removeItem(AuthService.activeRegionStorageKey);
      sessionStorage.removeItem(AuthService.activeTerritoryStorageKey);
    } catch {
      // Session restore is a convenience only.
    }
  }
}
