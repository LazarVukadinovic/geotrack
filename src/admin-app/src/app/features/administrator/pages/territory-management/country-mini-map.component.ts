import {
  Component, AfterViewInit, OnDestroy, OnChanges, SimpleChanges,
  Input, ViewChild, ElementRef, ChangeDetectionStrategy, ChangeDetectorRef, inject,
} from '@angular/core';
import { GeoJsonGeometry } from '../../../../core/models/administrator/territory.model';
import { MapTileLayerService } from '../../../../core/services/map-tile-layer.service';

declare const L: any;

@Component({
  selector: 'app-country-mini-map',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<div #mapEl style="width:100%;height:100%;"></div>`,
})
export class CountryMiniMapComponent implements AfterViewInit, OnDestroy, OnChanges {
  @ViewChild('mapEl', { static: true }) mapEl!: ElementRef<HTMLDivElement>;

  @Input() countryCode = '';
  @Input() countryName = '';
  @Input() geometry: GeoJsonGeometry | null = null;

  private map: any;
  private countryLayer: any;
  private readonly mapTileLayer = inject(MapTileLayerService);
  private readonly cdr = inject(ChangeDetectorRef);
  private destroyed = false;

  ngAfterViewInit(): void {
    setTimeout(() => this.initMap(), 50);
  }

  ngOnChanges(changes: SimpleChanges): void {
    if ((changes['countryCode'] || changes['countryName'] || changes['geometry']) && this.map) {
      this.renderCountry();
    }
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.map?.remove();
  }

  private initMap(): void {
    if (this.destroyed) return;
    this.map = L.map(this.mapEl.nativeElement, {
      center: [25, 15],
      zoom: 2,
      zoomControl: false,
      attributionControl: false,
      dragging: false,
      scrollWheelZoom: false,
      doubleClickZoom: false,
      touchZoom: false,
      keyboard: false,
    });

    this.mapTileLayer.addTo(L, this.map, {
      opacity: 0.3,
    });

    this.renderCountry();
  }

  private renderCountry(): void {
    if (!this.map || this.destroyed) return;
    if (this.countryLayer) {
      this.map.removeLayer(this.countryLayer);
      this.countryLayer = null;
    }
    if (!this.geometry) return;

    try {
      this.countryLayer = L.geoJSON({
        type: 'Feature',
        geometry: this.geometry,
        properties: {
          code: this.countryCode,
          name: this.countryName,
        },
      }, {
        style: { color: '#3b82f6', weight: 2, fillColor: '#3b82f6', fillOpacity: 0.35 },
      }).addTo(this.map);
      try { this.map.fitBounds(this.countryLayer.getBounds(), { padding: [20, 20] }); } catch { /* ignore */ }
    } catch { /* ignore invalid geometry */ }
  }
}
