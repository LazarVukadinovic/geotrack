import { AdminMapViewport, AdminMapZone } from '../../../core/models/admin-map.model';

const fallbackCenter: [number, number] = [44.0, 20.9];
const defaultPinColor = '#2563eb';

export function assignedRegionZone(zones: AdminMapZone[] | null | undefined): AdminMapZone | null {
  return zones?.find(zone => zone.entityType === 'Region') ?? null;
}

export function isPreciseAssignedRegionBoundary(zone: AdminMapZone | null): boolean {
  return Boolean(zone?.geometryGeoJson) && !zone?.isGeometryFallback;
}

export function renderAssignedRegionBoundary(
  leaflet: any,
  map: any,
  currentLayer: any,
  zone: AdminMapZone | null,
): any {
  if (!map || typeof leaflet === 'undefined') return currentLayer;

  if (currentLayer) {
    map.removeLayer(currentLayer);
  }

  if (!zone?.geometryGeoJson) return null;

  const layer = leaflet.geoJSON(zone.geometryGeoJson, {
    interactive: false,
    style: {
      color: '#2563eb',
      weight: 2,
      opacity: 0.85,
      fillColor: '#60a5fa',
      fillOpacity: 0.12,
      dashArray: '5 4',
    },
  });
  layer.addTo(map);
  return layer;
}

export function createRegionalMapPinIcon(
  leaflet: any,
  color = defaultPinColor,
): any {
  if (typeof leaflet === 'undefined' || !leaflet?.divIcon) return null;

  return leaflet.divIcon({
    html: `<span class="gt-regional-picker-pin" style="--gt-regional-picker-pin-color: ${color}"></span>`,
    className: '',
    iconSize: [28, 36],
    iconAnchor: [14, 36],
  });
}

export function applyAssignedRegionViewport(
  map: any,
  viewport: AdminMapViewport | null,
  fallbackZoom: number,
  maxZoom = 11,
): void {
  if (!map) return;

  if (viewport && !viewport.isEmpty) {
    if (viewport.bounds) {
      map.fitBounds(
        [
          [viewport.bounds.south, viewport.bounds.west],
          [viewport.bounds.north, viewport.bounds.east],
        ],
        {
          padding: [24, 24],
          maxZoom,
          animate: false,
        },
      );
      return;
    }

    if (
      viewport.centerLatitude !== null &&
      viewport.centerLatitude !== undefined &&
      viewport.centerLongitude !== null &&
      viewport.centerLongitude !== undefined
    ) {
      map.setView(
        [viewport.centerLatitude, viewport.centerLongitude],
        Math.min(viewport.zoom ?? fallbackZoom, maxZoom),
        { animate: false },
      );
      return;
    }
  }

  map.setView(fallbackCenter, fallbackZoom, { animate: false });
}
