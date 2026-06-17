import { Injectable } from '@angular/core';
import { BehaviorSubject, EMPTY, Observable, Subject, Subscription, catchError, exhaustMap, finalize, shareReplay, switchMap, tap, throwError } from 'rxjs';
import { TerritoryRegionSetupStatus } from '../models/dashboard.model';
import { User } from '../models/user.model';
import { AuthService } from './auth.service';
import { TerritoryDashboardService } from './territory-dashboard.service';

export interface TerritoryRegionSetupState {
  setupStatus: TerritoryRegionSetupStatus | null;
  setupCheckInProgress: boolean;
  setupCheckedForCurrentSession: boolean;
  modalOpen: boolean;
  syncInProgress: boolean;
  setupCompleted: boolean;
  setupError: string;
}

interface SetupStatusOptions {
  force?: boolean;
  openModalOnRequired?: boolean;
  openModalOnError?: boolean;
  markSessionChecked?: boolean;
}

interface ResolvedSetupStatusOptions {
  force: boolean;
  openModalOnRequired: boolean;
  openModalOnError: boolean;
  markSessionChecked: boolean;
}

@Injectable({ providedIn: 'root' })
export class TerritoryRegionSetupService {
  private readonly initialState: TerritoryRegionSetupState = {
    setupStatus: null,
    setupCheckInProgress: false,
    setupCheckedForCurrentSession: false,
    modalOpen: false,
    syncInProgress: false,
    setupCompleted: false,
    setupError: '',
  };

  private readonly stateSubject = new BehaviorSubject<TerritoryRegionSetupState>(this.initialState);
  private readonly syncRequestSubject = new Subject<void>();
  private setupStatusRequest$: Observable<TerritoryRegionSetupStatus> | null = null;
  private checkedSessionKey: string | null = null;
  private readonly authSessionSubscription: Subscription | null = null;

  readonly state$ = this.stateSubject.asObservable();

  constructor(
    private readonly auth: AuthService,
    private readonly territoryDashboard: TerritoryDashboardService,
  ) {
    this.authSessionSubscription = this.auth.sessionEvents$?.subscribe(event => {
      if (event !== 'login-success') {
        this.resetSession();
      }
    }) ?? null;

    this.syncRequestSubject
      .pipe(exhaustMap(() => this.runInitialSync()))
      .subscribe();
  }

  ensureCheckedForCurrentSession(): void {
    const user = this.auth.getUser();
    if (!user || !this.isTerritoryManager(user)) {
      this.resetSession();
      return;
    }

    const sessionKey = this.buildSessionKey(user, this.territoryDashboard.getActiveTerritoryId());
    if (this.checkedSessionKey !== sessionKey) {
      this.checkedSessionKey = sessionKey;
      this.patchState({
        setupStatus: null,
        setupCheckInProgress: false,
        setupCheckedForCurrentSession: false,
        modalOpen: false,
        syncInProgress: false,
        setupCompleted: false,
        setupError: '',
      });
    }

    const state = this.stateSubject.value;
    if (state.setupCheckInProgress || state.setupCheckedForCurrentSession || state.setupCompleted) {
      return;
    }

    this.refreshSetupStatus(this.territoryDashboard.getActiveTerritoryId(), {
      openModalOnRequired: true,
      openModalOnError: true,
      markSessionChecked: true,
    }).subscribe({ error: () => undefined });
  }

  retrySetupCheck(): void {
    if (!this.isTerritoryManager(this.auth.getUser())) {
      this.resetSession();
      return;
    }

    this.refreshSetupStatus(this.stateSubject.value.setupStatus?.territoryId ?? this.territoryDashboard.getActiveTerritoryId(), {
      force: true,
      openModalOnRequired: true,
      openModalOnError: true,
      markSessionChecked: true,
    }).subscribe({ error: () => undefined });
  }

  refreshSetupStatus(
    territoryId?: number | null,
    options: SetupStatusOptions = {},
  ): Observable<TerritoryRegionSetupStatus> {
    return this.fetchSetupStatus(territoryId, {
      force: false,
      openModalOnRequired: false,
      openModalOnError: false,
      markSessionChecked: false,
      ...options,
    });
  }

  requestInitialSync(): void {
    if (this.stateSubject.value.syncInProgress) {
      return;
    }

    this.syncRequestSubject.next();
  }

