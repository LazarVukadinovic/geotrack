import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, HostListener, OnDestroy, OnInit } from '@angular/core';
import { Router, RouterOutlet } from '@angular/router';
import { Subscription } from 'rxjs';
import { TerritoryRegionSetupStatus } from '../../models/dashboard.model';
import { AuthService } from '../../services/auth.service';
import {
  TerritoryRegionSetupService,
  TerritoryRegionSetupState,
} from '../../services/territory-region-setup.service';

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [CommonModule, RouterOutlet],
  template: `
    <router-outlet></router-outlet>

    <div
      *ngIf="showSetupModal"
      class="fixed inset-0 z-[1000] flex items-center justify-center bg-slate-950/55 px-4"
      role="presentation"
      (click)="$event.stopPropagation()"
    >
      <section
        role="dialog"
        aria-modal="true"
        aria-labelledby="territory-setup-title"
        class="w-full max-w-[540px] rounded-lg bg-white p-6 shadow-2xl"
        (click)="$event.stopPropagation()"
      >
        <div class="mb-5 flex items-start gap-4">
          <div class="flex h-11 w-11 flex-shrink-0 items-center justify-center rounded-lg bg-blue-50 text-blue-700">
            <svg class="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true">
              <circle cx="12" cy="12" r="10" />
              <line x1="2" y1="12" x2="22" y2="12" />
              <path d="M12 2a15.3 15.3 0 014 10 15.3 15.3 0 01-4 10 15.3 15.3 0 01-4-10 15.3 15.3 0 014-10z" />
            </svg>
          </div>
          <div class="min-w-0">
            <h2 id="territory-setup-title" class="text-xl font-bold text-gray-900">Get started with your territory</h2>
            <p class="mt-1 text-sm leading-6 text-gray-600">{{ setupIntroText }}</p>
          </div>
        </div>

        <div *ngIf="setupLoading" class="rounded-lg border border-blue-100 bg-blue-50 px-4 py-3 text-sm font-semibold text-blue-800">
          Checking territory setup...
        </div>

        <ng-container *ngIf="!setupLoading">
          <div class="rounded-lg border border-gray-200 bg-gray-50 px-4 py-3">
            <p class="text-xs font-bold uppercase tracking-wide text-gray-400">Assigned territory</p>
            <p class="mt-1 text-sm font-semibold text-gray-900">{{ assignedTerritoryLabel }}</p>
          </div>

          <div class="mt-4 space-y-3 text-sm leading-6 text-gray-600">
            <p>
              You manage regions inside your assigned country/territory, assign Regional Managers to regions,
              and monitor regional coverage and health.
            </p>
            <p *ngIf="!setupStatus?.missingCandidateData">
              GeoTrack will initialize official ADM1 regions from boundary data. This only needs to be done once.
            </p>
            <p *ngIf="setupStatus?.missingCandidateData" class="rounded-lg border border-blue-200 bg-blue-50 px-4 py-3 font-semibold text-blue-800">
              ADM1 boundary data has not been imported yet. Get Started will import it for {{ setupStatus?.countryIso3 }} and create the official regions automatically.
            </p>
          </div>

          <div *ngIf="setupError" class="mt-4 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm font-semibold text-red-700">
            {{ setupError }}
          </div>

          <div class="mt-6 flex items-center justify-between gap-3">
            <button
              type="button"
              (click)="signOut()"
              class="rounded-lg border border-gray-200 bg-white px-4 py-2 text-sm font-semibold text-gray-600 transition-colors hover:bg-gray-50"
            >
              Sign out
            </button>

            <button
              *ngIf="!setupStatus && setupError"
              type="button"
              (click)="retrySetupCheck()"
              [disabled]="setupLoading"
              class="inline-flex items-center gap-2 rounded-lg border border-blue-700 bg-blue-700 px-4 py-2 text-sm font-bold text-white shadow-sm transition-colors hover:bg-blue-800 disabled:cursor-not-allowed disabled:border-gray-300 disabled:bg-gray-300"
            >
              Retry
            </button>

            <button
              *ngIf="setupStatus || !setupError"
              type="button"
              (click)="startSetup()"
              [disabled]="setupSyncing || !canStartSetup"
              class="inline-flex items-center gap-2 rounded-lg border border-blue-700 bg-blue-700 px-4 py-2 text-sm font-bold text-white shadow-sm transition-colors hover:bg-blue-800 disabled:cursor-not-allowed disabled:border-gray-300 disabled:bg-gray-300"
            >
              <svg *ngIf="setupSyncing" class="h-4 w-4 animate-spin" viewBox="0 0 24 24" fill="none" aria-hidden="true">
                <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v8H4z"></path>
              </svg>
              {{ setupActionLabel }}
            </button>
          </div>
        </ng-container>
      </section>
    </div>
  `,
  styles: [`:host { display: block; height: 100vh; }`]
})
export class AppShellComponent implements OnInit, OnDestroy {
  setupStatus: TerritoryRegionSetupStatus | null = null;
  setupLoading = false;
  setupSyncing = false;
  setupError = '';
  setupModalOpen = false;

