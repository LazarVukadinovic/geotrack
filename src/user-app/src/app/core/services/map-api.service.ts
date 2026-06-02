import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface MapSearchResult {
  id?: number | null;
  type?: 'destination' | 'location' | 'place' | 'event' | string | null;
  name: string;
  description?: string | null;
  subtitle?: string | null;
  latitude: number;
  longitude: number;
  parentName?: string | null;
}

export interface MapReverseGeocodeResult {
  address: string;
  city: string;
  region: string;
  country: string;
  displayLabel: string;
}

export interface MapRouteCoordinate {
  latitude: number;
  longitude: number;
}

export interface MapRouteResult {
  coordinates: MapRouteCoordinate[];
  distanceMeters?: number | null;
  durationSeconds?: number | null;
}

export type MapEntityType = 'place' | 'stay' | 'event' | 'destination';

export interface MapEntityMarker {
  entityType: MapEntityType;
  entityId: number;
  title: string;
  subtitle?: string | null;
  categoryLabel?: string | null;
  imageUrl?: string | null;
  detailRoute?: string | null;
  latitude: number;
  longitude: number;
  distance?: string | null;
  isSaved: boolean;
}

export interface MapEntitiesResponse {
  focusLabel?: string | null;
  markers: MapEntityMarker[];
}

export type MapMarkersMode = 'entities' | 'destinations';

export interface MapViewportBounds {
  north: number;
  south: number;
  east: number;
  west: number;
}

export interface MapMarkersResponse {
  mode: MapMarkersMode;
  markers: MapEntityMarker[];
}

@Injectable({ providedIn: 'root' })
export class MapApiService {
  private readonly http = inject(HttpClient);

  getDestinations(): Observable<MapSearchResult[]> {
    return this.http.get<MapSearchResult[]>('/api/map/destinations');
  }

  search(query: string, types = 'destination,location,place,event'): Observable<MapSearchResult[]> {
    return this.http.get<MapSearchResult[]>('/api/map/search', {
      params: new HttpParams()
        .set('query', query)
        .set('types', types),
    });
  }

  geocode(query: string): Observable<MapSearchResult[]> {
    return this.http.get<MapSearchResult[]>('/api/map/search', {
      params: new HttpParams().set('query', query),
    });
  }

  reverseGeocode(latitude: number, longitude: number): Observable<MapReverseGeocodeResult> {
    return this.http.get<MapReverseGeocodeResult>('/api/map/reverse', {
      params: new HttpParams()
        .set('latitude', String(latitude))
        .set('longitude', String(longitude)),
    });
  }

  getWalkingRoute(
    originLatitude: number,
    originLongitude: number,
    destinationLatitude: number,
    destinationLongitude: number,
  ): Observable<MapRouteResult> {
    return this.http.get<MapRouteResult>('/api/map/route', {
      params: new HttpParams()
        .set('originLatitude', String(originLatitude))
        .set('originLongitude', String(originLongitude))
        .set('destinationLatitude', String(destinationLatitude))
        .set('destinationLongitude', String(destinationLongitude)),
    });
  }

  getDrivingRoute(
    originLatitude: number,
    originLongitude: number,
    destinationLatitude: number,
    destinationLongitude: number,
  ): Observable<MapRouteResult> {
    return this.http.get<MapRouteResult>('/api/map/driving-route', {
      params: new HttpParams()
        .set('originLatitude', String(originLatitude))
        .set('originLongitude', String(originLongitude))
        .set('destinationLatitude', String(destinationLatitude))
        .set('destinationLongitude', String(destinationLongitude)),
    });
  }

  getEntities(
    latitude: number,
    longitude: number,
    categories: string[],
    radiusKm?: number | null,
  ): Observable<MapEntitiesResponse> {
    let params = new HttpParams()
      .set('latitude', String(latitude))
      .set('longitude', String(longitude));

    if (categories.length > 0) {
      params = params.set('categories', categories.join(','));
    }

    if (radiusKm != null) {
      params = params.set('radiusKm', String(radiusKm));
    }

    return this.http.get<MapEntitiesResponse>('/api/map/entities', { params });
  }

  getMarkersInViewport(
    bounds: MapViewportBounds,
    zoom: number,
    categories: string[],
  ): Observable<MapMarkersResponse> {
    let params = new HttpParams()
      .set('north', String(bounds.north))
      .set('south', String(bounds.south))
      .set('east', String(bounds.east))
      .set('west', String(bounds.west))
      .set('zoom', String(zoom));

    if (categories.length > 0) {
      params = params.set('categories', categories.join(','));
    }

    return this.http.get<MapMarkersResponse>('/api/map/markers', { params });
  }
}
