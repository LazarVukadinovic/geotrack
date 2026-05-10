import { Injectable } from '@angular/core';

declare const L: any;

export type DrawTool = 'polygon' | 'rectangle' | 'circle';

export interface ZoneResult {
  layer: any;
  areaKm2: number;
  zone: [number, number][];
  centerLat: number;
  centerLng: number;
}

@Injectable({ providedIn: 'root' })
export class ZoneDrawService {

  readonly drawHints: Record<DrawTool, string> = {
    polygon:   'Click to add points. Double-click to close the polygon.',
    rectangle: 'Click and drag to draw a rectangle.',
    circle:    'Click the centre point, then drag to set the radius.',
  };

  readonly defaultShapeOptions = {
    color: '#2563eb', fillColor: '#3b82f6', fillOpacity: 0.2, weight: 2,
  };

  readonly cloneStyle = {
    color: '#2563eb', fillColor: '#3b82f6', fillOpacity: 0.25, weight: 2,
  };

  activateDrawTool(map: any, tool: DrawTool, onCreated: (layer: any) => void): any {
    let handler: any;
    switch (tool) {
      case 'polygon':
        handler = new L.Draw.Polygon(map, { shapeOptions: this.defaultShapeOptions, showArea: true, allowIntersection: false });
        break;
      case 'rectangle':
        handler = new L.Draw.Rectangle(map, { shapeOptions: this.defaultShapeOptions });
        break;
      case 'circle':
        handler = new L.Draw.Circle(map, { shapeOptions: this.defaultShapeOptions });
        break;
    }
    handler.enable();
    map.once('draw:created', (e: any) => onCreated(e.layer));
    return handler;
  }

  cloneLayer(layer: any): any {
    const s = this.cloneStyle;
    if (layer instanceof L.Circle)    return L.circle(layer.getLatLng(), { ...s, radius: layer.getRadius() });
    if (layer instanceof L.Rectangle) return L.rectangle(layer.getBounds(), s);
    if (layer instanceof L.Polygon)   return L.polygon(layer.getLatLngs(), s);
    return null;
  }

  calculateAreaKm2(layer: any): number {
    let areaM2 = 0;
    if (layer instanceof L.Circle) {
      const r = layer.getRadius();
      areaM2 = Math.PI * r * r;
    } else if (layer instanceof L.Polygon || layer instanceof L.Rectangle) {
      if ((L as any).GeometryUtil?.geodesicArea) {
        areaM2 = (L as any).GeometryUtil.geodesicArea(layer.getLatLngs()[0]);
      } else {
        areaM2 = this.shoelaceAreaM2(layer.getLatLngs()[0]);
      }
    }
    return areaM2 / 1_000_000;
  }

  extractZoneResult(layer: any): Omit<ZoneResult, 'layer' | 'areaKm2'> {
    let zone: [number, number][] = [];
    let centerLat: number;
    let centerLng: number;

    if (layer instanceof L.Circle) {
      const center = layer.getLatLng();
      const radius = layer.getRadius();
      centerLat = center.lat;
      centerLng = center.lng;
      const points = 16;
      for (let i = 0; i < points; i++) {
        const angle = (i / points) * 2 * Math.PI;
        const dLat = (radius / 111320) * Math.cos(angle);
        const dLng = (radius / (111320 * Math.cos(center.lat * Math.PI / 180))) * Math.sin(angle);
        zone.push([center.lat + dLat, center.lng + dLng]);
      }
    } else if (layer instanceof L.Rectangle) {
      const bounds = layer.getBounds();
      centerLat = (bounds.getNorth() + bounds.getSouth()) / 2;
      centerLng = (bounds.getEast() + bounds.getWest()) / 2;
      zone = [
        [bounds.getNorth(), bounds.getWest()],
        [bounds.getNorth(), bounds.getEast()],
        [bounds.getSouth(), bounds.getEast()],
        [bounds.getSouth(), bounds.getWest()],
      ];
    } else {
      const latlngs = layer.getLatLngs();
      const flat = Array.isArray(latlngs[0]) ? latlngs[0] : latlngs;
      zone = (flat as any[]).map((ll: any) => [ll.lat, ll.lng] as [number, number]);
      const bounds = layer.getBounds();
      centerLat = bounds.getCenter().lat;
      centerLng = bounds.getCenter().lng;
    }

    return { zone, centerLat: centerLat!, centerLng: centerLng! };
  }

  shoelaceAreaM2(latlngs: any[]): number {
    const flat = Array.isArray(latlngs[0]) ? latlngs[0] : latlngs;
    const R = 6_371_000;
    let area = 0;
    const n = flat.length;
    for (let i = 0; i < n; i++) {
      const j = (i + 1) % n;
      const lat1 = (flat[i].lat * Math.PI) / 180;
      const lat2 = (flat[j].lat * Math.PI) / 180;
      const lng1 = (flat[i].lng * Math.PI) / 180;
      const lng2 = (flat[j].lng * Math.PI) / 180;
      area += (lng2 - lng1) * (2 + Math.sin(lat1) + Math.sin(lat2));
    }
    return Math.abs((area * R * R) / 2);
  }
}