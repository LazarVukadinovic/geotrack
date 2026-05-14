import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export interface DestinationDiscoverySearchResponse {
  query: string;
  items: DestinationDiscoverySearchItem[];
  totalCount: number;
  skip: number;
  take: number;
  nextSkip: number;
  hasMore: boolean;
}

export interface DestinationDiscoverySearchItem {
  id: number;
  name: string;
  summary?: string | null;
  description?: string | null;
  imageUrl?: string | null;
  detailRoute: string;
  latitude: number;
  longitude: number;
  tags: DestinationDiscoveryTag[];
  matchedBy: string[];
  matchedLocationCount: number;
  matchedPlaceCount: number;
  score: number;
}

export interface DestinationDiscoveryTag {
  id: number;
  name: string;
  slug: string;
  tagTypeId?: number | null;
  tagTypeSlug?: string | null;
}

export interface DestinationDiscoveryFilters {
  tagTypeFilters: DestinationDiscoveryTagTypeFilter[];
  placeTypeFilters: DestinationDiscoveryPlaceTypeFilter[];
}

export interface DestinationDiscoveryTagTypeFilter {
  id: number;
  name: string;
  slug: string;
  label: string;
  order: number;
  options: DestinationDiscoveryTagFilterOption[];
}

export interface DestinationDiscoveryTagFilterOption {
  id: number;
  name: string;
  slug: string;
  count: number;
}

export interface DestinationDiscoveryPlaceTypeFilter {
  id: number;
  name: string;
  count: number;
}

export interface DestinationDiscoverySearchRequest {
  query?: string;
  tagIds?: number[];
  placeTypeIds?: number[];
  skip?: number;
  take?: number;
}

@Injectable({ providedIn: 'root' })
export class DestinationDiscoveryService {
  private readonly http = inject(HttpClient);

  search(request: DestinationDiscoverySearchRequest): Observable<DestinationDiscoverySearchResponse> {
    let params = new HttpParams()
      .set('skip', String(request.skip ?? 0))
      .set('take', String(request.take ?? 20));

    const query = request.query?.trim();
    if (query) {
      params = params.set('q', query);
    }

    if (request.tagIds?.length) {
      params = params.set('tagIds', this.joinIds(request.tagIds));
    }

    if (request.placeTypeIds?.length) {
      params = params.set('placeTypeIds', this.joinIds(request.placeTypeIds));
    }

    return this.http.get<DestinationDiscoverySearchResponse>('/api/destinations/discovery-search', { params });
  }

  getFilters(): Observable<DestinationDiscoveryFilters> {
    return this.http.get<DestinationDiscoveryFilters>('/api/destinations/discovery-search/filters');
  }

  private joinIds(ids: number[]): string {
    return ids
      .filter(id => Number.isFinite(id) && id > 0)
      .map(id => String(id))
      .join(',');
  }
}
