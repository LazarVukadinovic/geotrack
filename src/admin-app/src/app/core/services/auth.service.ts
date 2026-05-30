import { Injectable, Optional, computed, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable, Subject, catchError, defer, finalize, map, of, shareReplay, switchMap, take, tap, throwError } from 'rxjs';
import { AdminRoleCode, User } from '../models/user.model';
import {
  AdminView,
  getAdminViewByCode,
  getAdminViewsForRoles,
  normalizeAdminRoleCode,
  resolveAdminViewForRoles,
} from '../models/admin-view.model';
import { AdminNotificationsService } from './administrator/admin-notifications.service';
import { RegionalManagerSidebarStateService } from './regional-manager-sidebar-state.service';
import { TerritoryDashboardService } from './territory-dashboard.service';

export type AdminAuthSessionEvent = 'login-start' | 'login-success' | 'session-cleared';

export class AdminAccessDeniedError extends Error {
  constructor(message = 'This account does not have access to the administration portal.') {
    super(message);
    this.name = 'AdminAccessDeniedError';
  }
}

interface AdminAuthUserResponse {
  id: number;
  email: string;
  firstName?: string;
  lastName?: string;
  displayName: string;
  avatarUrl?: string | null;
  avatarVersion?: number | null;
  roles: string[];
  canAccessRegionalAdmin: boolean;
  canAccessTerritoryAdmin?: boolean;
  canAccessContentModeration?: boolean;
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
  private static readonly activeViewStorageKey = 'geotrack_admin_active_view';

  private accessToken: string | null = null;
  private accessTokenExpiresAtMs: number | null = null;
  private refreshRequest$: Observable<User | null> | null = null;
  private readonly _currentUser = signal<User | null>(null);
  private readonly _adminAccessDenied = signal(false);
  private readonly sessionEventsSubject = new Subject<AdminAuthSessionEvent>();

  readonly currentUser = computed(() => this._currentUser());
  readonly adminAccessDenied = computed(() => this._adminAccessDenied());
  readonly isLoggedIn = computed(() => this._currentUser() !== null);
  readonly isAdministrator = computed(() => this._currentUser()?.activeRoleCode === 'Admin');
  readonly isContentModerator = computed(() => this._currentUser()?.activeRoleCode === 'ContentModerator');
  readonly isTerritoryManager = computed(() => this._currentUser()?.activeRoleCode === 'TerritoryManager');
  readonly isTerritoryAssociate = computed(() => this._currentUser()?.activeRoleCode === 'TerritoryAssociate');
  readonly isNationalManager = computed(() => this._currentUser()?.activeRoleCode === 'TerritoryManager');
  readonly canAccessTerritoryAdmin = computed(() =>
    this._currentUser()?.activeRoleCode === 'TerritoryManager' ||
    this._currentUser()?.activeRoleCode === 'TerritoryAssociate');
  readonly isRegionalManager = computed(() => this._currentUser()?.activeRoleCode === 'RegionalManager');
  readonly availableAdminViews = computed(() => getAdminViewsForRoles(this._currentUser()?.roles ?? []));
  readonly activeAdminView = computed(() => getAdminViewByCode(this._currentUser()?.activeRoleCode));
  readonly sessionEvents$ = this.sessionEventsSubject.asObservable();

  constructor(
    @Optional() private readonly http: HttpClient | null,
    @Optional() private readonly territoryDashboard: TerritoryDashboardService | null,
    @Optional() private readonly regionalSidebarState: RegionalManagerSidebarStateService | null,
    @Optional() private readonly notifications: AdminNotificationsService | null,
  ) {}

  getUser(): User | null {
    return this._currentUser();
  }

  getAccessToken(): string | null {
    return this.accessToken;
  }

  getAvailableAdminViews(): AdminView[] {
    return this.availableAdminViews();
  }

