import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, map, throwError } from 'rxjs';
import {
  DestinationSort,
  RegionalDestinationCreateRequest,
  RegionalDestinationDetailDto,
  RegionalDestinationListItemDto,
  RegionalDestinationListParams,
  RegionalDestinationListResponse,
  RegionalDestinationTagDto,
  RegionalDestinationUpdateRequest,
} from '../models/destination.model';

export interface DestinationListItem {
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
  destinationTypeId: number;
  destinationTypeName?: string | null;
  createdAt?: string | null;
  lastUpdatedAt?: string | null;
  imageUrl?: string | null;
  detailRoute?: string | null;
  editRoute?: string | null;
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
  internalRefId?: string;
  tagsDto?: RegionalDestinationTagDto[];
  analyticsViews?: number;
  analyticsVisitors?: number;
  analyticsSaves?: number;
  analyticsActions?: number;
  analyticsReviewAvg?: number;
  analyticsReviewCount?: number;
  analyticsTrend?: 'up' | 'stable' | 'down';
  canDelete?: boolean;
}

export interface DestinationsPage {
  items: DestinationListItem[];
  total: number;
  page: number;
  pageSize: number;
}

export type DestinationSortField = 'name' | 'lastUpdated' | 'status';
export type DestinationSortDir = 'asc' | 'desc';

export interface DestinationListState {
  searchQuery: string;
  sort: DestinationSort;
  displayCount: number;
  scrollTop: number;
}

@Injectable({ providedIn: 'root' })
export class DestinationsListService {
  private readonly endpoint = '/api/admin/regional/destinations';
  private readonly cache = new Map<string, DestinationListItem>();
  private listState: DestinationListState | null = null;

  constructor(private readonly http: HttpClient) {}

  list(params: RegionalDestinationListParams): Observable<RegionalDestinationListResponse> {
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

    return this.http.get<RegionalDestinationListResponse>(this.endpoint, { params: httpParams });
  }

  getDestination(regionId: number, destinationId: string | number): Observable<RegionalDestinationDetailDto> {
    const params = new HttpParams().set('regionId', String(regionId));
    return this.http.get<RegionalDestinationDetailDto>(`${this.endpoint}/${destinationId}`, { params });
  }

  createDestination(payload: RegionalDestinationCreateRequest): Observable<RegionalDestinationDetailDto> {
    return this.http.post<RegionalDestinationDetailDto>(this.endpoint, this.buildCreateFormData(payload));
  }

  updateDestination(
    regionId: number,
    destinationId: string | number,
    payload: RegionalDestinationUpdateRequest,
  ): Observable<RegionalDestinationDetailDto> {
    const params = new HttpParams().set('regionId', String(regionId));
    return this.http.put<RegionalDestinationDetailDto>(`${this.endpoint}/${destinationId}`, this.buildUpdateFormData(payload), { params });
  }

  getDestinationTags(): Observable<RegionalDestinationTagDto[]> {
    return this.http.get<RegionalDestinationTagDto[]>('/api/admin/regional/destination-tags');
  }

  searchDestinationTags(query: string): Observable<RegionalDestinationTagDto[]> {
    const params = new HttpParams().set('search', query);
    return this.http.get<RegionalDestinationTagDto[]>('/api/admin/regional/destination-tags', { params });
  }

  createDestinationTag(name: string): Observable<RegionalDestinationTagDto> {
    return this.http.post<RegionalDestinationTagDto>('/api/admin/regional/destination-tags', { name });
  }

  deleteDestination(regionId: number, destinationId: string | number): Observable<void> {
    const params = new HttpParams().set('regionId', String(regionId));
    return this.http.delete<void>(`${this.endpoint}/${destinationId}`, { params });
  }

