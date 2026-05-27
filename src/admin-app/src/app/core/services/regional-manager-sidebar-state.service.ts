import { Injectable, computed, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { finalize, Subscription } from 'rxjs';
import { RegionalAssignedRegion, RegionalSidebarSummary } from '../models/dashboard.model';
import { DashboardService } from './dashboard.service';

@Injectable({ providedIn: 'root' })
export class RegionalManagerSidebarStateService {
  private static readonly activeRegionStorageKey = 'geotrack_admin_active_region_id';

  private readonly assignedRegionsSig = signal<RegionalAssignedRegion[]>([]);
  private readonly activeRegionSig = signal<RegionalAssignedRegion | null>(null);
  private readonly activeRegionIdSig = signal<number | null>(null);
  private readonly pendingRequestsCountSig = signal<number | null>(null);
  private readonly loadingSig = signal(false);
  private readonly errorSig = signal<string | null>(null);

  private readonly cachedPendingCounts = new Map<number, number>();
  private summarySubscription: Subscription | null = null;
  private summaryRequestSequence = 0;
  private loadedRegionId: number | null = null;
  private loadingRegionId: number | null = null;

  readonly assignedRegions = computed(() => this.assignedRegionsSig());
  readonly activeRegion = computed(() => this.activeRegionSig());
  readonly activeRegionId = computed(() => this.activeRegionIdSig());
  readonly pendingRequestsCount = computed(() => this.pendingRequestsCountSig());
  readonly loading = computed(() => this.loadingSig());
  readonly error = computed(() => this.errorSig());

  constructor(private readonly dashboardService: DashboardService) {}

  setAssignedRegions(regions: readonly RegionalAssignedRegion[] | null | undefined): void {
    const normalized = this.normalizeRegions(regions);
    this.assignedRegionsSig.set(normalized);

    const activeRegionId = this.activeRegionIdSig();
    if (activeRegionId) {
      const activeRegion = normalized.find(region => region.id === activeRegionId) ?? this.activeRegionSig();
      this.activeRegionSig.set(activeRegion);
    }
  }

  selectRegion(regionId: number, regions?: readonly RegionalAssignedRegion[] | null): void {
    const normalizedRegionId = this.toPositiveRegionId(regionId);
    if (!normalizedRegionId) {
      return;
    }

    if (regions) {
      this.setAssignedRegions(regions);
    }

    this.setActiveRegionId(normalizedRegionId);
    this.persistRegion(normalizedRegionId);
    this.loadSummary(normalizedRegionId);
  }

  ensureLoaded(regionId?: number | null): void {
    const normalizedRegionId =
      this.toPositiveRegionId(regionId) ??
      this.activeRegionIdSig() ??
      this.readPersistedRegionId();

    this.loadSummary(normalizedRegionId);
  }

  refresh(regionId?: number | null): void {
    const normalizedRegionId =
      this.toPositiveRegionId(regionId) ??
      this.activeRegionIdSig() ??
      this.readPersistedRegionId();

    this.loadSummary(normalizedRegionId, true);
  }

  setPendingRequestsCount(regionId: number, count: number): void {
    const normalizedRegionId = this.toPositiveRegionId(regionId);
    if (!normalizedRegionId) {
      return;
    }

    const safeCount = this.toSafeCount(count);
    this.cachedPendingCounts.set(normalizedRegionId, safeCount);
    this.setActiveRegionId(normalizedRegionId);
    this.pendingRequestsCountSig.set(safeCount);
    this.loadedRegionId = normalizedRegionId;
    this.errorSig.set(null);
  }

  clear(): void {
    this.summaryRequestSequence++;
    this.summarySubscription?.unsubscribe();
    this.summarySubscription = null;
    this.loadingRegionId = null;
    this.loadedRegionId = null;
    this.assignedRegionsSig.set([]);
    this.activeRegionSig.set(null);
    this.activeRegionIdSig.set(null);
    this.pendingRequestsCountSig.set(null);
    this.loadingSig.set(false);
    this.errorSig.set(null);
    this.cachedPendingCounts.clear();
  }

  private loadSummary(regionId: number | null, force = false): void {
    if (
      !force &&
      this.pendingRequestsCountSig() !== null &&
      ((regionId !== null && this.loadedRegionId === regionId) ||
        (regionId === null && this.loadedRegionId !== null))
    ) {
      return;
    }

    if (
      !force &&
      this.loadingSig() &&
      this.loadingRegionId === regionId
    ) {
      return;
    }

    const requestSequence = ++this.summaryRequestSequence;
    this.summarySubscription?.unsubscribe();
    this.loadingRegionId = regionId;
    this.loadingSig.set(true);
    this.errorSig.set(null);

    this.summarySubscription = this.dashboardService
      .getSidebarSummary(regionId)
      .pipe(
        finalize(() => {
          if (this.summaryRequestSequence === requestSequence) {
            this.loadingSig.set(false);
            this.loadingRegionId = null;
          }
        }),
      )
      .subscribe({
        next: summary => {
          if (this.summaryRequestSequence !== requestSequence) {
            return;
          }

          this.applySummary(summary, regionId);
        },
        error: error => {
          if (this.summaryRequestSequence !== requestSequence) {
            return;
          }

          this.handleSummaryError(error, regionId);
        },
      });
  }

  private applySummary(summary: RegionalSidebarSummary, requestedRegionId: number | null): void {
    const assignedRegions = this.normalizeRegions(summary.assignedRegions);
    this.assignedRegionsSig.set(assignedRegions);

    const activeRegion =
      summary.activeRegion ??
      assignedRegions.find(region => region.id === requestedRegionId) ??
      null;

    const pendingRequestsCount = this.toSafeCount(summary.pendingRequestsCount);
    this.pendingRequestsCountSig.set(pendingRequestsCount);
    this.errorSig.set(null);

    if (activeRegion) {
      this.activeRegionSig.set(activeRegion);
      this.activeRegionIdSig.set(activeRegion.id);
      this.persistRegion(activeRegion.id);
      this.cachedPendingCounts.set(activeRegion.id, pendingRequestsCount);
      this.loadedRegionId = activeRegion.id;
      return;
    }

    this.activeRegionSig.set(null);
    this.activeRegionIdSig.set(null);
    this.loadedRegionId = null;
  }

  private handleSummaryError(error: unknown, requestedRegionId: number | null): void {
    const message = error instanceof HttpErrorResponse && error.status === 403
      ? 'This account is not authorized for the selected region.'
      : 'Unable to load request badge count.';

    this.errorSig.set(message);
    this.pendingRequestsCountSig.set(null);

    if (requestedRegionId !== null && this.activeRegionIdSig() === requestedRegionId) {
      this.loadedRegionId = null;
    }
  }

  private setActiveRegionId(regionId: number): void {
    const previousRegionId = this.activeRegionIdSig();
    this.activeRegionIdSig.set(regionId);

    const activeRegion = this.assignedRegionsSig().find(region => region.id === regionId) ?? null;
    this.activeRegionSig.set(activeRegion);

    if (previousRegionId !== regionId) {
      this.pendingRequestsCountSig.set(this.cachedPendingCounts.get(regionId) ?? null);
    }
  }

  private normalizeRegions(regions: readonly RegionalAssignedRegion[] | null | undefined): RegionalAssignedRegion[] {
    return (regions ?? [])
      .filter(region => this.toPositiveRegionId(region.id) !== null)
      .map(region => ({
        ...region,
        id: Number(region.id),
        name: String(region.name ?? ''),
        displayName: String(region.displayName ?? region.name ?? ''),
      }));
  }

  private toPositiveRegionId(value: unknown): number | null {
    const parsed = Number(value);
    return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
  }

  private toSafeCount(value: unknown): number {
    return typeof value === 'number' && Number.isFinite(value) && value >= 0 ? value : 0;
  }

  private readPersistedRegionId(): number | null {
    try {
      return this.toPositiveRegionId(sessionStorage.getItem(RegionalManagerSidebarStateService.activeRegionStorageKey));
    } catch {
      return null;
    }
  }

  private persistRegion(regionId: number): void {
    try {
      sessionStorage.setItem(RegionalManagerSidebarStateService.activeRegionStorageKey, String(regionId));
    } catch {
      // Active region persistence is a session convenience only.
    }
  }
}