  getActiveAdminView(): AdminView | null {
    return this.activeAdminView();
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
    return defer(() => {
      this.beginLoginSession();

      return this.httpClient
        .post<LoginResponse>(
          '/api/admin/auth/login',
          { email, password, rememberMe },
          { withCredentials: true },
        )
        .pipe(
          tap(response => this.setToken(response.accessToken, response.expiresAt, response.expiresIn)),
          switchMap(response =>
            this.httpClient.get<AdminAuthUserResponse>('/api/admin/auth/me').pipe(
              map(userResponse => ({
                user: this.mapAdminUser(userResponse, false),
                sessionPersistent: response.sessionPersistent,
              })),
            ),
          ),
          switchMap(({ user, sessionPersistent }) =>
            this.primeAdminContext(user).pipe(map(() => ({ user, sessionPersistent }))),
          ),
          tap(({ user, sessionPersistent }) => {
            this._currentUser.set(user);
            this._adminAccessDenied.set(false);
            this.setSessionHint(sessionPersistent);
            this.sessionEventsSubject.next('login-success');
          }),
          map(({ user }) => user),
          catchError(error => {
            this.clearLocalSession();
            const safeError = this.toSafeAuthError(error);
            if (safeError instanceof AdminAccessDeniedError) {
              this._adminAccessDenied.set(true);
            }

            return throwError(() => safeError);
          }),
        );
    });
  }

