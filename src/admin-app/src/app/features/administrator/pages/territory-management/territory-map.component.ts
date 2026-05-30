import {
  Component, OnDestroy, AfterViewInit,
  Input, Output, EventEmitter, ElementRef, ViewChild,
  ChangeDetectionStrategy, ChangeDetectorRef, inject,
} from '@angular/core';
import { GeoJsonFeature } from '../../../../core/models/administrator/territory.model';
import { MapTileLayerService } from '../../../../core/services/map-tile-layer.service';

declare const L: any;

@Component({
  selector: 'app-territory-map',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex flex-col h-full">
      <div #mapEl style="height:100%;width:100%;border-radius:0.75rem;overflow:hidden;"></div>
      @if (!readonly) {
        <div class="flex items-center gap-3 mt-3 min-h-[1.75rem]">
          @if (isLoadingBounds) {
            <p class="text-xs text-gray-400 flex-1">Loading country boundaries…</p>
          } @else if (outOfBoundsWarning) {
            <p class="text-xs text-red-500 flex-1">
              The zone must be within the selected countries. Please draw again inside the highlighted area.
            </p>
          } @else {
            <p class="text-xs text-gray-500 flex-1">
              @if (hasZone) {
                <span class="text-emerald-600 font-medium">&#10003; Zone drawn</span>
                &mdash; draw a new one to replace it, or delete it with the icon on the right
              } @else {
                Use the drawing tools on the map (left side) to define territory boundaries
              }
            </p>
            @if (hasZone) {
              <button
                (click)="clearZone()"
                class="text-xs text-red-500 hover:text-red-700 border-0 bg-transparent cursor-pointer font-medium whitespace-nowrap"
              >
                Delete zone
              </button>
            }
          }
        </div>
      }
    </div>
  `,
})
export class TerritoryMapComponent implements AfterViewInit, OnDestroy {
  @ViewChild('mapEl', { static: true }) mapEl!: ElementRef<HTMLDivElement>;

  @Input() initialZone: GeoJsonFeature | null = null;
  @Input() readonly = false;
  @Input() allowedCountryNames: string[] = [];

  @Output() zoneChange = new EventEmitter<GeoJsonFeature | null>();

  private readonly cdr = inject(ChangeDetectorRef);
  private readonly mapTileLayer = inject(MapTileLayerService);

  private map: any;
  private drawnItems: any;
  private countryOutlineLayer: any;
  private allowedPolygons: any[] = [];
  private destroyed = false;

  hasZone = false;
  isLoadingBounds = false;
  outOfBoundsWarning = false;

  ngAfterViewInit(): void {
    setTimeout(() => { this.initMap(); }, 50);
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.map?.remove();
    this.map = undefined;
  }

  private async initMap(): Promise<void> {
    if (this.destroyed) return;
    const el = this.mapEl.nativeElement;

    this.map = L.map(el, {
      center: [25, 15],
      zoom: 2,
      zoomControl: true,
    });

    this.mapTileLayer.addTo(L, this.map);

    this.drawnItems = new L.FeatureGroup();
    this.map.addLayer(this.drawnItems);

    if (this.initialZone) {
      this.renderZone(this.initialZone);
    }

    if (!this.readonly) {
      const drawControl = new L.Control.Draw({
        position: 'topleft',
        draw: {
          polygon: {
            allowIntersection: false,
            showArea: true,
            shapeOptions: { color: '#3b82f6', fillColor: '#3b82f6', fillOpacity: 0.25, weight: 2 },
          },
          rectangle: {
            shapeOptions: { color: '#3b82f6', fillColor: '#3b82f6', fillOpacity: 0.25, weight: 2 },
          },
          polyline: false,
          circle: false,
          circlemarker: false,
          marker: false,
        },
        edit: {
          featureGroup: this.drawnItems,
          remove: true,
        },
      });
      this.map.addControl(drawControl);

      this.map.on('draw:created', (e: any) => {
        this.drawnItems.clearLayers();
        this.drawnItems.addLayer(e.layer);

        if (this.allowedPolygons.length > 0) {
          const center = e.layer.getBounds().getCenter();
          if (!this.isPointInAllowedArea(center.lat, center.lng)) {
            this.drawnItems.clearLayers();
            this.outOfBoundsWarning = true;
            this.hasZone = false;
            this.zoneChange.emit(null);
            this.cdr.markForCheck();
            return;
          }
        }

        this.outOfBoundsWarning = false;
        this.hasZone = true;
        this.zoneChange.emit(e.layer.toGeoJSON() as GeoJsonFeature);
        this.cdr.markForCheck();
      });

      this.map.on('draw:deleted', () => {
        if (this.drawnItems.getLayers().length === 0) {
          this.hasZone = false;
          this.zoneChange.emit(null);
          this.cdr.markForCheck();
        }
      });

      this.map.on('draw:edited', (e: any) => {
        e.layers.eachLayer((layer: any) => {
          this.zoneChange.emit(layer.toGeoJSON() as GeoJsonFeature);
        });
      });

      if (this.allowedCountryNames.length > 0) {
        await this.loadAndFitCountries();
      }
    }
  }

  private async loadAndFitCountries(): Promise<void> {
    if (this.destroyed || !this.map) return;

    this.isLoadingBounds = true;
    this.cdr.markForCheck();

    this.allowedPolygons = [];
    this.countryOutlineLayer = L.featureGroup().addTo(this.map);

    let unionBounds: any = null;

    for (let i = 0; i < this.allowedCountryNames.length; i++) {
      if (this.destroyed) return;
      if (i > 0) {
        await new Promise(resolve => setTimeout(resolve, 350));
      }

      try {
        const name = encodeURIComponent(this.allowedCountryNames[i]);
        const resp = await fetch(
          `https://nominatim.openstreetmap.org/search?q=${name}&format=geojson&polygon_geojson=1&limit=1`,
          { headers: { 'User-Agent': 'GeotrackAdminApp/1.0' } },
        );
        const data = await resp.json();

        if (data.features?.length > 0) {
          const feature = data.features[0];
          const geomType = feature.geometry?.type;
          if (geomType === 'Polygon' || geomType === 'MultiPolygon') {
            this.allowedPolygons.push(feature.geometry);

            const layer = L.geoJSON(feature, {
              style: {
                color: '#3b82f6',
                weight: 1.5,
                fillColor: '#3b82f6',
                fillOpacity: 0.07,
                dashArray: '5 4',
              },
            });
            this.countryOutlineLayer.addLayer(layer);

            try {
              const b = layer.getBounds();
              unionBounds = unionBounds ? unionBounds.extend(b) : b;
            } catch { /* skip */ }
          }
        }
      } catch { /* skip failed fetches */ }
    }

    if (this.destroyed || !this.map) return;

    if (unionBounds) {
      this.map.fitBounds(unionBounds, { padding: [30, 30] });
    }

    this.isLoadingBounds = false;
    this.cdr.markForCheck();
  }

  private isPointInAllowedArea(lat: number, lng: number): boolean {
    return this.allowedPolygons.some(geom => this.pointInGeometry(lat, lng, geom));
  }

  private pointInGeometry(lat: number, lng: number, geom: any): boolean {
    if (geom.type === 'Polygon') {
      return this.pointInRing(lat, lng, geom.coordinates[0]);
    }
    if (geom.type === 'MultiPolygon') {
      return geom.coordinates.some((poly: number[][][]) => this.pointInRing(lat, lng, poly[0]));
    }
    return false;
  }

  // Ray-casting algorithm — GeoJSON ring coords are [lng, lat]
  private pointInRing(lat: number, lng: number, ring: number[][]): boolean {
    let inside = false;
    for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) {
      const xi = ring[i][0], yi = ring[i][1];
      const xj = ring[j][0], yj = ring[j][1];
      if (((yi > lat) !== (yj > lat)) && (lng < (xj - xi) * (lat - yi) / (yj - yi) + xi)) {
        inside = !inside;
      }
    }
    return inside;
  }

  private renderZone(zone: GeoJsonFeature): void {
    const layer = L.geoJSON(zone, {
      style: { color: '#3b82f6', fillColor: '#3b82f6', fillOpacity: 0.25, weight: 2 },
    });
    layer.addTo(this.drawnItems);
    try { this.map.fitBounds(layer.getBounds(), { padding: [30, 30] }); } catch { /* ignore */ }
    this.hasZone = true;
  }

  clearZone(): void {
    this.drawnItems?.clearLayers();
    this.hasZone = false;
    this.outOfBoundsWarning = false;
    this.zoneChange.emit(null);
    this.cdr.markForCheck();
  }
}
