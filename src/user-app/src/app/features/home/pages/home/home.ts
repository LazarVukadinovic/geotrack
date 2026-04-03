import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import maplibregl, { Map } from 'maplibre-gl';

const MAP_STYLE_URL = '/map-style.json';

@Component({
  selector: 'app-home',
  imports: [CommonModule, FormsModule],
  templateUrl: './home.html',
  styleUrl: './home.css',
})
export class Home implements OnInit, OnDestroy {
  private bgMap!: Map;
  private searchTimeout?: ReturnType<typeof setTimeout>;

  searchQuery = '';
  searchResults: any[] = [];

  constructor(private router: Router) {}

  ngOnInit(): void {
    this.initBgMap();
  }

  ngOnDestroy(): void {
    this.bgMap?.remove();
  }

  private initBgMap(): void {
    this.bgMap = new maplibregl.Map({
      container: 'bg-map',
      style: MAP_STYLE_URL,
      center: [14.0, 20.0],
      zoom: 1.8,    
      pitch: 30,
      bearing: 0,
      interactive: false,
      attributionControl: false,
    });

    this.bgMap.on('load', () => {
      const logo = document.querySelector('.maplibregl-ctrl-logo') as HTMLElement;
      const attrib = document.querySelector('.maplibregl-ctrl-attrib') as HTMLElement;

      if (logo) logo.style.display = 'none';
      if (attrib) attrib.style.display = 'none';
      const layers = this.bgMap.getStyle().layers;

      layers?.forEach((layer) => {
        if (layer.type === 'symbol') {
          this.bgMap.setLayoutProperty(layer.id, 'visibility', 'none');
        }
      });
      this.startCinematicLoop();
    });
  }

  private startCinematicLoop(): void {
    let lng = 14.0;

    const animate = () => {
      lng += 0.10; // brzina rotacije

      if (lng > 180) lng = -180;

      this.bgMap.setCenter([lng, 20.0]);

      requestAnimationFrame(animate);
    };

    animate();
  }

  onSearch(value: string): void {
    clearTimeout(this.searchTimeout);
    if (value.trim().length < 2) {
      this.searchResults = [];
      return;
    }
    this.searchTimeout = setTimeout(() => this.geocode(value), 250);
  }

  private geocode(query: string): void {
    const url =
      `https://nominatim.openstreetmap.org/search` +
      `?format=json&q=${encodeURIComponent(query)}&limit=5&accept-language=en`;

    fetch(url)
      .then((r) => r.json())
      .then((data) => (this.searchResults = data))
      .catch(() => (this.searchResults = []));
  }

  selectResult(result: any): void {
    this.searchResults = [];
    this.searchQuery = this.getResultName(result);

    this.router.navigate(['/map'], {
      state: {
        name: this.getResultName(result),
        lat: parseFloat(result.lat),
        lon: parseFloat(result.lon),
        displayName: result.display_name,
      },
    });
  }

  clearSearch(): void {
    this.searchQuery = '';
    this.searchResults = [];
  }

  getResultName(result: any): string {
    return result.display_name?.split(',')[0] ?? '';
  }

  getResultSubtitle(result: any): string {
    const parts = result.display_name?.split(',') ?? [];
    return parts.slice(1, 3).join(',').trim();
  }
}