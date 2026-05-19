import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, map, throwError } from 'rxjs';
import {
  LocationSort,
  RegionalLocationCreateRequest,
  RegionalLocationDetailDto,
  RegionalLocationListItemDto,
  RegionalLocationListParams,
  RegionalLocationListResponse,
  RegionalLocationParentDestinationDto,
  RegionalLocationTagDto,
  RegionalLocationTypeDto,
} from '../models/location.model';

export interface LocationListItem {
  id: string;
  name: string;
  summary: string;
  region: string;
  status: string;
  lastUpdated: string;
  sortTimestamp: number;
  imageUrl?: string | null;
  imageBg: string;
  category: string;
  regionId: number;
  regionDisplayName: string;
  destinationId?: number | null;
  destinationName?: string | null;
  locationTypeId?: number | null;
  locationType?: string | null;
  tagsDto?: RegionalLocationTagDto[];
  createdAt?: string | null;
  lastUpdatedAt?: string | null;
  lat?: number;
  lng?: number;
  address?: string;
  zone?: [number, number][];
  description?: string | null;
  classification?: string[];
  tags?: string[];
  coverImageUrl?: string;
  coverImageName?: string;
  galleryImages?: { url: string; name: string }[];
  parentDestination?: string;
  internalRefId?: string;
  detailRoute?: string | null;
  editRoute?: string | null;
  analyticsViews?: number;
  analyticsVisitors?: number;
  analyticsSaves?: number;
  analyticsActions?: number;
  analyticsReviewAvg?: number;
  analyticsReviewCount?: number;
  analyticsTrend?: 'up' | 'stable' | 'down';
}

export interface LocationsPage {
  items: LocationListItem[];
  total: number;
  page: number;
  pageSize: number;
}

export type LocationSortField = 'name' | 'lastUpdated' | 'status';
export type LocationSortDir = 'asc' | 'desc';

export interface LocationListState {
  searchQuery: string;
  sort: LocationSort;
  displayCount: number;
  scrollTop: number;
}

@Injectable({ providedIn: 'root' })
export class LocationsListService {
  private readonly endpoint = '/api/admin/regional/locations';
  private readonly cache = new Map<string, LocationListItem>();
  private listState: LocationListState | null = null;

  constructor(private readonly http: HttpClient) {}

  list(params: RegionalLocationListParams): Observable<RegionalLocationListResponse> {
    let httpParams = new HttpParams()
      .set('regionId', String(params.regionId))
      .set('sort', params.sort ?? 'newest')
      .set('skip', String(params.skip ?? 0))
      .set('take', String(params.take ?? 25));

    const search = params.search?.trim();
    if (search) {
      httpParams = httpParams.set('search', search);
    }

    if (params.status && params.status !== 'all') {
      httpParams = httpParams.set('status', params.status);
    }

    return this.http.get<RegionalLocationListResponse>(this.endpoint, { params: httpParams });
  }

  getLocation(regionId: number, locationId: string | number): Observable<RegionalLocationDetailDto> {
    const params = new HttpParams().set('regionId', String(regionId));
    return this.http.get<RegionalLocationDetailDto>(`${this.endpoint}/${locationId}`, { params });
  }

  createLocation(payload: RegionalLocationCreateRequest): Observable<RegionalLocationDetailDto> {
    return this.http.post<RegionalLocationDetailDto>(this.endpoint, this.buildCreateFormData(payload));
  }

  getParentDestinations(
    regionId: number,
    search?: string | null,
    take = 50,
  ): Observable<RegionalLocationParentDestinationDto[]> {
    let params = new HttpParams()
      .set('regionId', String(regionId))
      .set('take', String(take));

    const normalizedSearch = search?.trim();
    if (normalizedSearch) {
      params = params.set('search', normalizedSearch);
    }

    return this.http.get<RegionalLocationParentDestinationDto[]>(
      '/api/admin/regional/location-parent-destinations',
      { params },
    );
  }

  getLocationTags(): Observable<RegionalLocationTagDto[]> {
    return this.http.get<RegionalLocationTagDto[]>('/api/admin/regional/location-tags');
  }

  searchLocationTags(query: string): Observable<RegionalLocationTagDto[]> {
    const params = new HttpParams().set('search', query);
    return this.http.get<RegionalLocationTagDto[]>('/api/admin/regional/location-tags', { params });
  }

  getLocationTypes(): Observable<RegionalLocationTypeDto[]> {
    return this.http.get<RegionalLocationTypeDto[]>('/api/admin/regional/location-types');
  }

  deleteLocation(regionId: number, locationId: string | number): Observable<void> {
    const params = new HttpParams().set('regionId', String(regionId));
    return this.http.delete<void>(`${this.endpoint}/${locationId}`, { params });
  }


