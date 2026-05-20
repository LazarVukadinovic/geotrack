import {
  AfterViewInit,
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  NgZone,
  OnDestroy,
  OnInit,
  ViewChild,
} from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router } from '@angular/router';
import { finalize, Subscription } from 'rxjs';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { AuthService } from '../../../../core/services/auth.service';
import { TerritoryDashboardService } from '../../../../core/services/territory-dashboard.service';
import { DrawTool, ZoneDrawService } from '../../../../core/services/zone-draw.service';
import {
  RegionalAssignedRegion,
  TerritoryAssignedTerritory,
  TerritoryRegionCreateContext,
  TerritoryRegionGeometry,
} from '../../../../core/models/dashboard.model';

declare const L: any;

type RegionBoundaryShape = 'Polygon' | 'Rectangle';
type RegionDrawTool = Extract<DrawTool, 'polygon' | 'rectangle'>;
type Position = [number, number];
type LinearRing = Position[];
type PolygonCoordinates = LinearRing[];

@Component({
  selector: 'app-add-region',
  standalone: true,
  imports: [CommonModule, FormsModule, DecimalPipe, SidebarComponent, TopbarComponent],
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

  territoryLoading = false;
  contextLoading = false;
  isSaving = false;
  errorMessage: string | null = null;
  feedbackMessage: string | null = null;

  regionName = '';
  drawTool: RegionDrawTool = 'polygon';
  boundaryAreaKm2: number | null = null;
  boundaryValidationMessage: string | null = null;
  boundaryConfirmed = false;

  private map: any = null;
  private drawnItems: any = null;
  private drawControl: any = null;
  private activeDrawHandler: any = null;
  private drawnLayer: any = null;
  private boundaryShapeType: RegionBoundaryShape | null = null;
  private territoriesSubscription: Subscription | null = null;
  private contextSubscription: Subscription | null = null;
  private createSubscription: Subscription | null = null;

  constructor(
    private readonly zone: NgZone,
    private readonly cdr: ChangeDetectorRef,
    private readonly router: Router,
    private readonly route: ActivatedRoute,
    private readonly auth: AuthService,
    private readonly territoryDashboard: TerritoryDashboardService,
    private readonly zoneDrawService: ZoneDrawService,
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
    this.createSubscription?.unsubscribe();
    this.disableActiveDrawHandler();

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

  get canDrawBoundary(): boolean {
    return Boolean(this.context?.canCreate && this.context.territoryBoundaryGeoJson && this.map && !this.contextLoading);
  }

  get canSave(): boolean {
    return Boolean(
      this.activeTerritoryId &&
      this.regionName.trim() &&
      this.context?.canCreate &&
      this.boundaryConfirmed &&
      !this.boundaryValidationMessage &&
      !this.territoryLoading &&
      !this.contextLoading &&
      !this.isSaving,
    );
  }

  get drawHint(): string {
    return this.zoneDrawService.drawHints[this.drawTool];
  }

  get existingRegionBoundaryCount(): number {
    return this.context?.existingRegions?.filter(region => !!region.geometryGeoJson).length ?? 0;
  }

  onTerritorySelected(territoryId: number): void {
    if (territoryId === this.activeTerritoryId) return;

    this.activeTerritoryId = territoryId;
    this.territoryDashboard.setActiveTerritoryId(territoryId);
    this.resetBoundary();
    this.loadCreateContext(territoryId);
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { territoryId },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
    this.notifyStateChanged();
  }

  setDrawTool(tool: RegionDrawTool): void {
    this.drawTool = tool;
    this.startDrawing();
    this.notifyStateChanged();
  }

  startDrawing(): void {
    if (!this.canDrawBoundary || typeof L === 'undefined') return;

    this.disableActiveDrawHandler();
    const shapeOptions = {
      color: '#2563eb',
      fillColor: '#60a5fa',
      fillOpacity: 0.18,
      weight: 2,
    };

    this.activeDrawHandler = this.drawTool === 'rectangle'
      ? new L.Draw.Rectangle(this.map, { shapeOptions })
      : new L.Draw.Polygon(this.map, {
          shapeOptions,
          showArea: true,
          allowIntersection: false,
        });
    this.activeDrawHandler.enable();
  }

  clearBoundary(): void {
    this.resetBoundary();
    this.notifyStateChanged();
  }

  save(): void {
    if (!this.canSave || !this.drawnLayer || !this.activeTerritoryId || !this.boundaryShapeType) {
      if (!this.boundaryConfirmed) {
        this.boundaryValidationMessage = 'Region boundary must be inside the assigned country/territory.';
      }
      this.notifyStateChanged();
      return;
    }

    let zone: [number, number][];
    try {
      zone = this.zoneDrawService.extractZoneResult(this.drawnLayer).zone;
    } catch {
      this.boundaryValidationMessage = 'Boundary geometry is invalid.';
      this.notifyStateChanged();
      return;
    }

    this.isSaving = true;
    this.errorMessage = null;
    this.feedbackMessage = null;
    this.notifyStateChanged();

    this.createSubscription?.unsubscribe();
    this.createSubscription = this.territoryDashboard.createRegion({
      territoryId: this.activeTerritoryId,
      name: this.regionName.trim(),
      boundarySource: 'ManualDraw',
      boundary: {
        shapeType: this.boundaryShapeType,
        coordinates: zone.map(([latitude, longitude]) => ({ latitude, longitude })),
      },
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
      this.errorMessage = 'No assigned country/territory is available for this account.';
      this.renderContextLayers();
      this.notifyStateChanged();
      return;
    }

    this.loadCreateContext(this.activeTerritoryId);
  }

  private loadCreateContext(territoryId: number): void {
    this.contextLoading = true;
    this.errorMessage = null;
    this.feedbackMessage = null;
    this.context = null;
    this.renderContextLayers();
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
          this.feedbackMessage = context.canCreate
            ? null
            : context.boundaryRequiredMessage ?? 'Assigned country/territory must have a boundary before adding regions.';
          this.renderContextLayers();
        },
        error: error => this.handleLoadError(error, 'Unable to load region creation context.'),
      });
  }

  private initMap(): void {
    if (this.map || !this.mapContainer || typeof L === 'undefined') return;

    this.zone.runOutsideAngular(() => {
      this.map = L.map(this.mapContainer!.nativeElement, {
        zoomControl: true,
        attributionControl: false,
      });
      L.tileLayer('https://tiles.stadiamaps.com/tiles/alidade_smooth/{z}/{x}/{y}{r}.png', {
        maxZoom: 19,
      }).addTo(this.map);

      this.drawnItems = new L.FeatureGroup();
      this.drawnItems.addTo(this.map);

      if (L.Control?.Draw) {
        this.drawControl = new L.Control.Draw({
          draw: {
            polygon: {
              allowIntersection: false,
              showArea: true,
              shapeOptions: { color: '#2563eb', fillColor: '#60a5fa', fillOpacity: 0.18, weight: 2 },
            },
            rectangle: {
              shapeOptions: { color: '#2563eb', fillColor: '#60a5fa', fillOpacity: 0.18, weight: 2 },
            },
            circle: false,
            circlemarker: false,
            marker: false,
            polyline: false,
          },
          edit: {
            featureGroup: this.drawnItems,
            remove: true,
          },
        });
        this.map.addControl(this.drawControl);
      }

      this.map.on('draw:created', (event: any) => {
        this.zone.run(() => this.handleDrawCreated(event.layer, event.layerType));
      });
      this.map.on('draw:edited', () => {
        this.zone.run(() => this.handleDrawEdited());
      });
      this.map.on('draw:deleted', () => {
        this.zone.run(() => this.handleDrawDeleted());
      });
    });

    setTimeout(() => {
      this.map?.invalidateSize();
      this.renderContextLayers();
    }, 0);
  }

  private handleDrawCreated(layer: any, layerType?: string): void {
    if (!this.drawnItems) return;

    this.drawnItems.clearLayers();
    this.drawnItems.addLayer(layer);
    this.drawnLayer = layer;
    this.boundaryShapeType = layerType === 'rectangle' ? 'Rectangle' : 'Polygon';
    this.applyBoundaryValidation(layer);
    this.disableActiveDrawHandler();
    this.notifyStateChanged();
  }

  private handleDrawEdited(): void {
    const layer = this.firstDrawnLayer();
    if (!layer) {
      this.resetBoundary(false);
      this.notifyStateChanged();
      return;
    }

    this.drawnLayer = layer;
    this.boundaryShapeType = this.resolveShapeType(layer);
    this.applyBoundaryValidation(layer);
    this.notifyStateChanged();
  }

  private handleDrawDeleted(): void {
    this.resetBoundary(false);
    this.notifyStateChanged();
  }

  private applyBoundaryValidation(layer: any): void {
    this.boundaryAreaKm2 = this.zoneDrawService.calculateAreaKm2(layer);
    this.boundaryValidationMessage = this.validateDrawnLayer(layer);
    this.boundaryConfirmed = !this.boundaryValidationMessage;
    this.styleDrawnLayer(layer, this.boundaryConfirmed);
  }

  private validateDrawnLayer(layer: any): string | null {
    const territoryGeometry = this.context?.territoryBoundaryGeoJson;
    if (!this.context?.canCreate || !territoryGeometry) {
      return this.context?.boundaryRequiredMessage ??
        'Assigned country/territory must have a boundary before adding regions.';
    }

    try {
      const { zone } = this.zoneDrawService.extractZoneResult(layer);
      if (zone.length < 3 || !this.isZoneCoveredByGeometry(zone, territoryGeometry)) {
        return 'Region boundary must be inside the assigned country/territory.';
      }
    } catch {
      return 'Boundary geometry is invalid.';
    }

    return null;
  }

  private renderContextLayers(): void {
    if (!this.map || typeof L === 'undefined') return;

    this.removeFlaggedLayers('_isTerritoryBoundary');
    this.removeFlaggedLayers('_isExistingRegionBoundary');

    const territoryGeometry = this.context?.territoryBoundaryGeoJson;
    if (territoryGeometry) {
      const territoryLayer = L.geoJSON(territoryGeometry, {
        interactive: false,
        style: {
          color: '#1d4ed8',
          weight: 2,
          opacity: 0.95,
          fillColor: '#93c5fd',
          fillOpacity: 0.12,
        },
      });
      territoryLayer._isTerritoryBoundary = true;
      territoryLayer.addTo(this.map);
    }

    for (const region of this.context?.existingRegions ?? []) {
      if (!region.geometryGeoJson) continue;
      const regionLayer = L.geoJSON(region.geometryGeoJson, {
        interactive: false,
        style: {
          color: '#64748b',
          weight: 1.5,
          opacity: 0.9,
          fillColor: '#cbd5e1',
          fillOpacity: 0.2,
          dashArray: '4 4',
        },
      });
      regionLayer._isExistingRegionBoundary = true;
      regionLayer.addTo(this.map);
    }

    this.applyViewport();
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

  private resetBoundary(removeLayer = true): void {
    this.disableActiveDrawHandler();
    this.drawnLayer = null;
    this.boundaryShapeType = null;
    this.boundaryConfirmed = false;
    this.boundaryAreaKm2 = null;
    this.boundaryValidationMessage = null;

    if (removeLayer && this.drawnItems) {
      this.drawnItems.clearLayers();
    }
  }

  private styleDrawnLayer(layer: any, valid: boolean): void {
    if (typeof layer?.setStyle !== 'function') return;

    layer.setStyle(valid
      ? { color: '#2563eb', fillColor: '#60a5fa', fillOpacity: 0.18, weight: 2 }
      : { color: '#dc2626', fillColor: '#fecaca', fillOpacity: 0.3, weight: 2 });
  }

  private isZoneCoveredByGeometry(zone: [number, number][], geometry: TerritoryRegionGeometry): boolean {
    const ring = this.closeRing(zone.map(([latitude, longitude]) => [longitude, latitude] as Position));
    if (ring.length < 4) return false;

    const checkPoints: Position[] = [];
    for (let index = 0; index < ring.length - 1; index += 1) {
      const current = ring[index];
      const next = ring[index + 1];
      checkPoints.push(current);
      checkPoints.push([(current[0] + next[0]) / 2, (current[1] + next[1]) / 2]);
    }

    return this.geometryPolygons(geometry).some(polygon =>
      checkPoints.every(point => this.pointCoveredByPolygon(point, polygon)),
    );
  }

  private geometryPolygons(geometry: TerritoryRegionGeometry): PolygonCoordinates[] {
    if (geometry.type === 'Polygon') {
      return [this.normalizePolygonCoordinates(geometry.coordinates)];
    }

    if (geometry.type === 'MultiPolygon') {
      return this.normalizeMultiPolygonCoordinates(geometry.coordinates);
    }

    return [];
  }

  private normalizeMultiPolygonCoordinates(value: unknown): PolygonCoordinates[] {
    if (!Array.isArray(value)) return [];
    return value
      .map(item => this.normalizePolygonCoordinates(item))
      .filter(polygon => polygon.length > 0);
  }

  private normalizePolygonCoordinates(value: unknown): PolygonCoordinates {
    if (!Array.isArray(value)) return [];
    return value
      .map(ring => this.normalizeRing(ring))
      .filter(ring => ring.length > 0);
  }

  private normalizeRing(value: unknown): LinearRing {
    if (!Array.isArray(value)) return [];
    return value
      .map(position => this.normalizePosition(position))
      .filter((position): position is Position => !!position);
  }

  private normalizePosition(value: unknown): Position | null {
    if (!Array.isArray(value) || value.length < 2) return null;
    const longitude = Number(value[0]);
    const latitude = Number(value[1]);
    return Number.isFinite(longitude) && Number.isFinite(latitude)
      ? [longitude, latitude]
      : null;
  }

  private pointCoveredByPolygon(point: Position, rings: PolygonCoordinates): boolean {
    const exterior = rings[0] ?? [];
    if (!this.pointInRingOrBoundary(point, exterior)) return false;

    return rings.slice(1).every(hole => !this.pointInRingStrict(point, hole));
  }

  private pointInRingOrBoundary(point: Position, ring: LinearRing): boolean {
    if (this.pointOnRingBoundary(point, ring)) return true;
    return this.pointInRingStrict(point, ring);
  }

  private pointInRingStrict(point: Position, ring: LinearRing): boolean {
    let inside = false;
    for (let i = 0, j = ring.length - 1; i < ring.length; j = i, i += 1) {
      const xi = ring[i][0];
      const yi = ring[i][1];
      const xj = ring[j][0];
      const yj = ring[j][1];
      const intersects = yi > point[1] !== yj > point[1] &&
        point[0] < ((xj - xi) * (point[1] - yi)) / (yj - yi) + xi;
      if (intersects) inside = !inside;
    }
    return inside;
  }

  private pointOnRingBoundary(point: Position, ring: LinearRing): boolean {
    for (let index = 0; index < ring.length - 1; index += 1) {
      if (this.pointOnSegment(point, ring[index], ring[index + 1])) {
        return true;
      }
    }
    return false;
  }

  private pointOnSegment(point: Position, start: Position, end: Position): boolean {
    const cross = (point[1] - start[1]) * (end[0] - start[0]) -
      (point[0] - start[0]) * (end[1] - start[1]);
    if (Math.abs(cross) > 1e-9) return false;

    const dot = (point[0] - start[0]) * (end[0] - start[0]) +
      (point[1] - start[1]) * (end[1] - start[1]);
    if (dot < -1e-9) return false;

    const lengthSquared = (end[0] - start[0]) ** 2 + (end[1] - start[1]) ** 2;
    return dot <= lengthSquared + 1e-9;
  }

  private closeRing(points: Position[]): Position[] {
    if (points.length === 0) return points;
    const first = points[0];
    const last = points[points.length - 1];
    if (Math.abs(first[0] - last[0]) < 1e-9 && Math.abs(first[1] - last[1]) < 1e-9) {
      return points;
    }
    return [...points, first];
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

  private firstDrawnLayer(): any {
    let found: any = null;
    this.drawnItems?.eachLayer((layer: any) => {
      if (!found) {
        found = layer;
      }
    });
    return found;
  }

  private resolveShapeType(layer: any): RegionBoundaryShape {
    return typeof L !== 'undefined' && layer instanceof L.Rectangle ? 'Rectangle' : 'Polygon';
  }

  private disableActiveDrawHandler(): void {
    if (this.activeDrawHandler) {
      try {
        this.activeDrawHandler.disable();
      } catch {
        // Leaflet.draw can throw if the handler has already been torn down.
      }
      this.activeDrawHandler = null;
    }
  }

  private handleLoadError(error: unknown, fallback: string): void {
    this.context = null;
    this.renderContextLayers();

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

  private handleSaveError(error: unknown): void {
    if (error instanceof HttpErrorResponse) {
      this.errorMessage = error.error?.message ?? 'Unable to add region.';
      this.notifyStateChanged();
      return;
    }

    this.errorMessage = 'Unable to add region.';
    this.notifyStateChanged();
  }

  private notifyStateChanged(): void {
    this.cdr.markForCheck();
  }
}
