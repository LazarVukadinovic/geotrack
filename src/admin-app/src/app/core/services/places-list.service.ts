import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, map, throwError } from 'rxjs';
import {
  PlaceSort,
  RegionalPlaceCreateRequest,
  RegionalPlaceDetailDto,
  RegionalPlaceListItemDto,
  RegionalPlaceListParams,
  RegionalPlaceListResponse,
  RegionalPlaceParentLocationDto,
  RegionalPlaceTagDto,
  RegionalPlaceTypeDto,
} from '../models/place.model';

export interface PlaceListItem {
  id: string;
  name: string;
  summary: string;
  region: string;
  status: string;
  lastUpdated: string;
  sortTimestamp: number;
  imageBg: string;
  category: string;
  regionId: number;
  regionDisplayName: string;
  destinationId?: number | null;
  destinationName?: string | null;
  locationId?: number | null;
  locationName?: string | null;
  placeTypeId?: number | null;
  placeType?: string | null;
  stayTypeId?: number | null;
  stayType?: string | null;
  createdAt?: string | null;
  lastUpdatedAt?: string | null;
  imageUrl?: string | null;
  detailRoute?: string | null;
  editRoute?: string | null;
  tagsDto?: RegionalPlaceTagDto[];
  subtitle?: string | null;
  address?: string;
  description?: string | null;
  openingHours?: string | null;
  contactEmail?: string | null;
  phoneNumber?: string | null;
  contactPhone?: string;
  lat?: number;
  lng?: number;
  classification?: string[];
  tags?: string[];
  coverImageUrl?: string;
  coverImageName?: string;
  galleryImages?: { url: string; name: string }[];
  hours?: { name: string; open: boolean; openTime: string; closeTime: string }[];
  internalRefId?: string;
  parentLocation?: string;
  bestTimeToVisit?: string;
  visitorTips?: string;
  accessNotes?: string;
  accessibilityNotes?: string;
  website?: string;
  admissionPrice?: string;
  bookingLink?: string;
}

export interface PlacesPage {
  items: PlaceListItem[];
  total: number;
  page: number;
  pageSize: number;
}

export type PlaceSortField = 'name' | 'lastUpdated' | 'status';
export type PlaceSortDir = 'asc' | 'desc';

export interface PlaceListState {
  searchQuery: string;
  sort: PlaceSort;
  displayCount: number;
  scrollTop: number;
}

@Injectable({ providedIn: 'root' })
export class PlacesListService {
  private readonly endpoint = '/api/admin/regional/places';
  private readonly cache = new Map<string, PlaceListItem>();
  private listState: PlaceListState | null = null;

  constructor(private readonly http: HttpClient) {}

  list(params: RegionalPlaceListParams): Observable<RegionalPlaceListResponse> {
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

    const placeType = params.placeType?.trim();
    if (placeType) {
      httpParams = httpParams.set('placeType', placeType);
    }

    return this.http.get<RegionalPlaceListResponse>(this.endpoint, { params: httpParams });
  }

  getPlace(regionId: number, placeId: string | number): Observable<RegionalPlaceDetailDto> {
    const params = new HttpParams().set('regionId', String(regionId));
    return this.http.get<RegionalPlaceDetailDto>(`${this.endpoint}/${placeId}`, { params });
  }

  createPlace(payload: RegionalPlaceCreateRequest): Observable<RegionalPlaceDetailDto> {
    return this.http.post<RegionalPlaceDetailDto>(this.endpoint, payload);
  }

  getParentLocations(
    regionId: number,
    search?: string | null,
    take = 50,
  ): Observable<RegionalPlaceParentLocationDto[]> {
    let params = new HttpParams()
      .set('regionId', String(regionId))
      .set('take', String(take));

    const normalizedSearch = search?.trim();
    if (normalizedSearch) {
      params = params.set('search', normalizedSearch);
    }

    return this.http.get<RegionalPlaceParentLocationDto[]>(
      '/api/admin/regional/place-parent-locations',
      { params },
    );
  }

  getPlaceTags(): Observable<RegionalPlaceTagDto[]> {
    return this.http.get<RegionalPlaceTagDto[]>('/api/admin/regional/place-tags');
  }