  private buildUpdateFormData(payload: RegionalDestinationUpdateRequest): FormData {
    const { galleryImageFiles, ...request } = payload;
    const formData = new FormData();
    formData.append('payload', JSON.stringify(request));

    for (const file of galleryImageFiles ?? []) {
      if (file) {
        formData.append('galleryImages', file);
      }
    }

    return formData;
  }

  private buildCreateFormData(payload: RegionalDestinationCreateRequest): FormData {
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

  toListItem(dto: RegionalDestinationListItemDto): DestinationListItem {
    const id = String(dto.id);
    const lastUpdatedIso = dto.lastUpdatedAt ?? dto.createdAt ?? null;
    const item: DestinationListItem = {
      id,
      name: dto.name,
      summary: dto.summary ?? dto.description ?? '',
      description: dto.description ?? null,
      region: dto.regionDisplayName,
      status: dto.status || 'Published',
      internalRefId: dto.internalReferenceId ?? undefined,
      lastUpdated: this.formatDate(lastUpdatedIso),
      sortTimestamp: this.toTimestamp(lastUpdatedIso),
      imageBg: (dto.coverImageUrl ?? dto.imageUrl) ? `url("${dto.coverImageUrl ?? dto.imageUrl}") center/cover` : this.fallbackGradient(dto.id),
      category: dto.destinationTypeName ?? 'Destination',
      regionId: dto.regionId,
      regionDisplayName: dto.regionDisplayName,
      destinationTypeId: dto.destinationTypeId,
      destinationTypeName: dto.destinationTypeName,
      createdAt: dto.createdAt ?? null,
      lastUpdatedAt: dto.lastUpdatedAt ?? null,
      imageUrl: dto.imageUrl ?? dto.cardImageUrl ?? null,
      coverImageUrl: dto.coverImageUrl ?? dto.imageUrl ?? undefined,
      coverImageName: dto.coverImageUrl ? 'Cover image' : undefined,
      galleryImages: (dto.galleryImageUrls ?? []).map((url, index) => ({ url, name: `Gallery image ${index + 1}` })),
      detailRoute: dto.detailRoute ?? `/destinations/detail/${dto.id}`,
      editRoute: dto.editRoute ?? `/destinations/edit/${dto.id}`,
      tagsDto: dto.tags ?? [],
      lat: (dto as RegionalDestinationDetailDto).latitude,
      lng: (dto as RegionalDestinationDetailDto).longitude,
      tags: (dto.tags ?? []).map(tag => tag.name),
      canDelete: (dto as RegionalDestinationDetailDto).canDelete ?? true,
    };

    this.cache.set(id, item);
    return item;
  }

  rememberItems(items: DestinationListItem[]): void {
    items.forEach(item => this.cache.set(String(item.id), item));
  }

  getAllItems(): DestinationListItem[] {
    return [...this.cache.values()];
  }

  addItem(_item: Partial<DestinationListItem>): void {
    // The Regional Manager list is backend-owned; legacy create flows should not
    // inject unsaved mock rows into this data source.
  }

  updateItem(id: string | number, patch: Partial<DestinationListItem>): void {
    const key = String(id);
    const existing = this.cache.get(key);
    if (!existing) return;

    this.cache.set(key, { ...existing, ...patch });
  }

  findById(id: string | number): DestinationListItem | undefined {
    return this.cache.get(String(id));
  }

  delete$(id: string | number, regionId?: number): Observable<void> {
    if (!regionId) {
      return throwError(() => new Error('regionId is required to delete a destination.'));
    }

    return this.deleteDestination(regionId, id);
  }

  getPage$(params: {
    regionId: number;
    page: number;
    pageSize: number;
    search: string;
    sortField: DestinationSortField;
    sortDir: DestinationSortDir;
  }): Observable<DestinationsPage> {
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

  saveListState(state: DestinationListState): void {
    this.listState = state;
  }

  getListState(): DestinationListState | null {
    return this.listState;
  }

  private toBackendSort(field: DestinationSortField, dir: DestinationSortDir): DestinationSort {
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
