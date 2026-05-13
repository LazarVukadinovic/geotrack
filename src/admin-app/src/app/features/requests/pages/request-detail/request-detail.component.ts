import { ChangeDetectorRef, Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { finalize, Subscription } from 'rxjs';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { DashboardService } from '../../../../core/services/dashboard.service';
import { AuthService } from '../../../../core/services/auth.service';
import { ReviewRequestsService } from '../../../../core/services/review-requests.service';
import { RegionalAssignedRegion } from '../../../../core/models/dashboard.model';
import { ReviewRequestDetail } from '../../../../core/models/review-request.model';

interface DetailLoadOptions {
  allowRegionFallback: boolean;
  attemptedRegionIds: number[];
}

@Component({
  selector: 'app-review-request-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './request-detail.component.html',
  styleUrl: './request-detail.component.css',
})
export class RequestDetailComponent implements OnInit, OnDestroy {
  private static readonly activeRegionStorageKey = 'geotrack_admin_active_region_id';

  assignedRegions: RegionalAssignedRegion[] = [];
  activeRegionId: number | null = null;
  request: ReviewRequestDetail | null = null;
  isLoading = true;
  regionLoading = false;
  notFound = false;
  errorMessage: string | null = null;
  authRequired = false;
  reviewerNotes = '';
  rejectReason = '';
  processing = false;

  private detailRequestSequence = 0;
  private regionSubscription: Subscription | null = null;
  private detailSubscription: Subscription | null = null;

  constructor(
    private readonly router: Router,
    private readonly route: ActivatedRoute,
    private readonly auth: AuthService,
    private readonly dashboardService: DashboardService,
    private readonly requestsService: ReviewRequestsService,
    private readonly cdr: ChangeDetectorRef,
  ) {}

  ngOnInit(): void {
    this.loadAssignedRegions();
  }

  ngOnDestroy(): void {
    this.detailRequestSequence++;
    this.regionSubscription?.unsubscribe();
    this.detailSubscription?.unsubscribe();
  }

  get userName(): string {
    return this.auth.fullName;
  }

  get userRole(): string {
    return this.auth.getUser()?.role ?? '';
  }

  get activeRegion(): RegionalAssignedRegion | null {
    return this.assignedRegions.find(region => region.id === this.activeRegionId) ?? null;
  }

  get regionDisplayName(): string {
    return this.activeRegion?.displayName ?? this.request?.regionDisplayName ?? '';
  }

  get canModerate(): boolean {
    return this.request?.status === 'Pending' && !this.processing && !!this.activeRegionId;
  }

  get primaryImageUrl(): string | null {
    return (
      this.request?.images?.find(image => image.isMain)?.url ??
      this.request?.thumbnailImageUrl ??
      this.request?.images?.[0]?.url ??
      null
    );
  }

  get allAmenities(): string[] {
    const values = [
      ...(this.request?.amenities ?? []),
      ...(this.request?.stayAmenities ?? []),
    ];

    return [...new Set(values.filter(Boolean))];
  }

  goBack(): void {
    this.router.navigate(['/requests'], {
      queryParams: this.activeRegionId ? { regionId: this.activeRegionId } : undefined,
    });
  }

  onRegionSelected(regionId: number): void {
    if (regionId === this.activeRegionId) return;

    this.setActiveRegion(regionId, true);
    this.request = null;
    this.reviewerNotes = '';
    this.rejectReason = '';
    this.loadRequest({ allowRegionFallback: false, attemptedRegionIds: [regionId] });
    this.notifyStateChanged();
  }

  approveRequest(): void {
    if (!this.request || !this.activeRegionId || !this.canModerate) return;

    this.processing = true;
    this.errorMessage = null;
    this.notifyStateChanged();
    this.requestsService
      .approveBySource(this.activeRegionId, this.request.source, this.request.requestId, this.reviewerNotes)
      .pipe(
        finalize(() => {
          this.processing = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: result => {
          if (result.item) {
            this.request = { ...this.request!, ...result.item };
          }
          this.goBack();
        },
        error: error => this.handleActionError(error),
      });
  }

  rejectRequest(): void {
    if (!this.request || !this.activeRegionId || !this.canModerate) return;

    this.processing = true;
    this.errorMessage = null;
    this.notifyStateChanged();
    this.requestsService
      .rejectBySource(
        this.activeRegionId,
        this.request.source,
        this.request.requestId,
        this.rejectReason,
        this.reviewerNotes,
      )
      .pipe(
        finalize(() => {
          this.processing = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: result => {
          if (result.item) {
            this.request = { ...this.request!, ...result.item };
          }
          this.goBack();
        },
        error: error => this.handleActionError(error),
      });
  }

  statusBadgeClass(status: string): string {
    switch (status) {
      case 'Approved':
        return 'bg-blue-50 text-blue-700 border border-blue-100';
      case 'Rejected':
        return 'bg-red-50 text-red-600 border border-red-100';
      default:
        return 'bg-amber-50 text-amber-700 border border-amber-100';
    }
  }

  typeBadgeClass(type: string): string {
    switch (type) {
      case 'Destination':
        return 'bg-purple-50 text-purple-700 border border-purple-100';
      case 'Location':
        return 'bg-indigo-50 text-indigo-700 border border-indigo-100';
      case 'Event':
        return 'bg-yellow-50 text-yellow-700 border border-yellow-100';
      case 'Review':
        return 'bg-rose-50 text-rose-700 border border-rose-100';
      default:
        return 'bg-blue-50 text-blue-700 border border-blue-100';
    }
  }

  private loadAssignedRegions(): void {
    this.regionLoading = true;
    this.isLoading = true;
    this.notFound = false;
    this.errorMessage = null;
    this.authRequired = false;
    this.notifyStateChanged();

    this.regionSubscription?.unsubscribe();
    this.regionSubscription = this.dashboardService
      .getAssignedRegions()
      .pipe(
        finalize(() => {
          this.regionLoading = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: regions => {
          this.assignedRegions = regions ?? [];
          if (this.assignedRegions.length === 0) {
            this.activeRegionId = null;
            this.request = null;
            this.isLoading = false;
            this.errorMessage = 'No active assigned region.';
            this.clearPersistedRegion();
            this.notifyStateChanged();
            return;
          }

          const selection = this.resolveRegionSelection();
          if (!selection.regionId) {
            this.activeRegionId = null;
            this.request = null;
            this.isLoading = false;
            this.errorMessage = 'No active assigned region.';
            this.clearPersistedRegion();
            this.notifyStateChanged();
            return;
          }

          this.setActiveRegion(selection.regionId, false);
          this.loadRequest({
            allowRegionFallback: selection.allowRegionFallback,
            attemptedRegionIds: [selection.regionId],
          });
        },
        error: error => this.handleLoadError(error),
      });
  }

  private loadRequest(options: DetailLoadOptions): void {
    const regionId = this.activeRegionId;
    const routeInfo = this.resolveRequestRoute();
    const requestSequence = ++this.detailRequestSequence;
    this.detailSubscription?.unsubscribe();
    this.detailSubscription = null;

    if (!regionId) {
      this.request = null;
      this.isLoading = false;
      this.notFound = false;
      this.errorMessage = 'No active assigned region.';
      this.notifyStateChanged();
      return;
    }

    if (!routeInfo) {
      this.request = null;
      this.isLoading = false;
      this.notFound = true;
      this.errorMessage = null;
      this.notifyStateChanged();
      return;
    }

    this.isLoading = true;
    this.notFound = false;
    this.errorMessage = null;
    this.authRequired = false;
    this.request = null;
    this.notifyStateChanged();

    this.detailSubscription = this.requestsService
      .getDetail(regionId, routeInfo.source, routeInfo.requestId)
      .pipe(
        finalize(() => {
          if (this.detailRequestSequence === requestSequence) {
            this.isLoading = false;
            this.notifyStateChanged();
          }
        }),
      )
      .subscribe({
        next: detail => {
          if (this.detailRequestSequence !== requestSequence) {
            return;
          }

          this.request = detail;
          this.reviewerNotes = detail.decisionNote ?? '';
          this.rejectReason = '';
          this.setActiveRegion(detail.regionId, false);
          this.upsertAssignedRegion(detail);
        },
        error: error => {
          if (this.detailRequestSequence !== requestSequence) {
            return;
          }

          if (this.tryLoadNextAssignedRegion(error, options, regionId)) {
            return;
          }

          this.handleLoadError(error);
        },
      });
  }

  private resolveRequestRoute(): { source: 'Contribution' | 'ReviewReport'; requestId: number } | null {
    const source = this.normalizeRouteSource(this.route.snapshot.paramMap.get('source') ?? 'contribution');
    const idValue = this.route.snapshot.paramMap.get('id');
    const requestId = Number(idValue);
    if (!source || !Number.isFinite(requestId) || requestId <= 0) {
      return null;
    }

    return { source, requestId };
  }

  private resolveRegionSelection(): { regionId: number | null; allowRegionFallback: boolean } {
    const queryRegionId = Number(this.route.snapshot.queryParamMap.get('regionId'));
    const queryRegion = this.assignedRegions.find(region => region.id === queryRegionId);
    if (queryRegion) {
      return { regionId: queryRegion.id, allowRegionFallback: false };
    }

    const persistedRegionId = this.readPersistedRegionId();
    const selected =
      this.assignedRegions.find(region => region.id === persistedRegionId) ??
      this.assignedRegions[0] ??
      null;

    return { regionId: selected?.id ?? null, allowRegionFallback: true };
  }

  private handleLoadError(error: unknown): void {
    this.request = null;
    this.isLoading = false;
    this.authRequired = false;
    if (error instanceof HttpErrorResponse) {
      if (error.status === 401) {
        this.authRequired = true;
        this.errorMessage =
          'Sign in with an authenticated Regional Manager account to view this request.';
        this.notifyStateChanged();
        return;
      }

      if (error.status === 404) {
        this.notFound = true;
        this.errorMessage = null;
        this.notifyStateChanged();
        return;
      }

      if (error.status === 403) {
        this.errorMessage = 'This request is not available or you do not have access.';
        this.notifyStateChanged();
        return;
      }
    }

    this.errorMessage = 'Unable to load request details.';
    this.notifyStateChanged();
  }

  private handleActionError(error: unknown): void {
    if (error instanceof HttpErrorResponse) {
      this.errorMessage = error.error?.message ?? 'Unable to process this request.';
      this.notifyStateChanged();
      return;
    }

    this.errorMessage = 'Unable to process this request.';
    this.notifyStateChanged();
  }

  private tryLoadNextAssignedRegion(
    error: unknown,
    options: DetailLoadOptions,
    currentRegionId: number,
  ): boolean {
    if (
      !options.allowRegionFallback ||
      !(error instanceof HttpErrorResponse) ||
      error.status !== 404
    ) {
      return false;
    }

    const attempted = new Set([...options.attemptedRegionIds, currentRegionId]);
    const nextRegion = this.assignedRegions.find(region => !attempted.has(region.id));
    if (!nextRegion) {
      return false;
    }

    this.setActiveRegion(nextRegion.id, false);
    this.loadRequest({
      allowRegionFallback: true,
      attemptedRegionIds: [...attempted, nextRegion.id],
    });
    return true;
  }

  private setActiveRegion(regionId: number, updateUrl: boolean): void {
    this.activeRegionId = regionId;
    this.persistRegion(regionId);

    if (updateUrl) {
      this.router.navigate([], {
        relativeTo: this.route,
        queryParams: { regionId },
        queryParamsHandling: 'merge',
        replaceUrl: true,
      });
    }

    this.notifyStateChanged();
  }

  private upsertAssignedRegion(detail: ReviewRequestDetail): void {
    if (this.assignedRegions.some(region => region.id === detail.regionId)) {
      return;
    }

    this.assignedRegions = [
      {
        id: detail.regionId,
        name: detail.regionDisplayName,
        displayName: detail.regionDisplayName,
      },
      ...this.assignedRegions,
    ];
  }

  private normalizeRouteSource(source: string): 'Contribution' | 'ReviewReport' | null {
    const normalized = source.trim().replace(/[\s_-]/g, '').toLowerCase();
    if (normalized === 'contribution' || normalized === 'contributions') {
      return 'Contribution';
    }

    if (normalized === 'review' || normalized === 'reviewreport' || normalized === 'reviewreports') {
      return 'ReviewReport';
    }

    return null;
  }

  private notifyStateChanged(): void {
    this.cdr.markForCheck();
  }

  private readPersistedRegionId(): number | null {
    try {
      const value = sessionStorage.getItem(RequestDetailComponent.activeRegionStorageKey);
      const parsed = value ? Number(value) : NaN;
      return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
    } catch {
      return null;
    }
  }

  private persistRegion(regionId: number): void {
    try {
      sessionStorage.setItem(RequestDetailComponent.activeRegionStorageKey, String(regionId));
    } catch {
      // Session persistence is a convenience only.
    }
  }

  private clearPersistedRegion(): void {
    try {
      sessionStorage.removeItem(RequestDetailComponent.activeRegionStorageKey);
    } catch {
      // Session persistence is a convenience only.
    }
  }
}