  private buildCreateFormData(payload: RegionalLocationCreateRequest): FormData {
    const { cardImageFile, coverImageFile, galleryImageFiles, ...request } = payload;
    if (!cardImageFile || !coverImageFile) {
      throw new Error('Card and cover images are required.');
    }

    const formData = new FormData();
    formData.append('payload', JSON.stringify(request));
    formData.append('cardImage', cardImageFile);
    formData.append('coverImage', coverImageFile);

    for (const file of galleryImageFiles ?? []) {
      formData.append('galleryImages', file);
    }

    return formData;
  }

  toListItem(dto: RegionalLocationListItemDto): LocationListItem {
    const id = String(dto.id);
    const lastUpdatedIso = dto.lastUpdatedAt ?? dto.createdAt ?? null;
    const destinationName = dto.destinationName ?? null;
    const item: LocationListItem = {
      id,
      name: dto.name,
      summary: dto.summary ?? dto.description ?? (destinationName ? `Part of ${destinationName}` : ''),
      description: dto.description ?? null,
      region: dto.regionDisplayName,
      status: dto.status || 'Published',
      internalRefId: dto.internalReferenceId ?? undefined,
      lastUpdated: this.formatDate(lastUpdatedIso),
      sortTimestamp: this.toTimestamp(lastUpdatedIso),
      imageUrl: dto.imageUrl ?? null,
      imageBg: dto.imageUrl ? `url("${dto.imageUrl}") center/cover` : this.fallbackGradient(dto.id),
      category: dto.locationType ?? 'Location',
      regionId: dto.regionId,
      regionDisplayName: dto.regionDisplayName,
      destinationId: dto.destinationId ?? null,
      destinationName,
      locationTypeId: dto.locationTypeId ?? null,
      locationType: dto.locationType ?? null,
      parentDestination: destinationName ?? undefined,
      tagsDto: dto.tags ?? [],
      tags: (dto.tags ?? []).map(tag => tag.name),
      createdAt: dto.createdAt ?? null,
      lastUpdatedAt: dto.lastUpdatedAt ?? null,
      detailRoute: dto.detailRoute ?? `/locations/detail/${dto.id}`,
      editRoute: dto.editRoute ?? '/locations/new',
    };

    this.cache.set(id, item);
    return item;
  }

  rememberItems(items: LocationListItem[]): void {
    items.forEach(item => this.cache.set(String(item.id), item));
  }

  getAllItems(): LocationListItem[] {
    return [...this.cache.values()];
  }

  addItem(_item: Partial<LocationListItem>): void {
    // The Regional Manager list is backend-owned; legacy create flows should not
    // inject unsaved mock rows into this data source.
  }

  updateItem(id: string | number, patch: Partial<LocationListItem>): void {
    const key = String(id);
    const existing = this.cache.get(key);
    if (!existing) return;

    this.cache.set(key, { ...existing, ...patch });
  }

  findById(id: string | number): LocationListItem | undefined {
    return this.cache.get(String(id));
  }

  delete$(id: string | number, regionId?: number): Observable<void> {
    const resolvedRegionId = regionId ?? this.cache.get(String(id))?.regionId;
    if (!resolvedRegionId) {
      return throwError(() => new Error('regionId is required to delete a location.'));
    }

    return this.deleteLocation(resolvedRegionId, id);
  }

  getPage$(params: {
    regionId: number;
    page: number;
    pageSize: number;
    search: string;
    sortField: LocationSortField;
    sortDir: LocationSortDir;
  }): Observable<LocationsPage> {
    const skip = Math.max(0, (params.page - 1) * params.pageSize);
    return this.list({
      regionId: params.regionId,
      search: params.search,
      sort: this.toBackendSort(params.sortField, params.sortDir),
      skip,
      take: params.pageSize,
    }).pipe(
      map(response => ({
        items: response.items.map(item => this.toListItem(item)),
        total: response.totalCount,
        page: params.page,
        pageSize: response.take,
      })),
    );
  }

  saveListState(state: LocationListState): void {
    this.listState = state;
  }

  getListState(): LocationListState | null {
    return this.listState;
  }

  private toBackendSort(field: LocationSortField, dir: LocationSortDir): LocationSort {
    if (field === 'name') {
      return dir === 'asc' ? 'nameAsc' : 'nameDesc';
    }

    return dir === 'asc' ? 'oldest' : 'newest';
  }

  private formatDate(value: string | null): string {
    if (!value) return '';

    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return '';

    return date.toLocaleDateString('en-US', {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
    });
  }

  private toTimestamp(value: string | null): number {
    if (!value) return 0;

    const timestamp = new Date(value).getTime();
    return Number.isFinite(timestamp) ? timestamp : 0;
  }

  private fallbackGradient(seed: number): string {
    const colors = ['#0ea5e9', '#f97316', '#10b981', '#8b5cf6', '#e11d48', '#0891b2', '#2563eb'];
    const first = colors[Math.abs(seed) % colors.length];
    const second = colors[Math.abs(seed + 3) % colors.length];
    return `linear-gradient(135deg, ${first} 0%, ${second} 100%)`;
  }
}