  private setupStateSubscription: Subscription | null = null;

  constructor(
    private readonly auth: AuthService,
    private readonly territorySetup: TerritoryRegionSetupService,
    private readonly router: Router,
    private readonly cdr: ChangeDetectorRef,
  ) {}

  ngOnInit(): void {
    this.setupStateSubscription = this.territorySetup.state$
      .subscribe(state => this.applySetupState(state));
    this.territorySetup.ensureCheckedForCurrentSession();
  }

  ngOnDestroy(): void {
    this.setupStateSubscription?.unsubscribe();
  }

  get showSetupModal(): boolean {
    if (!this.isActiveTerritoryManager) {
      return false;
    }

    return this.setupModalOpen;
  }

  get setupIntroText(): string {
    if (this.setupStatus?.missingCandidateData) {
      return 'GeoTrack needs to import ADM1 boundary data and initialize regions for your assigned territory.';
    }

    return 'Before you can manage assignments and regional coverage, GeoTrack needs to initialize regions for your assigned territory.';
  }

  get assignedTerritoryLabel(): string {
    if (!this.setupStatus) {
      return this.setupError ? 'Unavailable' : 'Loading...';
    }

    return `${this.setupStatus.territoryName} (${this.setupStatus.countryIso3})`;
  }

  get canStartSetup(): boolean {
    return Boolean(
      !this.setupStatus?.onboardingCompleted &&
      this.setupStatus?.requiresInitialSync &&
      this.setupStatus.canSync,
    );
  }

  get setupActionLabel(): string {
    if (this.setupSyncing) {
      return this.setupStatus?.missingCandidateData
        ? 'Importing boundaries and initializing regions...'
        : 'Initializing regions...';
    }

    return this.setupStatus?.missingCandidateData
      ? 'Import & Get Started'
      : 'Get Started';
  }

  startSetup(): void {
    this.territorySetup.requestInitialSync();
  }

  retrySetupCheck(): void {
    this.territorySetup.retrySetupCheck();
  }

  signOut(): void {
    this.territorySetup.resetSession();
    this.auth.logout();
    this.router.navigateByUrl('/login');
  }

  @HostListener('document:keydown.escape', ['$event'])
  onEscape(event: Event): void {
    if (this.showSetupModal) {
      event.preventDefault();
      event.stopPropagation();
    }
  }

  private applySetupState(state: TerritoryRegionSetupState): void {
    this.setupStatus = state.setupStatus;
    this.setupLoading = state.setupCheckInProgress;
    this.setupSyncing = state.syncInProgress;
    this.setupError = state.setupError;
    this.setupModalOpen = state.modalOpen;
    // Angular 21 is zoneless here; manual RxJS subscriptions must notify the view.
    this.cdr.markForCheck();
  }

  private get isActiveTerritoryManager(): boolean {
    return this.auth.getUser()?.activeRoleCode === 'TerritoryManager';
  }
}
