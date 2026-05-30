import { Injectable } from '@angular/core';
import { environment } from '../../environments/environment';

export type MapTileProvider = 'osm' | 'stadia' | 'custom';

export interface MapTileLayerConfig {
  provider: MapTileProvider;
  urlTemplate: string;
  attribution: string;
  maxZoom?: number;
  minZoom?: number;
  requiresApiKey?: boolean;
  apiKey?: string;
}

export type MapTileLayerOptions = Record<string, unknown>;

export interface ResolvedMapTileLayer {
  urlTemplate: string;
  options: MapTileLayerOptions;
}

export function resolveMapTileLayer(
  config: MapTileLayerConfig,
  overrides: MapTileLayerOptions = {},
): ResolvedMapTileLayer | null {
  const apiKey = config.apiKey?.trim() ?? '';
  const needsApiKey = Boolean(config.requiresApiKey) || config.urlTemplate.includes('{apiKey}');

  if (needsApiKey && !apiKey) {
    return null;
  }

  const options: MapTileLayerOptions = {
    attribution: config.attribution,
    maxZoom: config.maxZoom ?? 19,
    ...overrides,
  };

  if (config.minZoom !== undefined && options['minZoom'] === undefined) {
    options['minZoom'] = config.minZoom;
  }

  return {
    urlTemplate: config.urlTemplate.replace('{apiKey}', encodeURIComponent(apiKey)),
    options,
  };
}

@Injectable({ providedIn: 'root' })
export class MapTileLayerService {
  private hasReportedMissingKey = false;

  createTileLayer(leaflet: any, overrides: MapTileLayerOptions = {}): any | null {
    if (!leaflet?.tileLayer) {
      return null;
    }

    const config = environment.mapTiles as MapTileLayerConfig;
    const resolved = resolveMapTileLayer(config, overrides);
    if (!resolved) {
      this.reportMissingApiKey(config);
      return null;
    }

    return leaflet.tileLayer(resolved.urlTemplate, resolved.options);
  }

  addTo(leaflet: any, map: any, overrides: MapTileLayerOptions = {}): any | null {
    if (!map) {
      return null;
    }

    const layer = this.createTileLayer(leaflet, overrides);
    layer?.addTo?.(map);
    return layer;
  }

  private reportMissingApiKey(config: MapTileLayerConfig): void {
    if (this.hasReportedMissingKey) {
      return;
    }

    this.hasReportedMissingKey = true;
    const message = `[GeoTrack] Map tile provider "${config.provider}" requires an API key, but none is configured. Base map tiles were not requested.`;
    if (environment.production) {
      console.error(message);
    } else {
      console.warn(message);
    }
  }
}
