import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import type { GlobalSearchResponse, SearchDomain, SearchMetadata } from '../models/search.model';

@Injectable({ providedIn: 'root' })
export class SearchService {
  private readonly http = inject(HttpClient);

  search(
    query: string,
    domains: SearchDomain[],
    latitude?: number | null,
    longitude?: number | null,
  ): Observable<GlobalSearchResponse> {
    let params = new HttpParams()
      .set('q', query)
      .set('language', 'en')
      .set('take', '6');

    if (domains.length > 0) {
      params = params.set('domains', domains.join(','));
    }

    if (latitude != null && longitude != null) {
      params = params
        .set('latitude', String(latitude))
        .set('longitude', String(longitude));
    }

    return this.http.get<GlobalSearchResponse>('/api/search', { params });
  }

  metadata(): Observable<SearchMetadata> {
    return this.http.get<SearchMetadata>('/api/search/metadata', {
      params: new HttpParams().set('language', 'en'),
    });
  }
}
