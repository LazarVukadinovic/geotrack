import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { RegionalImageUploadResponse } from '../models/regional-image.model';

@Injectable({ providedIn: 'root' })
export class RegionalMediaService {
  private readonly endpoint = '/api/admin/regional/media/images';

  constructor(private readonly http: HttpClient) {}

  uploadImages(regionId: number, files: File[]): Observable<RegionalImageUploadResponse> {
    const form = new FormData();
    for (const file of files) {
      form.append('files', file, file.name);
    }

    const params = new HttpParams().set('regionId', String(regionId));
    return this.http.post<RegionalImageUploadResponse>(this.endpoint, form, { params });
  }
}
