import {
  Component, AfterViewInit, OnDestroy, OnChanges, SimpleChanges,
  Input, Output, EventEmitter, ViewChild, ElementRef,
  ChangeDetectionStrategy, ChangeDetectorRef, NgZone, inject,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ALL_COUNTRIES } from '../../../../core/services/administrator/territory.service';
import { GeoJsonGeometry } from '../../../../core/models/administrator/territory.model';

declare const L: any;

const NAME_TO_CODE = new Map<string, string>(
  ALL_COUNTRIES.map(c => [c.name.toLowerCase(), c.code])
);

export interface CountryMapAssignment {
  managerId: string;
  managerName: string;
  managerColor: string;
}

export interface CountryMapClick {
  code: string;
  name: string;
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
            <p class="text-sm text-gray-500">Loading world map…</p>
          </div>
        </div>
      }
      @if (!isLoading && loadFailed) {
        <div style="position:absolute;inset:0;display:flex;align-items:center;justify-content:center;background:rgba(249,250,251,0.9);z-index:500;">
          <div class="flex flex-col items-center gap-2 text-center px-6">
            <svg class="w-8 h-8 text-gray-300" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
              <circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/>
            </svg>
            <p class="text-sm font-medium text-gray-500">Map could not be loaded</p>
            <p class="text-xs text-gray-400">Check your internet connection and reload the page</p>
          </div>
        </div>
      }
    </div>
  `,
})
export class TerritoryWorldMapComponent implements AfterViewInit, OnDestroy, OnChanges {
  @ViewChild('mapEl', { static: true }) mapEl!: ElementRef<HTMLDivElement>;

  @Input() assignments: Map<string, CountryMapAssignment> = new Map();
  @Input() pickerMode = false;
  @Input() selectedCode = '';
  @Input() unavailableCodes: string[] = [];
  @Input() highlightCode = '';
  @Output() countryClick = new EventEmitter<CountryMapClick>();

  isLoading = true;
  loadFailed = false;
  private map: any;
  private geoLayer: any;
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
    if (this.geoLayer && (changes['assignments'] || changes['selectedCode'] || changes['unavailableCodes'])) {
      this.refreshStyles();
    }
    if (changes['highlightCode'] && this.highlightCode && this.geoLayer) {
      this.zoomToCountry(this.highlightCode);
    }
  }

  ngOnDestroy(): void {
    this.map?.remove();
  }

  private initMap(): void {
    this.map = L.map(this.mapEl.nativeElement, {
      center: [25, 10],
      zoom: 2,
      minZoom: 1,
      maxZoom: 7,
      zoomControl: true,
      worldCopyJump: true,
    });

    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      attribution: '© OpenStreetMap contributors',
      opacity: 0.35,
    }).addTo(this.map);

    this.fetchCountries();
  }

  private async fetchCountries(): Promise<void> {
    const urls = [
      // naturalearth 110m via jsDelivr (~900 KB, brzo)
      'https://cdn.jsdelivr.net/gh/nvkelso/natural-earth-vector@v5.1.2/geojson/ne_110m_admin_0_countries.geojson',
      // originalni geo-countries via jsDelivr (fallback, ~22 MB)
      'https://cdn.jsdelivr.net/gh/datasets/geo-countries@0.1.0/data/countries.geojson',
    ];

    let loaded = false;
    for (const url of urls) {
      try {
        const res = await fetch(url);
        if (!res.ok) continue;
        const data = await res.json();
        this.renderLayer(data);
        loaded = true;
        break;
      } catch {
        // probaj sledeći URL
      }
    }

    if (!loaded) this.loadFailed = true;
    this.isLoading = false;
    this.cdr.detectChanges();
  }

  private extractCode(props: any): string {
    const iso2: string = props.ISO_A2;
    if (iso2 && iso2 !== '-99' && iso2 !== '-1') return iso2;
    const eh: string = props.ISO_A2_EH;
    if (eh && eh !== '-99' && eh !== '-1') return eh;
    const name: string = (props.ADMIN ?? props.NAME ?? '').toLowerCase();
    if (name) {
      const byName = NAME_TO_CODE.get(name);
      if (byName) return byName;
    }
    return '-99';
  }

  private renderLayer(geojson: any): void {
    this.geoLayer = L.geoJSON(geojson, {
      style: (feature: any) => this.styleFor(this.extractCode(feature.properties)),
      onEachFeature: (feature: any, layer: any) => {
        const code: string = this.extractCode(feature.properties);
        const name: string = feature.properties.ADMIN ?? feature.properties.NAME ?? code;
        this.bindLayer(layer, code, name);
      },
    }).addTo(this.map);
  }

  private styleFor(code: string): any {
    if (code === '-99' || code === '-1') {
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
    const a = this.assignments.get(code);
    if (a) {
      return { fillColor: '#3b82f6', fillOpacity: 0.55, color: '#ffffff', weight: 0.8 };
    }
    return { fillColor: '#cbd5e1', fillOpacity: 0.45, color: '#94a3b8', weight: 0.5 };
  }

  private bindLayer(layer: any, code: string, name: string): void {
    if (code === '-99' || code === '-1') return;

    if (this.pickerMode) {
      const isUnavailable = this.unavailableSet.has(code);
      if (isUnavailable) {
        layer.bindTooltip(
          `<strong>${name}</strong><br><span style="color:#6b7280;font-size:11px;">Assigned elsewhere</span>`,
          { sticky: true }
        );
      } else {
        layer.bindTooltip(
          code === this.selectedCode
            ? `<strong>${name}</strong><br><span style="color:#10b981;font-size:11px;">Selected</span>`
            : `<strong>${name}</strong><br><span style="color:#6b7280;font-size:11px;">Click to select</span>`,
          { sticky: true }
        );
        layer.on('click', () => this.emitCountryClick(code, name, layer));
        layer.on('mouseover', (e: any) => {
          if (code !== this.selectedCode) e.target.setStyle({ fillColor: '#60a5fa', fillOpacity: 0.6 });
        });
        layer.on('mouseout', (e: any) => {
          e.target.setStyle(this.styleFor(code));
        });
      }
      return;
    }

    const a = this.assignments.get(code);
    if (a) {
      layer.bindTooltip(
        `<strong>${name}</strong><br><span style="color:#6b7280;font-size:11px;">Assigned — click to view</span>`,
        { sticky: true }
      );
      layer.on('click', () => this.emitCountryClick(code, name, layer));
      layer.on('mouseover', (e: any) => {
        e.target.setStyle({ fillColor: '#2563eb', fillOpacity: 0.7 });
      });
      layer.on('mouseout', (e: any) => {
        e.target.setStyle(this.styleFor(code));
      });
    } else {
      layer.bindTooltip(
        `<strong>${name}</strong><br><span style="color:#6b7280;font-size:11px;">Available — click to assign</span>`,
        { sticky: true }
      );
      layer.on('click', () => this.emitCountryClick(code, name, layer));
      layer.on('mouseover', (e: any) => {
        e.target.setStyle({ fillColor: '#60a5fa', fillOpacity: 0.6 });
      });
      layer.on('mouseout', (e: any) => {
        e.target.setStyle(this.styleFor(code));
      });
    }
  }

  private refreshStyles(): void {
    if (!this.geoLayer) return;
    this.geoLayer.eachLayer((layer: any) => {
      const props = layer.feature?.properties;
      if (!props) return;
      const code: string = this.extractCode(props);
      const name: string = props.ADMIN ?? props.NAME ?? code;
      layer.setStyle(this.styleFor(code));
      layer.off('click').off('mouseover').off('mouseout');
      layer.unbindTooltip();
      this.bindLayer(layer, code, name);
    });
  }

  zoomToCountry(code: string): void {
    if (!this.geoLayer || !this.map) return;
    this.geoLayer.eachLayer((layer: any) => {
      const props = layer.feature?.properties;
      if (!props) return;
      if (this.extractCode(props) === code) {
        try {
          this.map.fitBounds(layer.getBounds(), { padding: [40, 40], maxZoom: 6 });
        } catch { /* ignore */ }
      }
    });
  }

  private emitCountryClick(code: string, name: string, layer: any): void {
    const rawGeometry = layer.feature?.geometry;
    const geometry = rawGeometry?.type === 'Polygon' || rawGeometry?.type === 'MultiPolygon'
      ? {
          type: rawGeometry.type,
          coordinates: rawGeometry.coordinates,
        } as GeoJsonGeometry
      : null;

    this.zone.run(() => this.countryClick.emit({ code, name, geometry }));
  }
}
