import {
  Component, AfterViewInit, OnDestroy, OnChanges, SimpleChanges,
  Input, Output, EventEmitter, ViewChild, ElementRef,
  ChangeDetectionStrategy, ChangeDetectorRef, NgZone, inject,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  GeoJsonGeometry,
  TerritoryMapFeature,
  TerritoryMapFeatureCollection,
  TerritoryMapFeatureProperties,
  TerritoryMapStatus,
} from '../../../../core/models/administrator/territory.model';

declare const L: any;

export interface CountryMapAssignment {
  managerId: string;
  managerName: string;
  managerColor: string;
}

export interface CountryMapClick {
  code: string;
  iso2: string | null;
  iso3: string | null;
  name: string;
  territoryKind: string;
  shapeGroup: string | null;
  shapeType: string | null;
  shapeId: string | null;
  territoryId: number | null;
  status: TerritoryMapStatus;
  canAssign: boolean;
  hasBoundary: boolean;
  hasValidationBoundary: boolean;
  hasDisplayBoundary: boolean;
  boundaryRequiredMessage: string | null;
  boundaryMissingReason: string | null;
  geometry: GeoJsonGeometry | null;
}

@Component({
  selector: 'app-territory-world-map',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div style="position:relative;width:100%;height:100%;">
      <div #mapEl style="width:100%;height:100%;"></div>
      @if (isLoading) {
        <div style="position:absolute;inset:0;display:flex;align-items:center;justify-content:center;background:rgba(249,250,251,0.85);z-index:500;">
          <div class="flex flex-col items-center gap-2">
            <svg class="w-6 h-6 text-blue-400 animate-spin" viewBox="0 0 24 24" fill="none">
              <circle cx="12" cy="12" r="10" stroke="currentColor" stroke-width="2" stroke-dasharray="31.4" stroke-dashoffset="10"/>
            </svg>
            <p class="text-sm text-gray-500">Loading stored ADM0 boundaries...</p>
          </div>
        </div>
      }
      @if (!isLoading && loadFailed) {
        <div style="position:absolute;inset:0;display:flex;align-items:center;justify-content:center;background:rgba(249,250,251,0.9);z-index:500;">
          <div class="flex flex-col items-center gap-2 text-center px-6">
            <svg class="w-8 h-8 text-gray-300" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
              <circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/>
            </svg>
            <p class="text-sm font-medium text-gray-500">Map data could not be loaded</p>
            <p class="text-xs text-gray-400">Stored country boundaries are unavailable right now</p>
            <button
              type="button"
              class="mt-1 px-3 py-1.5 rounded-lg border border-gray-200 bg-white text-xs font-semibold text-gray-600 hover:bg-gray-50"
              (click)="retryLoad.emit()"
            >
              Retry
            </button>
          </div>
        </div>
      }
    </div>
  `,
})
export class TerritoryWorldMapComponent implements AfterViewInit, OnDestroy, OnChanges {
  @ViewChild('mapEl', { static: true }) mapEl!: ElementRef<HTMLDivElement>;

  @Input() featureCollection: TerritoryMapFeatureCollection | null = null;
  @Input() assignments: Map<string, CountryMapAssignment> = new Map();
  @Input() pickerMode = false;
  @Input() selectedCode = '';
  @Input() unavailableCodes: string[] = [];
  @Input() highlightCode = '';
  @Input() isLoading = false;
  @Input() loadFailed = false;
  @Output() countryClick = new EventEmitter<CountryMapClick>();
  @Output() retryLoad = new EventEmitter<void>();

  private map: any;
  private geoLayer: any;
  private renderFrame: number | null = null;
  private geometrySignature = '';
  private unavailableSet = new Set<string>();
  private readonly cdr  = inject(ChangeDetectorRef);
  private readonly zone = inject(NgZone);

  ngAfterViewInit(): void {
    setTimeout(() => this.initMap(), 50);
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['unavailableCodes']) {
      this.unavailableSet = new Set(this.unavailableCodes);
    }
    if (this.map && changes['featureCollection']) {
      this.applyFeatureCollectionChange();
    }
    if (this.geoLayer && (changes['assignments'] || changes['selectedCode'] || changes['unavailableCodes'])) {
      this.refreshStyles();
    }
    if (changes['highlightCode'] && this.highlightCode && this.geoLayer) {
      this.zoomToCountry(this.highlightCode);
    }
  }

  ngOnDestroy(): void {
    if (this.renderFrame !== null) {
      cancelAnimationFrame(this.renderFrame);
      this.renderFrame = null;
    }
    this.map?.remove();
  }

  private initMap(): void {
    this.zone.runOutsideAngular(() => {
      this.map = L.map(this.mapEl.nativeElement, {
        center: [25, 10],
        zoom: 2,
        minZoom: 1,
        maxZoom: 7,
        zoomControl: true,
        worldCopyJump: false,
        maxBounds: [[-85, -220], [85, 220]],
        maxBoundsViscosity: 0.65,
        preferCanvas: true,
      });

      L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '&copy; OpenStreetMap contributors',
        opacity: 0.35,
        noWrap: false,
      }).addTo(this.map);

      this.renderLayer();
    });
  }

  private renderLayer(): void {
    if (!this.map) return;

    if (this.renderFrame !== null) {
      cancelAnimationFrame(this.renderFrame);
    }

    this.renderFrame = requestAnimationFrame(() => {
      this.renderFrame = null;
      this.renderLayerNow();
    });
  }

  private renderLayerNow(): void {
    if (!this.map) return;

    if (this.geoLayer) {
      this.map.removeLayer(this.geoLayer);
      this.geoLayer = null;
    }

    const features = (this.featureCollection?.features ?? [])
      .filter((feature): feature is TerritoryMapFeature => !!feature.geometry);

    const geojson = {
      type: 'FeatureCollection',
      features: features.map(feature => ({
        type: 'Feature',
        geometry: feature.geometry,
        properties: feature.properties,
      })),
    };

    this.geoLayer = L.geoJSON(geojson, {
      renderer: typeof L.canvas === 'function' ? L.canvas({ padding: 0.35 }) : undefined,
      noClip: true,
      fillRule: 'nonzero',
      style: (feature: any) => this.styleFor(feature.properties as TerritoryMapFeatureProperties),
      onEachFeature: (feature: any, layer: any) => {
        this.bindLayer(layer, feature.properties as TerritoryMapFeatureProperties, feature.geometry ?? null);
      },
    }).addTo(this.map);
    this.geometrySignature = this.buildGeometrySignature(features);

    this.cdr.markForCheck();
  }

  private applyFeatureCollectionChange(): void {
    const features = (this.featureCollection?.features ?? [])
      .filter((feature): feature is TerritoryMapFeature => !!feature.geometry);
    const nextSignature = this.buildGeometrySignature(features);
    if (this.geoLayer && nextSignature === this.geometrySignature) {
      this.updateLayerProperties(features);
      this.refreshStyles();
      return;
    }

    this.renderLayer();
  }

  private updateLayerProperties(features: TerritoryMapFeature[]): void {
    if (!this.geoLayer) return;
    const propertiesByKey = new Map<string, TerritoryMapFeatureProperties>();
    for (const feature of features) {
      const key = this.featureKey(feature.properties);
      if (key) {
        propertiesByKey.set(key, feature.properties);
      }
    }

    this.geoLayer.eachLayer((layer: any) => {
      const props = layer.feature?.properties as TerritoryMapFeatureProperties | undefined;
      const key = props ? this.featureKey(props) : '';
      const nextProps = key ? propertiesByKey.get(key) : undefined;
      if (nextProps && layer.feature) {
        layer.feature.properties = nextProps;
      }
    });
  }

  private buildGeometrySignature(features: TerritoryMapFeature[]): string {
    return features
      .map(feature => {
        const key = this.featureKey(feature.properties);
        const geometry = feature.geometry;
        return `${key}:${geometry?.type ?? 'null'}:${this.coordinateCount(geometry?.coordinates)}`;
      })
      .sort()
      .join('|');
  }

  private coordinateCount(value: unknown): number {
    if (!Array.isArray(value)) {
      return 0;
    }

    if (typeof value[0] === 'number') {
      return 1;
    }

    return value.reduce((sum, child) => sum + this.coordinateCount(child), 0);
  }

  private styleFor(props: TerritoryMapFeatureProperties): any {
    const code = this.featureCode(props);
    if (this.isDisputed(props)) {
      if (props.status === 'DisputedAssigned') {
        return { fillColor: '#7c3aed', fillOpacity: 0.5, color: '#5b21b6', weight: 1.1, dashArray: '5 3' };
      }
      return { fillColor: '#f59e0b', fillOpacity: 0.38, color: '#b45309', weight: 1.1, dashArray: '5 3' };
    }

    if (!code) {
      return { fillColor: '#e5e7eb', fillOpacity: 0.25, color: '#d1d5db', weight: 0.5 };
    }

    if (this.pickerMode) {
      if (code === this.selectedCode) {
        return { fillColor: '#10b981', fillOpacity: 0.65, color: '#ffffff', weight: 1 };
      }
      if (this.unavailableSet.has(code)) {
        return { fillColor: '#d1d5db', fillOpacity: 0.45, color: '#9ca3af', weight: 0.5 };
      }
      return { fillColor: '#cbd5e1', fillOpacity: 0.45, color: '#94a3b8', weight: 0.5 };
    }

    const status = this.effectiveStatus(props);
    if (status === 'Assigned') {
      return { fillColor: '#3b82f6', fillOpacity: 0.58, color: '#1d4ed8', weight: 0.9 };
    }
    if (status === 'MissingBoundary') {
      return { fillColor: '#e5e7eb', fillOpacity: 0.3, color: '#9ca3af', weight: 0.7, dashArray: '4 4' };
    }
    if (status === 'MissingDisplayGeometry') {
      return { fillColor: '#fef3c7', fillOpacity: 0.45, color: '#d97706', weight: 0.9, dashArray: '2 4' };
    }
    return { fillColor: '#bbf7d0', fillOpacity: 0.42, color: '#15803d', weight: 0.75 };
  }

  private bindLayer(layer: any, props: TerritoryMapFeatureProperties, geometry: GeoJsonGeometry | null): void {
    const code = this.featureCode(props);
    const key = this.featureKey(props);
    if (!key) return;

    const name = props.displayName || props.name || code || 'Boundary feature';
    const status = this.effectiveStatus(props);

    if (this.pickerMode) {
      if (this.isDisputed(props) || !code) {
        layer.bindTooltip(
          `<strong>${name}</strong><br><span style="color:#b45309;font-size:11px;">Disputed territory - view-only</span>`,
          { sticky: true },
        );
        return;
      }

      const isUnavailable = this.unavailableSet.has(code);
      if (isUnavailable) {
        layer.bindTooltip(
          `<strong>${name}</strong><br><span style="color:#6b7280;font-size:11px;">Assigned elsewhere</span>`,
          { sticky: true },
        );
      } else {
        layer.bindTooltip(
          code === this.selectedCode
            ? `<strong>${name}</strong><br><span style="color:#10b981;font-size:11px;">Selected</span>`
            : `<strong>${name}</strong><br><span style="color:#6b7280;font-size:11px;">Click to select</span>`,
          { sticky: true },
        );
        layer.on('click', () => this.emitCountryClick(props, geometry));
        layer.on('mouseover', (e: any) => {
          if (code !== this.selectedCode) e.target.setStyle({ fillColor: '#60a5fa', fillOpacity: 0.6 });
        });
        layer.on('mouseout', (e: any) => e.target.setStyle(this.styleFor(props)));
      }
      return;
    }

    if (this.isDisputed(props)) {
      const assignability = props.canAssign
        ? 'Click to view assignment options'
        : 'View-only - not assignable as a country territory';
      layer.bindTooltip(
        `<strong>${name}</strong><br><span style="color:#b45309;font-size:11px;">Disputed territory</span><br><span style="color:#6b7280;font-size:11px;">shapeType: ${props.shapeType || props.boundaryAdmLevel || 'DISP'} - ${assignability}</span>`,
        { sticky: true },
      );
      layer.on('click', () => this.emitCountryClick(props, geometry));
      layer.on('mouseover', (e: any) => e.target.setStyle({ fillColor: '#fbbf24', fillOpacity: 0.52, color: '#92400e' }));
      layer.on('mouseout', (e: any) => e.target.setStyle(this.styleFor(props)));
      return;
    }

    if (status === 'MissingBoundary' || status === 'MissingDisplayGeometry') {
      layer.bindTooltip(
        `<strong>${name}</strong><br><span style="color:#b45309;font-size:11px;">${status === 'MissingDisplayGeometry' ? 'Display geometry must be rebuilt.' : 'Boundary missing'}</span>`,
        { sticky: true },
      );
      return;
    }

    if (status === 'Assigned') {
      const manager = this.assignments.get(code)?.managerName || props.assignedManager?.displayName || 'Assigned manager';
      layer.bindTooltip(
        `<strong>${name}</strong><br><span style="color:#6b7280;font-size:11px;">Assigned to ${manager} - click to view</span>`,
        { sticky: true },
      );
      layer.on('click', () => this.emitCountryClick(props, geometry));
      layer.on('mouseover', (e: any) => e.target.setStyle({ fillColor: '#2563eb', fillOpacity: 0.7 }));
      layer.on('mouseout', (e: any) => e.target.setStyle(this.styleFor(props)));
      return;
    }

    layer.bindTooltip(
      `<strong>${name}</strong><br><span style="color:#6b7280;font-size:11px;">Available - click to assign</span>`,
      { sticky: true },
    );
    layer.on('click', () => this.emitCountryClick(props, geometry));
    layer.on('mouseover', (e: any) => e.target.setStyle({ fillColor: '#86efac', fillOpacity: 0.58, color: '#166534' }));
    layer.on('mouseout', (e: any) => e.target.setStyle(this.styleFor(props)));
  }

  private refreshStyles(): void {
    if (!this.geoLayer) return;
    this.zone.runOutsideAngular(() => {
      this.geoLayer.eachLayer((layer: any) => {
        const props = layer.feature?.properties as TerritoryMapFeatureProperties | undefined;
        if (!props) return;
        layer.setStyle(this.styleFor(props));
        layer.off('click').off('mouseover').off('mouseout');
        layer.unbindTooltip();
        this.bindLayer(layer, props, layer.feature?.geometry ?? null);
      });
    });
  }

  zoomToCountry(code: string): void {
    if (!this.geoLayer || !this.map) return;
    this.geoLayer.eachLayer((layer: any) => {
      const props = layer.feature?.properties as TerritoryMapFeatureProperties | undefined;
      if (!props) return;
      if (this.featureCode(props) === code || props.iso3 === code || this.featureKey(props) === code) {
        try {
          this.map.fitBounds(layer.getBounds(), { padding: [40, 40], maxZoom: 6 });
        } catch { /* ignore */ }
      }
    });
  }

  private emitCountryClick(props: TerritoryMapFeatureProperties, geometry: GeoJsonGeometry | null): void {
    const code = this.featureCode(props) || this.featureKey(props);
    if (!code) return;

    this.zone.run(() => this.countryClick.emit({
      code,
      iso2: props.iso2 ?? props.countryCode ?? null,
      iso3: props.iso3 ?? null,
      name: props.displayName || props.name || code,
      territoryKind: props.territoryKind ?? 'Country',
      shapeGroup: props.shapeGroup ?? props.boundaryShapeGroup ?? null,
      shapeType: props.shapeType ?? props.boundaryShapeType ?? props.boundaryAdmLevel ?? null,
      shapeId: props.shapeId ?? props.boundaryShapeId ?? null,
      territoryId: props.territoryId ?? null,
      status: this.effectiveStatus(props),
      canAssign: props.canAssign,
      hasBoundary: props.hasBoundary,
      hasValidationBoundary: props.hasValidationBoundary ?? props.hasBoundary,
      hasDisplayBoundary: props.hasDisplayBoundary ?? !!geometry,
      boundaryRequiredMessage: props.boundaryRequiredMessage ?? null,
      boundaryMissingReason: props.boundaryMissingReason ?? props.boundaryRequiredMessage ?? null,
      geometry,
    }));
  }

  private effectiveStatus(props: TerritoryMapFeatureProperties): TerritoryMapStatus {
    if (this.isDisputed(props)) {
      return props.status;
    }

    const code = this.featureCode(props);
    if (code && this.assignments.has(code)) {
      return 'Assigned';
    }
    return props.status;
  }

  private featureCode(props: TerritoryMapFeatureProperties): string {
    return props.iso2 || props.countryCode || props.iso3 || '';
  }

  private featureKey(props: TerritoryMapFeatureProperties): string {
    return this.featureCode(props) ||
      props.boundaryExternalKey ||
      props.shapeId ||
      props.shapeGroup ||
      (props.territoryId ? `territory-${props.territoryId}` : '');
  }

  private isDisputed(props: TerritoryMapFeatureProperties): boolean {
    return props.territoryKind === 'DisputedTerritory' ||
      props.status === 'DisputedViewOnly' ||
      props.status === 'DisputedAssigned' ||
      props.shapeType === 'DISP' ||
      props.boundaryAdmLevel === 'DISP';
  }
}
