import {
  Component, AfterViewInit, OnDestroy, OnChanges, SimpleChanges,
  Input, ViewChild, ElementRef, ChangeDetectionStrategy, ChangeDetectorRef, inject,
} from '@angular/core';

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

  private map: any;
  private countryLayer: any;
  private readonly cdr = inject(ChangeDetectorRef);
  private destroyed = false;

  ngAfterViewInit(): void {
    setTimeout(() => this.initMap(), 50);
  }

  ngOnChanges(changes: SimpleChanges): void {
    if ((changes['countryCode'] || changes['countryName']) && this.map && !changes['countryCode']?.firstChange) {
      this.loadCountry();
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

    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      opacity: 0.3,
    }).addTo(this.map);

    if (this.countryCode) this.loadCountry();
  }

  private async loadCountry(): Promise<void> {
    if (!this.map || this.destroyed) return;
    if (this.countryLayer) {
      this.map.removeLayer(this.countryLayer);
      this.countryLayer = null;
    }
    try {
      const q = encodeURIComponent(this.countryName || this.countryCode);
      const resp = await fetch(
        `https://nominatim.openstreetmap.org/search?q=${q}&format=geojson&polygon_geojson=1&limit=1`,
        { headers: { 'User-Agent': 'GeotrackAdminApp/1.0' } },
      );
      const data = await resp.json();
      if (this.destroyed || !this.map) return;
      if (data.features?.length > 0) {
        this.countryLayer = L.geoJSON(data.features[0], {
          style: { color: '#3b82f6', weight: 2, fillColor: '#3b82f6', fillOpacity: 0.35 },
        }).addTo(this.map);
        try { this.map.fitBounds(this.countryLayer.getBounds(), { padding: [20, 20] }); } catch { /* ignore */ }
      }
    } catch { /* ignore network errors */ }
  }
}
