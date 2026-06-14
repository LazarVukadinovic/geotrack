import {
  AfterViewInit,
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  inject,
  NgZone,
  OnDestroy,
  OnInit,
  ViewChild,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router } from '@angular/router';
import { finalize, Subscription } from 'rxjs';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { AuthService } from '../../../../core/services/auth.service';
import { TerritoryDashboardService } from '../../../../core/services/territory-dashboard.service';
import { MapTileLayerService } from '../../../../core/services/map-tile-layer.service';
import {
  BoundaryRegionCandidate,
  BoundaryRegionCandidateResponse,
  RegionalAssignedRegion,
  TerritoryAssignedTerritory,
  TerritoryRegionCreateContext,
} from '../../../../core/models/dashboard.model';

declare const L: any;

@Component({
  selector: 'app-add-region',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './add-region.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: [`
    .region-map .leaflet-container {
      font: inherit;
    }
  `],
})
export class AddRegionComponent implements OnInit, AfterViewInit, OnDestroy {
  @ViewChild('mapContainer') mapContainer?: ElementRef<HTMLDivElement>;

  assignedTerritories: RegionalAssignedRegion[] = [];
  activeTerritoryId: number | null = null;
  context: TerritoryRegionCreateContext | null = null;
  candidateResponse: BoundaryRegionCandidateResponse | null = null;
  candidates: BoundaryRegionCandidate[] = [];
  selectedCandidate: BoundaryRegionCandidate | null = null;

  territoryLoading = false;
  contextLoading = false;
  candidateLoading = false;
  isSaving = false;
  errorMessage: string | null = null;
  feedbackMessage: string | null = null;
  candidateErrorMessage: string | null = null;
  candidateNoticeMessage: string | null = null;

  regionName = '';

  private map: any = null;
  private territoriesSubscription: Subscription | null = null;
  private contextSubscription: Subscription | null = null;
  private candidateSubscription: Subscription | null = null;
  private createSubscription: Subscription | null = null;
  private readonly mapTileLayer = inject(MapTileLayerService);

  constructor(
    private readonly zone: NgZone,
    private readonly cdr: ChangeDetectorRef,
    private readonly router: Router,
    private readonly route: ActivatedRoute,
    private readonly auth: AuthService,
    private readonly territoryDashboard: TerritoryDashboardService,
  ) {}

  ngOnInit(): void {
    this.loadAssignedTerritories();
  }

  ngAfterViewInit(): void {
    setTimeout(() => this.initMap(), 0);
  }

  ngOnDestroy(): void {
    this.territoriesSubscription?.unsubscribe();
    this.contextSubscription?.unsubscribe();
    this.candidateSubscription?.unsubscribe();
    this.createSubscription?.unsubscribe();

    if (this.map) {
      this.map.remove();
      this.map = null;
    }
  }

  get userName(): string {
    return this.auth.fullName;
  }

  get userRole(): string {
    return this.auth.getUser()?.role ?? '';
  }

  get activeTerritoryName(): string {
    return this.assignedTerritories.find(item => item.id === this.activeTerritoryId)?.displayName ?? '';
  }

  get activeTerritoryDisplayName(): string {
    return this.context?.territory?.displayName || this.activeTerritoryName || 'No assigned territory';
  }

  get canSave(): boolean {
    return Boolean(
      this.activeTerritoryId &&
      this.context?.canCreate &&
      this.selectedCandidate?.canSelect &&
      this.selectedCandidate?.shapeId &&
      this.regionName.trim() &&
      !this.territoryLoading &&
      !this.contextLoading &&
      !this.candidateLoading &&
      !this.isSaving,
    );
  }

  get selectedRegionDisplayName(): string {
    return this.selectedCandidate?.displayName || this.selectedCandidate?.name || 'None selected';
  }

  get selectedBoundaryDisplayName(): string {
    return this.selectedCandidate
      ? this.selectedRegionDisplayName
      : 'Required';
  }

  get availableCandidateCount(): number {
    return this.candidates.filter(candidate => candidate.canSelect).length;
  }

  get existingRegionBoundaryCount(): number {
    return this.context?.existingRegions?.length ?? 0;
  }

  get hasMissingAdm1Data(): boolean {
    return Boolean(
      this.context?.canCreate &&
      !this.candidateLoading &&
      !this.candidateErrorMessage &&
      this.candidates.length === 0,
    );
  }

  get selectionStateLabel(): string {
    return this.selectedCandidate ? this.selectedRegionDisplayName : 'Required';
  }

  onTerritorySelected(territoryId: number): void {
    if (territoryId === this.activeTerritoryId) return;

    this.activeTerritoryId = territoryId;
    this.territoryDashboard.setActiveTerritoryId(territoryId);
    this.clearSelection(false);
    this.loadCreateContext(territoryId);
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { territoryId },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
    this.notifyStateChanged();
  }

  selectCandidate(candidate: BoundaryRegionCandidate): void {
    if (!candidate.canSelect) {
      if (candidate.status === 'ExistingRegion' && candidate.existingRegionName) {
        this.candidateNoticeMessage = `Already created as: ${candidate.existingRegionName}.`;
        this.notifyStateChanged();
        return;
      }

      this.candidateNoticeMessage = candidate.validationMessage ||
        `${candidate.displayName || candidate.name} has already been created.`;
      this.notifyStateChanged();
      return;
    }

    this.selectedCandidate = candidate;
    this.regionName = candidate.displayName || candidate.name || '';
    this.candidateNoticeMessage = null;
    this.feedbackMessage = null;
    this.renderMapLayers(false);
    this.notifyStateChanged();
    this.save();
  }

  clearSelection(render = true): void {
    this.selectedCandidate = null;
    this.regionName = '';
    this.candidateNoticeMessage = null;
    if (render) {
      this.renderMapLayers(false);
    }
    this.notifyStateChanged();
  }

  retryCandidates(): void {
    if (this.activeTerritoryId) {
      this.loadBoundaryCandidates(this.activeTerritoryId);
    }
  }

  save(): void {
    if (!this.canSave || !this.selectedCandidate || !this.activeTerritoryId) {
      this.errorMessage = 'Select an available ADM1 region from the map before adding a region.';
      this.notifyStateChanged();
      return;
    }

    this.isSaving = true;
    this.errorMessage = null;
    this.feedbackMessage = null;
    this.candidateNoticeMessage = null;
    this.notifyStateChanged();

    const candidate = this.selectedCandidate;
    this.createSubscription?.unsubscribe();
    this.createSubscription = this.territoryDashboard.createRegion({
      territoryId: this.activeTerritoryId,
      name: this.regionName.trim() || candidate.displayName || candidate.name,
      admLevel: 'ADM1',
      shapeGroup: candidate.shapeGroup,
      shapeId: candidate.shapeId,
      candidateExternalKey: candidate.boundaryExternalKey || candidate.candidateId,
      boundaryCandidateId: candidate.candidateId,
    })
      .pipe(
        finalize(() => {
          this.isSaving = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: created => {
          const createdName = created.displayName || created.name;
          this.territoryDashboard.setActiveTerritoryId(created.territoryId);
          this.router.navigate(['/territory/regions'], {
            state: {
              feedbackMessage: `${createdName} was added.`,
              createdRegionName: createdName,
              territoryId: created.territoryId,
            },
          });
        },
        error: error => this.handleSaveError(error),
      });
  }

  cancel(): void {
    this.router.navigate(['/territory/regions'], {
      state: { territoryId: this.activeTerritoryId },
    });
  }

  candidateTrackBy(_index: number, candidate: BoundaryRegionCandidate): string {
    return this.candidateKey(candidate);
  }

  isSelectedCandidate(candidate: BoundaryRegionCandidate): boolean {
    return this.selectedCandidate
      ? this.candidateKey(this.selectedCandidate) === this.candidateKey(candidate)
      : false;
  }

  private loadAssignedTerritories(): void {
    this.territoryLoading = true;
    this.errorMessage = null;
    this.notifyStateChanged();

    this.territoriesSubscription?.unsubscribe();
    this.territoriesSubscription = this.territoryDashboard.getAssignedTerritories()
      .pipe(
        finalize(() => {
          this.territoryLoading = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: territories => this.applyAssignedTerritories(territories ?? []),
        error: error => this.handleLoadError(error, 'Unable to load assigned territories.'),
      });
  }

  private applyAssignedTerritories(territories: TerritoryAssignedTerritory[]): void {
    this.assignedTerritories = territories.map(territory => ({
      id: territory.id,
      name: territory.name,
      displayName: territory.displayName,
      statusLabel: territory.statusLabel,
    }));

    const queryTerritoryId = Number(this.route.snapshot.queryParamMap.get('territoryId'));
    const stateTerritoryId = Number((history.state as { territoryId?: number | null })?.territoryId);
    const persistedTerritoryId = this.territoryDashboard.getActiveTerritoryId();
    const selected =
      this.assignedTerritories.find(territory => territory.id === queryTerritoryId) ??
      this.assignedTerritories.find(territory => territory.id === stateTerritoryId) ??
      this.assignedTerritories.find(territory => territory.id === persistedTerritoryId) ??
      this.assignedTerritories[0] ??
      null;

    this.activeTerritoryId = selected?.id ?? null;
    this.territoryDashboard.setActiveTerritoryId(this.activeTerritoryId);

    if (!this.activeTerritoryId) {
      this.context = null;
      this.candidates = [];
      this.errorMessage = 'No territory is assigned to this account.';
      this.renderMapLayers(true);
      this.notifyStateChanged();
      return;
    }

    this.loadCreateContext(this.activeTerritoryId);
  }

  private loadCreateContext(territoryId: number): void {
    this.contextLoading = true;
    this.errorMessage = null;
    this.feedbackMessage = null;
    this.candidateErrorMessage = null;
    this.candidateNoticeMessage = null;
    this.context = null;
    this.candidateResponse = null;
    this.candidates = [];
    this.clearSelection(false);
    this.renderMapLayers(true);
    this.notifyStateChanged();

    this.contextSubscription?.unsubscribe();
    this.contextSubscription = this.territoryDashboard.getRegionCreateContext(territoryId)
      .pipe(
        finalize(() => {
          this.contextLoading = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: context => {
          this.context = context;
          if (context.territory?.id) {
            this.activeTerritoryId = context.territory.id;
            this.territoryDashboard.setActiveTerritoryId(context.territory.id);
          }

          if (!context.canCreate) {
            this.feedbackMessage = context.boundaryRequiredMessage ??
              'Assigned country/territory must have a boundary before adding regions.';
            this.renderMapLayers(true);
            return;
          }

          this.feedbackMessage = null;
          this.renderMapLayers(true);
          this.loadBoundaryCandidates(context.territory?.id ?? territoryId);
        },
        error: error => this.handleLoadError(error, 'Unable to load region creation context.'),
      });
  }

  private loadBoundaryCandidates(territoryId: number): void {
    this.candidateLoading = true;
    this.candidateErrorMessage = null;
    this.candidateNoticeMessage = null;
    this.candidateResponse = null;
    this.candidates = [];
    this.clearSelection(false);
    this.renderMapLayers(false);
    this.notifyStateChanged();

    this.candidateSubscription?.unsubscribe();
    this.candidateSubscription = this.territoryDashboard.getRegionBoundaryCandidates(territoryId, 'ADM1', 'display', 100)
      .pipe(
        finalize(() => {
          this.candidateLoading = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: response => {
          this.candidateResponse = response;
          this.candidates = response.items ?? [];
          this.renderMapLayers(true);
        },
        error: error => this.handleCandidateError(error),
      });
  }

  private initMap(): void {
    if (this.map || !this.mapContainer || typeof L === 'undefined') return;

    this.zone.runOutsideAngular(() => {
      this.map = L.map(this.mapContainer!.nativeElement, {
        zoomControl: true,
        attributionControl: false,
        preferCanvas: true,
      });
      this.mapTileLayer.addTo(L, this.map);
    });

    setTimeout(() => {
      this.map?.invalidateSize();
      this.renderMapLayers(true);
    }, 0);
  }

  private renderMapLayers(fitViewport: boolean): void {
    if (!this.map || typeof L === 'undefined') return;

    this.removeFlaggedLayers('_isTerritoryBoundary');
    this.removeFlaggedLayers('_isSelectedAdm1Candidate');
    this.removeFlaggedLayers('_isExistingRegionBoundary');
    this.removeFlaggedLayers('_isAdm1Candidate');

    const territoryGeometry = this.context?.territoryBoundaryGeoJson;
    if (territoryGeometry) {
      const territoryLayer = L.geoJSON(territoryGeometry, {
        interactive: false,
        style: {
          color: '#1d4ed8',
          weight: 2,
          opacity: 0.95,
          fillColor: '#93c5fd',
          fillOpacity: 0.10,
        },
      });
      territoryLayer._isTerritoryBoundary = true;
      territoryLayer.addTo(this.map);
    }

    for (const candidate of this.candidates) {
      if (candidate.status === 'ExistingRegion' || this.isSelectedCandidate(candidate)) continue;
      if (!candidate.geometryGeoJson) continue;
      const candidateLayer = L.geoJSON(candidate.geometryGeoJson, {
        interactive: true,
        style: () => this.styleForCandidate(candidate),
        onEachFeature: (_feature: unknown, layer: any) => this.bindCandidateLayer(layer, candidate),
      });
      candidateLayer._isAdm1Candidate = true;
      candidateLayer.addTo(this.map);
    }

    for (const region of this.context?.existingRegions ?? []) {
      if (!region.geometryGeoJson) continue;
      const regionLayer = L.geoJSON(region.geometryGeoJson, {
        interactive: true,
        style: {
          color: '#2563eb',
          weight: 1.8,
          opacity: 0.92,
          fillColor: '#93c5fd',
          fillOpacity: 0.22,
          dashArray: '5 4',
        },
        onEachFeature: (_feature: unknown, layer: any) => {
          layer.bindTooltip(this.existingRegionTooltip(region.displayName || region.name), { sticky: true });
        },
      });
      regionLayer._isExistingRegionBoundary = true;
      regionLayer.addTo(this.map);
    }

    if (this.selectedCandidate?.geometryGeoJson) {
      const selectedLayer = L.geoJSON(this.selectedCandidate.geometryGeoJson, {
        interactive: true,
        style: () => this.styleForCandidate(this.selectedCandidate!),
        onEachFeature: (_feature: unknown, layer: any) => this.bindCandidateLayer(layer, this.selectedCandidate!),
      });
      selectedLayer._isSelectedAdm1Candidate = true;
      selectedLayer.addTo(this.map);
    }

    if (fitViewport) {
      this.applyViewport();
    }
  }

  private bindCandidateLayer(layer: any, candidate: BoundaryRegionCandidate): void {
    layer.bindTooltip(this.candidateTooltip(candidate), { sticky: true });
    layer.on('click', () => this.zone.run(() => this.selectCandidate(candidate)));
    layer.on('mouseover', (event: any) => {
      if (candidate.canSelect && !this.isSelectedCandidate(candidate)) {
        event.target.setStyle({
          fillColor: '#bbf7d0',
          fillOpacity: 0.52,
          color: '#15803d',
          weight: 2,
        });
      }
    });
    layer.on('mouseout', (event: any) => event.target.setStyle(this.styleForCandidate(candidate)));
  }

  private candidateTooltip(candidate: BoundaryRegionCandidate): string {
    const name = this.escapeHtml(candidate.displayName || candidate.name || 'ADM1 region');
    const existingRegionName = candidate.existingRegionName
      ? this.escapeHtml(candidate.existingRegionName)
      : null;
    const status = candidate.status === 'ExistingRegion'
      ? existingRegionName
        ? `Already created as: ${existingRegionName}`
        : 'Already created'
      : candidate.canSelect
        ? 'Click to select'
        : 'Unavailable';
    const color = candidate.status === 'ExistingRegion'
      ? '#475569'
      : candidate.canSelect
        ? '#047857'
        : '#b45309';

    return `<strong>${name}</strong><br><span style="color:${color};font-size:11px;">${status}</span>`;
  }

  private existingRegionTooltip(regionName: string): string {
    const name = this.escapeHtml(regionName || 'Existing region');
    return `<strong>${name}</strong><br><span style="color:#2563eb;font-size:11px;">Already created as: ${name}</span>`;
  }

  private styleForCandidate(candidate: BoundaryRegionCandidate): Record<string, unknown> {
    if (this.isSelectedCandidate(candidate)) {
      return {
        color: '#047857',
        fillColor: '#10b981',
        fillOpacity: 0.56,
        weight: 2.4,
      };
    }

    if (candidate.status === 'ExistingRegion') {
      return {
        color: '#2563eb',
        fillColor: '#93c5fd',
        fillOpacity: 0.30,
        weight: 1.5,
        dashArray: '5 4',
      };
    }

    if (!candidate.canSelect) {
      return {
        color: '#9ca3af',
        fillColor: '#e5e7eb',
        fillOpacity: 0.24,
        weight: 1,
      };
    }

    return {
      color: '#16a34a',
      fillColor: '#dcfce7',
      fillOpacity: 0.34,
      weight: 1.4,
    };
  }

  private applyViewport(): void {
    if (!this.map || typeof L === 'undefined') return;

    try {
      const territoryLayer = this.findFlaggedLayer('_isTerritoryBoundary');
      if (territoryLayer) {
        this.map.fitBounds(territoryLayer.getBounds(), { padding: [30, 30], animate: false });
        return;
      }

      const viewport = this.context?.viewport;
      if (viewport?.bounds) {
        this.map.fitBounds(
          [
            [viewport.bounds.south, viewport.bounds.west],
            [viewport.bounds.north, viewport.bounds.east],
          ],
          { padding: [30, 30], animate: false },
        );
        return;
      }

      if (
        typeof viewport?.centerLatitude === 'number' &&
        typeof viewport.centerLongitude === 'number' &&
        typeof viewport.zoom === 'number'
      ) {
        this.map.setView([viewport.centerLatitude, viewport.centerLongitude], viewport.zoom, { animate: false });
        return;
      }

      this.map.fitWorld({ animate: false });
    } catch {
      // Leaflet can throw while the container is settling. The next render pass will retry.
    }
  }

  private removeFlaggedLayers(flag: string): void {
    this.map?.eachLayer((layer: any) => {
      if (layer?.[flag]) {
        this.map.removeLayer(layer);
      }
    });
  }

  private findFlaggedLayer(flag: string): any {
    let found: any = null;
    this.map?.eachLayer((layer: any) => {
      if (!found && layer?.[flag]) {
        found = layer;
      }
    });
    return found;
  }

  private candidateKey(candidate: BoundaryRegionCandidate): string {
    return candidate.boundaryExternalKey ||
      candidate.candidateId ||
      `${candidate.shapeGroup ?? ''}:${candidate.shapeId ?? ''}:${candidate.displayName || candidate.name}`;
  }

  private handleLoadError(error: unknown, fallback: string): void {
    this.context = null;
    this.candidates = [];
    this.renderMapLayers(true);

    if (error instanceof HttpErrorResponse) {
      if (error.status === 401) {
        this.errorMessage = 'Sign in with a Territory Manager account to add regions.';
        this.notifyStateChanged();
        return;
      }

      if (error.status === 403) {
        this.errorMessage = 'This account is not authorized for the selected country/territory.';
        this.notifyStateChanged();
        return;
      }

      this.errorMessage = error.error?.message ?? fallback;
      this.notifyStateChanged();
      return;
    }

    this.errorMessage = fallback;
    this.notifyStateChanged();
  }

  private handleCandidateError(error: unknown): void {
    this.candidates = [];
    this.renderMapLayers(false);

    if (error instanceof HttpErrorResponse) {
      this.candidateErrorMessage = error.error?.message ??
        'Unable to load ADM1 boundary candidates for this territory.';
      this.notifyStateChanged();
      return;
    }

    this.candidateErrorMessage = 'Unable to load ADM1 boundary candidates for this territory.';
    this.notifyStateChanged();
  }

  private handleSaveError(error: unknown): void {
    if (error instanceof HttpErrorResponse) {
      this.errorMessage = error.error?.message ?? 'Unable to add region.';
      this.notifyStateChanged();
      return;
    }

    this.errorMessage = 'Unable to add region.';
    this.notifyStateChanged();
  }

  private escapeHtml(value: string): string {
    return value
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  }

  private notifyStateChanged(): void {
    this.cdr.markForCheck();
  }
}
