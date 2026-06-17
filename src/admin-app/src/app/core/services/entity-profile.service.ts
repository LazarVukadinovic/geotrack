import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { EntityProfileDto, EntityProfileTagDto, EntityProfileUpdateRequest } from '../models/entity-profile.model';

export interface ImageUploadResult {
  fileName: string;
  url: string;
  sizeBytes: number;
}

export interface ImageUploadResponse {
  images: ImageUploadResult[];
}

@Injectable({ providedIn: 'root' })
export class EntityProfileService {
  constructor(private readonly http: HttpClient) {}

  // ── Region (RM) ──────────────────────────────────────────────────────────

  getRegionProfile$(regionId: number): Observable<EntityProfileDto> {
    return this.http.get<EntityProfileDto>('/api/admin/regional/region-profile', {
      params: new HttpParams().set('regionId', String(regionId)),
    });
  }

  updateRegionProfile$(regionId: number, request: EntityProfileUpdateRequest): Observable<EntityProfileDto> {
    return this.http.put<EntityProfileDto>('/api/admin/regional/region-profile', request, {
      params: new HttpParams().set('regionId', String(regionId)),
    });
  }

  getRegionTags$(regionId: number): Observable<EntityProfileTagDto[]> {
    return this.http.get<EntityProfileTagDto[]>('/api/admin/regional/region-tags', {
      params: new HttpParams().set('regionId', String(regionId)),
    });
  }

  uploadRegionImages$(regionId: number, files: File[]): Observable<ImageUploadResponse> {
    const form = new FormData();
    for (const file of files) {
      form.append('files', file, file.name);
    }
    return this.http.post<ImageUploadResponse>('/api/admin/regional/media/images', form, {
      params: new HttpParams().set('regionId', String(regionId)),
    });
  }

  // ── Territory (TM) ───────────────────────────────────────────────────────

  getTerritoryProfile$(territoryId: number): Observable<EntityProfileDto> {
    return this.http.get<EntityProfileDto>('/api/admin/territory/territory-profile', {
      params: new HttpParams().set('territoryId', String(territoryId)),
    });
  }

  updateTerritoryProfile$(territoryId: number, request: EntityProfileUpdateRequest): Observable<EntityProfileDto> {
    return this.http.put<EntityProfileDto>('/api/admin/territory/territory-profile', request, {
      params: new HttpParams().set('territoryId', String(territoryId)),
    });
  }

  getTerritoryTags$(territoryId: number): Observable<EntityProfileTagDto[]> {
    return this.http.get<EntityProfileTagDto[]>('/api/admin/territory/territory-tags', {
      params: new HttpParams().set('territoryId', String(territoryId)),
    });
  }

  uploadTerritoryImages$(territoryId: number, files: File[]): Observable<ImageUploadResponse> {
    const form = new FormData();
    for (const file of files) {
      form.append('files', file, file.name);
    }
    return this.http.post<ImageUploadResponse>('/api/admin/territory/media/images', form, {
      params: new HttpParams().set('territoryId', String(territoryId)),
    });
  }
}
