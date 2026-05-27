import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AdminMapResponse,
  AdminMapSearchResponse,
  AdminMapViewport,
  AdminMapZone,
  RegionalEntityLocationValidationRequest,
  RegionalScopeValidationResult,
} from '../models/admin-map.model';

@Injectable({ providedIn: 'root' })
export class AdminMapService {
  private readonly endpoint = '/api/admin/regional/map';

  constructor(private readonly http: HttpClient) {}

  getMap(regionId: number, types: string[] = []): Observable<AdminMapResponse> {
    let params = new HttpParams().set('regionId', String(regionId));
    if (types.length > 0) {
      params = params.set('types', types.join(','));
    }

    return this.http.get<AdminMapResponse>(`${this.endpoint}/entities`, { params });
  }

  getViewport(regionId: number): Observable<AdminMapViewport> {
    const params = new HttpParams().set('regionId', String(regionId));
    return this.http.get<AdminMapViewport>(`${this.endpoint}/viewport`, { params });
  }

  getZones(regionId: number): Observable<AdminMapZone[]> {
    const params = new HttpParams().set('regionId', String(regionId));
    return this.http.get<AdminMapZone[]>(`${this.endpoint}/zones`, { params });
  }

  search(regionId: number, query: string): Observable<AdminMapSearchResponse> {
    const params = new HttpParams()
      .set('regionId', String(regionId))
      .set('query', query);

    return this.http.get<AdminMapSearchResponse>(`${this.endpoint}/search`, { params });
  }

  validateEntityLocation(
    request: RegionalEntityLocationValidationRequest,
  ): Observable<RegionalScopeValidationResult> {
    return this.http.post<RegionalScopeValidationResult>(
      '/api/admin/regional/validate-entity-location',
      request,
    );
  }
}
