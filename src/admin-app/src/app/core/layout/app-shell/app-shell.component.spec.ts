import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
import { vi } from 'vitest';

import { AppShellComponent } from './app-shell.component';
import { AuthService } from '../../services/auth.service';
import { TerritoryDashboardService } from '../../services/territory-dashboard.service';
import { TerritoryRegionSetupStatus } from '../../models/dashboard.model';
import { TerritoryRegionSetupService } from '../../services/territory-region-setup.service';

describe('AppShellComponent territory setup onboarding', () => {
  let fixture: ComponentFixture<AppShellComponent>;
  let getRegionSetupStatus: ReturnType<typeof vi.fn>;
  let syncRegionsFromAdm1: ReturnType<typeof vi.fn>;
  let activeTerritoryChanged$: Subject<number | null>;
  let regionSetupChanged$: Subject<void>;
  let statusQueue: TerritoryRegionSetupStatus[];

  const requiredStatus: TerritoryRegionSetupStatus = {
    territoryId: 10,
    territoryName: 'Croatia',
    countryIso3: 'HRV',
    requiresInitialSync: true,
    onboardingCompleted: false,
    hasSyncedRegions: false,
    syncedRegionCount: 0,
    candidateCount: 21,
    hasCandidateData: true,
    canSync: true,
    canUpdate: false,
    updateAvailable: true,
    missingCandidateData: false,
    regionSetupCompletedAt: null,
    candidateVersionHash: 'candidate-hash',
    syncedVersionHash: null,
    message: 'Initialize official ADM1 regions before managing assignments for this territory.',
  };

  const readyStatus: TerritoryRegionSetupStatus = {
    ...requiredStatus,
    requiresInitialSync: false,
    onboardingCompleted: true,
    hasSyncedRegions: true,
    syncedRegionCount: 21,
    canUpdate: false,
    updateAvailable: false,
    regionSetupCompletedAt: '2026-05-01T00:00:00Z',
    syncedVersionHash: 'synced-hash',
    message: 'Regions are up to date.',
  };

  const missingCandidateStatus: TerritoryRegionSetupStatus = {
    ...requiredStatus,
    requiresInitialSync: true,
    onboardingCompleted: false,
    hasCandidateData: false,
    canSync: false,
    updateAvailable: false,
    missingCandidateData: true,
    candidateCount: 0,
    message: 'ADM1 boundary data is missing for this territory. Ask an administrator to import boundary data.',
  };

  const authService = {
    getUser: vi.fn(() => ({ id: '42', activeRoleCode: 'TerritoryManager' })),
    logout: vi.fn(),
  };

  beforeEach(async () => {
    vi.clearAllMocks();
    activeTerritoryChanged$ = new Subject<number | null>();
    regionSetupChanged$ = new Subject<void>();
    statusQueue = [requiredStatus];
    getRegionSetupStatus = vi.fn(() => of(statusQueue.shift() ?? readyStatus));
    syncRegionsFromAdm1 = vi.fn(() => of({
      territoryId: 10,
      territoryName: 'Croatia',
      countryIso3: 'HRV',
      totalCandidates: 21,
      existingLinkedCount: 0,
      linkedExistingCount: 0,
      createdCount: 21,
      updatedCount: 0,
      skippedCount: 0,
      ambiguousCount: 0,
      failedCount: 0,
      dryRun: false,
      items: [],
    }));

    await TestBed.configureTestingModule({
      imports: [AppShellComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: authService },
        {
          provide: TerritoryDashboardService,
          useValue: {
            activeTerritoryChanged$: activeTerritoryChanged$.asObservable(),
            regionSetupChanged$: regionSetupChanged$.asObservable(),
            getActiveTerritoryId: vi.fn(() => 10),
            setActiveTerritoryId: vi.fn(),
            getRegionSetupStatus,
            syncRegionsFromAdm1,
            notifyRegionSetupChanged: vi.fn(),
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AppShellComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  });

  it('opens a blocking Get Started modal when initial sync is required', () => {
    const text = fixture.nativeElement.textContent;

    expect(text).toContain('Get started with your territory');
    expect(text).toContain('Croatia (HRV)');
    expect(text).toContain('Get Started');
    expect(fixture.nativeElement.querySelector('[aria-label="Close"]')).toBeNull();
  });

  it('does not close the modal on backdrop click or Escape', () => {
    const backdrop = fixture.nativeElement.querySelector('[role="presentation"]') as HTMLElement;
    backdrop.click();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Get started with your territory');
  });

  it('calls sync and closes the modal after setup succeeds', () => {
    statusQueue.push(readyStatus);

    const button = findButton('Get Started');
    button.click();
    fixture.detectChanges();

    expect(syncRegionsFromAdm1).toHaveBeenCalledWith(10, false);
    expect(fixture.nativeElement.textContent).not.toContain('Get started with your territory');
  });

  it('keeps the modal open and shows retry copy after sync error', () => {
    syncRegionsFromAdm1.mockReturnValueOnce(throwError(() => new Error('sync failed')));

    findButton('Get Started').click();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Get started with your territory');
    expect(fixture.nativeElement.textContent).toContain('Unable to initialize regions. Please try again.');
  });

  it('blocks setup when candidate data is missing', async () => {
    const setupService = TestBed.inject(TerritoryRegionSetupService);
    fixture.destroy();
    setupService.resetSession();
    statusQueue = [missingCandidateStatus];
    fixture = TestBed.createComponent(AppShellComponent);
    getRegionSetupStatus.mockReturnValueOnce(of(missingCandidateStatus));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const button = findButton('Get Started');
    expect(fixture.nativeElement.textContent).toContain('Region boundary data is not available yet.');
    expect(button.disabled).toBe(true);
  });

  it('signs out from the blocking modal', () => {
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigateByUrl');

    findButton('Sign out').click();

    expect(authService.logout).toHaveBeenCalled();
    expect(navigate).toHaveBeenCalledWith('/login');
  });

  function findButton(label: string): HTMLButtonElement {
    const buttons = Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[];
    const button = buttons.find(item => item.textContent?.includes(label));
    expect(button).toBeTruthy();
    return button!;
  }
});