  resetSession(): void {
    this.checkedSessionKey = null;
    this.setupStatusRequest$ = null;
    this.stateSubject.next(this.initialState);
  }

  private runInitialSync(): Observable<TerritoryRegionSetupStatus> {
    const status = this.stateSubject.value.setupStatus;
    if (!this.isTerritoryManager(this.auth.getUser()) || !status || !this.canStartSetup(status)) {
      return EMPTY;
    }

    this.patchState({
      modalOpen: true,
      syncInProgress: true,
      setupError: '',
    });

    return this.territoryDashboard
      .syncRegionsFromAdm1(status.territoryId, false)
      .pipe(
        switchMap(summary => {
          if (summary.failedCount > 0 || summary.ambiguousCount > 0) {
            this.patchState({
              setupError: 'Region setup could not complete safely. Please retry or contact an administrator.',
              modalOpen: true,
            });
            return EMPTY;
          }

          this.territoryDashboard.notifyRegionSetupChanged();
          return this.fetchSetupStatus(status.territoryId, {
            force: true,
            openModalOnRequired: true,
            openModalOnError: true,
            markSessionChecked: true,
          });
        }),
        catchError(error => {
          const serverMessage = typeof error?.error?.message === 'string'
            ? error.error.message.trim()
            : '';

          this.patchState({
            setupError: serverMessage || 'Unable to import ADM1 boundaries and initialize regions. Please try again.',
            modalOpen: true,
          });
          return EMPTY;
        }),
        finalize(() => {
          this.patchState({ syncInProgress: false });
        }),
      );
  }

  private fetchSetupStatus(
    territoryId: number | null | undefined,
    options: ResolvedSetupStatusOptions,
  ): Observable<TerritoryRegionSetupStatus> {
    if (this.setupStatusRequest$ && !options.force) {
      return this.setupStatusRequest$;
    }

    this.patchState({
      setupCheckInProgress: true,
      setupError: '',
    });

    this.setupStatusRequest$ = this.territoryDashboard
      .getRegionSetupStatus(territoryId)
      .pipe(
        tap(status => this.applySetupStatus(status, options)),
        catchError(error => {
          this.patchState({
            setupStatus: null,
            setupCheckedForCurrentSession: options.markSessionChecked || this.stateSubject.value.setupCheckedForCurrentSession,
            modalOpen: options.openModalOnError,
            setupCompleted: false,
            setupError: 'Unable to check territory setup. Please retry or sign out.',
          });
          return throwError(() => error);
        }),
        finalize(() => {
          this.setupStatusRequest$ = null;
          this.patchState({ setupCheckInProgress: false });
        }),
        shareReplay({ bufferSize: 1, refCount: false }),
      );

    return this.setupStatusRequest$;
  }

  private applySetupStatus(
    status: TerritoryRegionSetupStatus,
    options: ResolvedSetupStatusOptions,
  ): void {
    this.territoryDashboard.setActiveTerritoryId(status.territoryId);

    const setupCompleted = status.onboardingCompleted || (!status.requiresInitialSync && !status.missingCandidateData);
    const modalOpen = options.openModalOnRequired && this.shouldBlockForSetup(status);

    this.patchState({
      setupStatus: status,
      setupCheckedForCurrentSession: options.markSessionChecked || this.stateSubject.value.setupCheckedForCurrentSession,
      modalOpen,
      setupCompleted,
      setupError: '',
    });
  }

  private shouldBlockForSetup(status: TerritoryRegionSetupStatus): boolean {
    return !status.onboardingCompleted && (status.requiresInitialSync || status.missingCandidateData);
  }

  private canStartSetup(status: TerritoryRegionSetupStatus): boolean {
    return Boolean(
      !status.onboardingCompleted &&
      status.requiresInitialSync &&
      status.canSync,
    );
  }

  private patchState(patch: Partial<TerritoryRegionSetupState>): void {
    this.stateSubject.next({
      ...this.stateSubject.value,
      ...patch,
    });
  }

  private isTerritoryManager(user: User | null): boolean {
    return user?.activeRoleCode === 'TerritoryManager';
  }

  private buildSessionKey(user: User, territoryId: number | null): string {
    return `${user.id}:${territoryId ?? 'default'}`;
  }
}