  restoreSession(options: { forceRefresh?: boolean } = {}): Observable<User | null> {
    if (this._currentUser() && !options.forceRefresh) {
      return of(this._currentUser());
    }

    if (this.refreshRequest$) {
      return this.refreshRequest$;
    }

    this.refreshRequest$ = this.httpClient
      .post<LoginResponse>('/api/admin/auth/refresh', {}, { withCredentials: true })
      .pipe(
        tap(response => this.setToken(response.accessToken, response.expiresAt, response.expiresIn)),
        map(response => ({
          user: this.mapAdminUser(response.user),
          sessionPersistent: response.sessionPersistent,
        })),
        tap(({ user }) => this._currentUser.set(user)),
        tap(() => this._adminAccessDenied.set(false)),
        tap(({ sessionPersistent }) => this.setSessionHint(sessionPersistent)),
        map(({ user }) => user as User | null),
        catchError(error => {
          if (error instanceof HttpErrorResponse && error.status === 403) {
            this.markAdminAccessDenied();
            return of(null);
          }

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

  switchAdminView(roleCode: AdminRoleCode): boolean {
    const current = this._currentUser();
    const requestedView = getAdminViewByCode(roleCode);
    if (!current || !requestedView) {
      return false;
    }

    const allowedViews = getAdminViewsForRoles(current.roles);
    if (!allowedViews.some(view => view.roleCode === requestedView.roleCode)) {
      return false;
    }

    this._currentUser.set({
      ...current,
      activeRoleCode: requestedView.roleCode,
      role: requestedView.label,
    });
    this.persistActiveViewCode(requestedView.roleCode);
    return true;
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
    return this.getActiveAdminView()?.dashboardRoute ?? '/login';
  }

  loadCurrentUser(): Observable<User> {
    return this.httpClient.get<AdminAuthUserResponse>('/api/admin/auth/me').pipe(
      map(response => this.mapAdminUser(response)),
      tap(user => this._currentUser.set(user)),
    );
  }

  private mapAdminUser(response: AdminAuthUserResponse, preferStoredView = true): User {
    const roleCodes = this.resolveAdminRoleCodes(response.roles);
    const preferredRoleCode = preferStoredView ? this.readActiveViewCode() : null;
    const activeView = resolveAdminViewForRoles(roleCodes, preferredRoleCode);
    if (!activeView) {
      throw new Error('This account is not assigned to an admin role.');
    }

    this.persistActiveViewCode(activeView.roleCode);

    const displayName = response.displayName?.trim() || response.email;
    const nameParts = displayName.split(/\s+/).filter(Boolean);
    const firstName = response.firstName?.trim() || nameParts[0] || displayName;
    const lastName = response.lastName?.trim() || nameParts.slice(1).join(' ');

    return {
      id: String(response.id),
      firstName,
      lastName,
      email: response.email,
      role: activeView.label,
      activeRoleCode: activeView.roleCode,
      roles: roleCodes,
      region: '',
      memberSince: '',
      bio: '',
      language: navigator.language || 'en-US',
      timezone: Intl.DateTimeFormat().resolvedOptions().timeZone || '',
      notifyRequests: true,
      notifyNewReview: true,
      avatarUrl: response.avatarUrl ?? undefined,
      avatarVersion: response.avatarVersion ?? null,
    };
  }

  private get httpClient(): HttpClient {
    if (!this.http) {
      throw new Error('HttpClient is required for admin authentication.');
    }

    return this.http;
  }

  private resolveAdminRoleCodes(roles: string[]): AdminRoleCode[] {
    const roleCodes = getAdminViewsForRoles(roles).map(view => view.roleCode);
    if (roleCodes.length === 0) {
      throw new Error('This account is not assigned to an admin role.');
    }

    return roleCodes;
  }

  private setToken(token: string, expiresAt?: string | null, expiresIn?: number | null): void {
    this.accessToken = token;
    this.accessTokenExpiresAtMs = this.resolveExpiryMs(expiresAt, expiresIn);
  }

  private beginLoginSession(): void {
    this.accessToken = null;
    this.accessTokenExpiresAtMs = null;
    this._currentUser.set(null);
    this._adminAccessDenied.set(false);
    this.refreshRequest$ = null;
    this.clearSessionStorage();
    this.resetSessionScopedServices();
    this.sessionEventsSubject.next('login-start');
  }

  private primeAdminContext(user: User): Observable<unknown> {
    if (!this.territoryDashboard || !this.canUseTerritoryContext(user)) {
      return of(null);
    }

    return this.territoryDashboard.getAssignedTerritories().pipe(
      take(1),
      tap(territories => {
        const activeTerritoryId = territories[0]?.id ?? null;
        this.territoryDashboard?.setActiveTerritoryId(activeTerritoryId);
      }),
      map(() => null),
    );
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

  private toSafeAuthError(error: unknown): Error {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 403) {
        return new AdminAccessDeniedError(
          error.error?.message ?? 'This account does not have access to the administration portal.',
        );
      }

      if (error.status === 400) {
        return new Error(error.error?.message ?? 'Email and password are required.');
      }

      return new Error('Invalid email or password.');
    }

    return error instanceof Error ? error : new Error('Invalid email or password.');
  }

  private setSessionHint(sessionPersistent: boolean): void {
    try {
      localStorage.removeItem(AuthService.sessionHintKey);
      sessionStorage.removeItem(AuthService.sessionHintKey);

      if (sessionPersistent) {
        localStorage.setItem(AuthService.sessionHintKey, 'persistent');
      } else {
        sessionStorage.setItem(AuthService.sessionHintKey, 'session');
      }
    } catch {
      // Session restore is a convenience only.
    }
  }

  private clearLocalSession(): void {
    this.accessToken = null;
    this.accessTokenExpiresAtMs = null;
    this._currentUser.set(null);
    this._adminAccessDenied.set(false);
    this.refreshRequest$ = null;
    this.clearSessionStorage();
    this.resetSessionScopedServices();
    this.sessionEventsSubject.next('session-cleared');
  }

  private clearSessionStorage(): void {
    try {
      localStorage.removeItem(AuthService.sessionHintKey);
      sessionStorage.removeItem(AuthService.sessionHintKey);
      sessionStorage.removeItem(AuthService.activeRegionStorageKey);
      sessionStorage.removeItem(AuthService.activeTerritoryStorageKey);
      sessionStorage.removeItem(AuthService.activeViewStorageKey);
    } catch {
      // Session restore is a convenience only.
    }
  }

  private resetSessionScopedServices(): void {
    this.territoryDashboard?.clearSessionState();
    this.regionalSidebarState?.clear();
    this.notifications?.reset();
  }

  private canUseTerritoryContext(user: User): boolean {
    return user.activeRoleCode === 'TerritoryManager' || user.activeRoleCode === 'TerritoryAssociate';
  }

  private readActiveViewCode(): AdminRoleCode | null {
    try {
      return normalizeAdminRoleCode(sessionStorage.getItem(AuthService.activeViewStorageKey));
    } catch {
      return null;
    }
  }

  private persistActiveViewCode(roleCode: AdminRoleCode): void {
    try {
      sessionStorage.setItem(AuthService.activeViewStorageKey, roleCode);
    } catch {
      // Active view persistence is only a session convenience.
    }
  }

  private markAdminAccessDenied(): void {
    this.clearLocalSession();
    this._adminAccessDenied.set(true);
  }
}