  searchPlaceTags(query: string): Observable<RegionalPlaceTagDto[]> {
    const params = new HttpParams().set('search', query);
    return this.http.get<RegionalPlaceTagDto[]>('/api/admin/regional/place-tags', { params });
  }

  getPlaceTypes(): Observable<RegionalPlaceTypeDto[]> {
    return this.http.get<RegionalPlaceTypeDto[]>('/api/admin/regional/place-types');
  }

  deletePlace(regionId: number, placeId: string | number): Observable<void> {
    const params = new HttpParams().set('regionId', String(regionId));
    return this.http.delete<void>(`${this.endpoint}/${placeId}`, { params });
  }

  toListItem(dto: RegionalPlaceListItemDto): PlaceListItem {
    const id = String(dto.id);
    const lastUpdatedIso = dto.lastUpdatedAt ?? dto.createdAt ?? null;
    const locationName = dto.locationName ?? null;
    const destinationName = dto.destinationName ?? null;
    const placeType = dto.placeType ?? null;
    const stayType = dto.stayType ?? null;
    const item: PlaceListItem = {
      id,
      name: dto.name,
      summary: dto.summary ?? dto.subtitle ?? dto.description ?? (locationName ? `In ${locationName}` : ''),
      subtitle: dto.subtitle ?? null,
      description: dto.description ?? null,
      region: dto.regionDisplayName,
      status: dto.status || 'Published',
      internalRefId: dto.internalReferenceId ?? undefined,
      lastUpdated: this.formatDate(lastUpdatedIso),
      sortTimestamp: this.toTimestamp(lastUpdatedIso),
      imageBg: dto.imageUrl ? `url("${dto.imageUrl}") center/cover` : this.fallbackGradient(dto.id),
      category: placeType ?? 'Place',
      regionId: dto.regionId,
      regionDisplayName: dto.regionDisplayName,
      destinationId: dto.destinationId ?? null,
      destinationName,
      locationId: dto.locationId ?? null,
      locationName,
      placeTypeId: dto.placeTypeId ?? null,
      placeType,
      stayTypeId: dto.stayTypeId ?? null,
      stayType,
      createdAt: dto.createdAt ?? null,
      lastUpdatedAt: dto.lastUpdatedAt ?? null,
      tagsDto: dto.tags ?? [],
      tags: (dto.tags ?? []).map(tag => tag.name),
      imageUrl: dto.imageUrl ?? null,
      detailRoute: dto.detailRoute ?? `/places/detail/${dto.id}`,
      editRoute: dto.editRoute ?? '/places/new',
      address: dto.address ?? undefined,
    };

    this.cache.set(id, item);
    return item;
  }

  rememberItems(items: PlaceListItem[]): void {
    items.forEach(item => this.cache.set(String(item.id), item));
  }

  getAllItems(): PlaceListItem[] {
    return [...this.cache.values()];
  }

  addItem(_item: Partial<PlaceListItem>): void {
    // The Regional Manager list is backend-owned; legacy create flows should not
    // inject unsaved mock rows into this data source.
  }

  updateItem(id: string | number, patch: Partial<PlaceListItem>): void {
    const key = String(id);
    const existing = this.cache.get(key);
    if (!existing) return;

    this.cache.set(key, { ...existing, ...patch });
  }

  findById(id: string | number): PlaceListItem | undefined {
    return this.cache.get(String(id));
  }

  delete$(id: string | number, regionId?: number): Observable<void> {
    const resolvedRegionId = regionId ?? this.cache.get(String(id))?.regionId;
    if (!resolvedRegionId) {
      return throwError(() => new Error('regionId is required to delete a place.'));
    }

    return this.deletePlace(resolvedRegionId, id);
  }

  getPage$(params: {
    regionId: number;
    page: number;
    pageSize: number;
    search: string;
    sortField: PlaceSortField;
    sortDir: PlaceSortDir;
  }): Observable<PlacesPage> {
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

  saveListState(state: PlaceListState): void {
    this.listState = state;
  }

  getListState(): PlaceListState | null {
    return this.listState;
  }

  private toBackendSort(field: PlaceSortField, dir: PlaceSortDir): PlaceSort {
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
