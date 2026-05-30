import { TestBed } from '@angular/core/testing';
import { EMPTY, of, Subject, throwError } from 'rxjs';
import { vi } from 'vitest';

import { TerritoryRegionSetupStatus, RegionSyncSummary } from '../models/dashboard.model';
import { AuthService } from './auth.service';
import { TerritoryDashboardService } from './territory-dashboard.service';
import { TerritoryRegionSetupService, TerritoryRegionSetupState } from './territory-region-setup.service';

describe('TerritoryRegionSetupService', () => {
  let service: TerritoryRegionSetupService;
  let latestState: TerritoryRegionSetupState;
  let getRegionSetupStatus: ReturnType<typeof vi.fn>;
  let syncRegionsFromAdm1: ReturnType<typeof vi.fn>;
  let notifyRegionSetupChanged: ReturnType<typeof vi.fn>;

  const authService = {
    getUser: vi.fn(() => ({
      id: '42',
      activeRoleCode: 'TerritoryManager',
    })),
    sessionEvents$: EMPTY,
  };

  beforeEach(() => {
    vi.clearAllMocks();
    getRegionSetupStatus = vi.fn(() => of(requiredStatus()));
    syncRegionsFromAdm1 = vi.fn(() => of(syncSummary()));
    notifyRegionSetupChanged = vi.fn();

    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: authService },
        {
          provide: TerritoryDashboardService,
          useValue: {
            getActiveTerritoryId: vi.fn(() => 10),
            setActiveTerritoryId: vi.fn(),
            getRegionSetupStatus,
            syncRegionsFromAdm1,
            notifyRegionSetupChanged,
          },
        },
      ],
    });

    service = TestBed.inject(TerritoryRegionSetupService);
    service.state$.subscribe(state => {
      latestState = state;
    });
  });

  it('checks setup status once for the current Territory Manager session', () => {
    const pendingStatus = new Subject<TerritoryRegionSetupStatus>();
    getRegionSetupStatus.mockReturnValueOnce(pendingStatus.asObservable());

    service.ensureCheckedForCurrentSession();
    service.ensureCheckedForCurrentSession();

    expect(getRegionSetupStatus).toHaveBeenCalledTimes(1);
    expect(latestState.setupCheckInProgress).toBe(true);

    pendingStatus.next(requiredStatus());
    pendingStatus.complete();

    service.ensureCheckedForCurrentSession();

    expect(getRegionSetupStatus).toHaveBeenCalledTimes(1);
    expect(latestState.modalOpen).toBe(true);
  });

  it('does not check or open the modal for non-Territory Manager roles', () => {
    authService.getUser.mockReturnValueOnce({
      id: '7',
      activeRoleCode: 'RegionalManager',
    });

    service.ensureCheckedForCurrentSession();

    expect(getRegionSetupStatus).not.toHaveBeenCalled();
    expect(latestState.modalOpen).toBe(false);
  });

  it('keeps one modal open on setup-status failure and retries explicitly', () => {
    getRegionSetupStatus.mockReturnValueOnce(throwError(() => new Error('status failed')));

    service.ensureCheckedForCurrentSession();
    service.ensureCheckedForCurrentSession();

    expect(getRegionSetupStatus).toHaveBeenCalledTimes(1);
    expect(latestState.modalOpen).toBe(true);
    expect(latestState.setupCheckInProgress).toBe(false);
    expect(latestState.setupError).toContain('Unable to check territory setup');

    getRegionSetupStatus.mockReturnValueOnce(of(requiredStatus()));
    service.retrySetupCheck();

    expect(getRegionSetupStatus).toHaveBeenCalledTimes(2);
    expect(latestState.modalOpen).toBe(true);
    expect(latestState.setupError).toBe('');
  });

  it('runs initial sync once, refreshes status, and closes after completion', () => {
    const syncResult = new Subject<RegionSyncSummary>();
    getRegionSetupStatus
      .mockReturnValueOnce(of(requiredStatus()))
      .mockReturnValueOnce(of(readyStatus()));
    syncRegionsFromAdm1.mockReturnValueOnce(syncResult.asObservable());

    service.ensureCheckedForCurrentSession();
    service.requestInitialSync();
    service.requestInitialSync();

    expect(syncRegionsFromAdm1).toHaveBeenCalledTimes(1);
    expect(syncRegionsFromAdm1).toHaveBeenCalledWith(10, false);
    expect(latestState.syncInProgress).toBe(true);

    syncResult.next(syncSummary());
    syncResult.complete();

    expect(notifyRegionSetupChanged).toHaveBeenCalledTimes(1);
    expect(getRegionSetupStatus).toHaveBeenCalledTimes(2);
    expect(latestState.syncInProgress).toBe(false);
    expect(latestState.modalOpen).toBe(false);
    expect(latestState.setupCompleted).toBe(true);
  });

  function requiredStatus(): TerritoryRegionSetupStatus {
    return {
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
  }

  function readyStatus(): TerritoryRegionSetupStatus {
    return {
      ...requiredStatus(),
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
  }

  function syncSummary(): RegionSyncSummary {
    return {
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
      completedAt: '2026-05-01T00:00:00Z',
      items: [],
    };
  }
});
