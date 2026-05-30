import { vi } from 'vitest';
import { environment } from '../../environments/environment';
import { environment as productionEnvironment } from '../../environments/environment.production';
import {
  MapTileLayerConfig,
  MapTileLayerService,
  resolveMapTileLayer,
} from './map-tile-layer.service';

describe('MapTileLayerService', () => {
  it('creates Leaflet tile layers from the shared environment config', () => {
    const tileLayer = { addTo: vi.fn() };
    const leaflet = {
      tileLayer: vi.fn(() => tileLayer),
    };
    const map = {};
    const service = new MapTileLayerService();

    const createdLayer = service.addTo(leaflet, map, { opacity: 0.35, noWrap: false });

    expect(createdLayer).toBe(tileLayer);
    expect(leaflet.tileLayer).toHaveBeenCalledWith(environment.mapTiles.urlTemplate, {
      attribution: environment.mapTiles.attribution,
      maxZoom: environment.mapTiles.maxZoom,
      opacity: 0.35,
      noWrap: false,
    });
    expect(tileLayer.addTo).toHaveBeenCalledWith(map);
  });

  it('resolves the production tile URL and attribution without Stadia authentication', () => {
    const resolved = resolveMapTileLayer(productionEnvironment.mapTiles as MapTileLayerConfig);

    expect(resolved?.urlTemplate).toBe('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png');
    expect(resolved?.options['attribution']).toBe('&copy; OpenStreetMap contributors');
    expect(resolved?.options['maxZoom']).toBe(19);
  });

  it('does not request authenticated tiles when a required API key is missing', () => {
    const resolved = resolveMapTileLayer({
      provider: 'stadia',
      urlTemplate: 'https://tiles.example.test/{z}/{x}/{y}.png?api_key={apiKey}',
      attribution: 'Example tiles',
      maxZoom: 19,
      requiresApiKey: true,
      apiKey: '',
    });

    expect(resolved).toBeNull();
  });

  it('substitutes configured API keys without hardcoding them in source', () => {
    const resolved = resolveMapTileLayer({
      provider: 'custom',
      urlTemplate: 'https://tiles.example.test/{z}/{x}/{y}.png?api_key={apiKey}',
      attribution: 'Example tiles',
      maxZoom: 18,
      requiresApiKey: true,
      apiKey: 'runtime key',
    });

    expect(resolved?.urlTemplate).toBe('https://tiles.example.test/{z}/{x}/{y}.png?api_key=runtime%20key');
    expect(resolved?.options['maxZoom']).toBe(18);
  });
});
