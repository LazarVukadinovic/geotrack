import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, map, throwError } from 'rxjs';
import {
  EventSort,
  RegionalEventImageInputDto,
  RegionalEventCreateRequest,
  RegionalEventDetailDto,
  RegionalEventListItemDto,
  RegionalEventListParams,
  RegionalEventListResponse,
  RegionalEventParentDestinationDto,
  RegionalEventParentLocationDto,
  RegionalEventParentPlaceDto,
  RegionalEventTagDto,
  RegionalEventTypeDto,
  RegionalEventUpdateRequest,
} from '../models/event.model';

export interface EventListItem {
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
  placeId?: number | null;
  placeName?: string | null;
  eventTypeId?: number | null;
  eventType?: string | null;
  createdAt?: string | null;
  lastUpdatedAt?: string | null;
  startsAt?: string | null;
  endsAt?: string | null;
  imageUrl?: string | null;
  detailRoute?: string | null;
  editRoute?: string | null;
  dateRange?: string;
  startDate?: string;
  endDate?: string;
  startTime?: string;
  endTime?: string;
  lat?: number;
  lng?: number;
  address?: string;
  description?: string | null;
  venueName?: string;
  customVenueName?: string | null;
  ticketPrice?: string | null;
  externalTicketLink?: string | null;
  duration?: string | null;
  organizerName?: string;
  venueDescription?: string | null;
  arrivalTips?: string | null;
  accessibility?: string | null;
  classification?: string[];
  tags?: string[];
  coverImageUrl?: string;
  coverImageName?: string;
  galleryImages?: { url: string; name: string }[];
  zone?: [number, number][];
  venueType?: string;
  contactEmail?: string;
  contactPhone?: string;
  attendanceLimit?: string;
  ageRestriction?: string;
  internalRefId?: string;
  allDay?: boolean;
  freeEvent?: boolean;
  recurring?: boolean;
  analyticsViews?: number;
  analyticsVisitors?: number;
  analyticsSaves?: number;
  analyticsActions?: number;
  analyticsReviewAvg?: number;
  analyticsReviewCount?: number;
  analyticsTrend?: 'up' | 'stable' | 'down';
  canDelete?: boolean;
}

export interface EventsPage {
  items: EventListItem[];
  total: number;
  page: number;
  pageSize: number;
}

export type EventSortField = 'name' | 'lastUpdated' | 'status' | 'eventDate';
export type EventSortDir = 'asc' | 'desc';

export interface EventListState {
  searchQuery: string;
  sort: EventSort;
  displayCount: number;
  scrollTop: number;
}

@Injectable({ providedIn: 'root' })
export class EventsListService {
  private readonly endpoint = '/api/admin/regional/events';
  private readonly cache = new Map<string, EventListItem>();
  private listState: EventListState | null = null;

  constructor(private readonly http: HttpClient) {}

  list(params: RegionalEventListParams): Observable<RegionalEventListResponse> {
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

    const eventType = params.eventType?.trim();
    if (eventType) {
      httpParams = httpParams.set('eventType', eventType);
    }

    return this.http.get<RegionalEventListResponse>(this.endpoint, { params: httpParams });
  }

  getEvent(regionId: number, eventId: string | number): Observable<RegionalEventDetailDto> {
    const params = new HttpParams().set('regionId', String(regionId));
    return this.http.get<RegionalEventDetailDto>(`${this.endpoint}/${eventId}`, { params });
  }

  createEvent(payload: RegionalEventCreateRequest): Observable<RegionalEventDetailDto> {
    return this.http.post<RegionalEventDetailDto>(this.endpoint, this.buildCreateFormData(payload));
  }

  updateEvent(
    regionId: number,
    eventId: string | number,
    payload: RegionalEventUpdateRequest,
  ): Observable<RegionalEventDetailDto> {
    const params = new HttpParams().set('regionId', String(regionId));
    return this.http.put<RegionalEventDetailDto>(`${this.endpoint}/${eventId}`, payload, { params });
  }

  uploadImages(regionId: number, files: File[]): Observable<string[]> {
    const formData = new FormData();
    files.forEach(file => formData.append('files', file));
    const params = new HttpParams().set('regionId', String(regionId));
    return this.http
      .post<{ images: { url: string }[] }>('/api/admin/regional/media/images', formData, { params })
      .pipe(map(response => response.images.map(image => image.url)));
  }

  deleteEvent(regionId: number, eventId: string | number): Observable<void> {
    const params = new HttpParams().set('regionId', String(regionId));
    return this.http.delete<void>(`${this.endpoint}/${eventId}`, { params });
  }

  getParentDestinations(
    regionId: number,
    search: string | null = null,
    take = 50,
  ): Observable<RegionalEventParentDestinationDto[]> {
    let params = new HttpParams().set('regionId', String(regionId)).set('take', String(take));
    const query = search?.trim();
    if (query) params = params.set('search', query);
    return this.http.get<RegionalEventParentDestinationDto[]>('/api/admin/regional/event-parent-destinations', { params });
  }

  getParentLocations(
    regionId: number,
    search: string | null = null,
    take = 50,
  ): Observable<RegionalEventParentLocationDto[]> {
    let params = new HttpParams().set('regionId', String(regionId)).set('take', String(take));
    const query = search?.trim();
    if (query) params = params.set('search', query);
    return this.http.get<RegionalEventParentLocationDto[]>('/api/admin/regional/event-parent-locations', { params });
  }

  getParentPlaces(
    regionId: number,
    search: string | null = null,
    take = 50,
  ): Observable<RegionalEventParentPlaceDto[]> {
    let params = new HttpParams().set('regionId', String(regionId)).set('take', String(take));
    const query = search?.trim();
    if (query) params = params.set('search', query);
    return this.http.get<RegionalEventParentPlaceDto[]>('/api/admin/regional/event-parent-places', { params });
  }

  getEventTags(search: string | null = null): Observable<RegionalEventTagDto[]> {
    let params = new HttpParams();
    const query = search?.trim();
    if (query) params = params.set('search', query);
    return this.http.get<RegionalEventTagDto[]>('/api/admin/regional/event-tags', { params });
  }

  createEventTag(name: string): Observable<RegionalEventTagDto> {
    return this.http.post<RegionalEventTagDto>('/api/admin/regional/event-tags', { name });
  }

  getEventTypes(): Observable<RegionalEventTypeDto[]> {
    return this.http.get<RegionalEventTypeDto[]>('/api/admin/regional/event-types');
  }


  private buildCreateFormData(payload: RegionalEventCreateRequest): FormData {
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

  toListItem(dto: RegionalEventListItemDto & { galleryImageUrls?: string[] | null }): EventListItem {
    const id = String(dto.id);
    const lastUpdatedIso = dto.lastUpdatedAt ?? dto.createdAt ?? null;
    const eventType = dto.eventType ?? dto.category ?? null;
    const venueName = dto.placeName ?? dto.locationName ?? dto.destinationName ?? undefined;
    const sortedImages = [...(dto.images ?? [])].sort((a, b) => a.sortOrder - b.sortOrder);
    const cardImage = sortedImages.find(image => image.isMain) ?? sortedImages[0] ?? null;
    const coverImage = sortedImages.find(image => !image.isMain && image.sortOrder === 1) ?? cardImage;
    const galleryImages = sortedImages
      .filter(image => !image.isMain && image.sortOrder >= 2)
      .map((image, index) => ({
        url: image.url,
        name: image.altText || image.caption || `Gallery image ${index + 1}`,
      }));
    const fallbackGalleryImages = (dto.galleryImageUrls ?? [])
      .filter(url => !galleryImages.some(image => image.url === url))
      .map((url, index) => ({ url, name: `Gallery image ${galleryImages.length + index + 1}` }));
    const imageUrl = cardImage?.url ?? dto.imageUrl ?? null;
    const coverImageUrl = coverImage?.url ?? imageUrl ?? undefined;
    const item: EventListItem = {
      id,
      name: dto.name,
      summary: dto.summary ?? dto.description ?? (venueName ? `At ${venueName}` : ''),
      description: dto.description ?? null,
      region: dto.regionDisplayName,
      status: dto.status || 'Published',
      lastUpdated: this.formatDate(lastUpdatedIso),
      sortTimestamp: this.toTimestamp(lastUpdatedIso),
      imageBg: coverImageUrl ? `url("${coverImageUrl}") center/cover` : this.fallbackGradient(dto.id),
      category: eventType ?? 'Event',
      regionId: dto.regionId,
      regionDisplayName: dto.regionDisplayName,
      destinationId: dto.destinationId ?? null,
      destinationName: dto.destinationName ?? null,
      locationId: dto.locationId ?? null,
      locationName: dto.locationName ?? null,
      placeId: dto.placeId ?? null,
      placeName: dto.placeName ?? null,
      eventTypeId: dto.eventTypeId ?? null,
      eventType,
      createdAt: dto.createdAt ?? null,
      lastUpdatedAt: dto.lastUpdatedAt ?? null,
      startsAt: dto.startsAt ?? null,
      endsAt: dto.endsAt ?? null,
      imageUrl,
      detailRoute: dto.detailRoute ?? `/events/detail/${dto.id}`,
      editRoute: dto.editRoute ?? `/events/edit/${dto.id}`,
      dateRange: this.formatDateRange(dto.startsAt ?? null, dto.endsAt ?? null),
      startDate: this.formatDate(dto.startsAt ?? null),
      endDate: this.formatDate(dto.endsAt ?? null),
      startTime: this.formatTime(dto.startsAt ?? null),
      endTime: this.formatTime(dto.endsAt ?? null),
      lat: this.toOptionalNumber(dto.latitude),
      lng: this.toOptionalNumber(dto.longitude),
      address: dto.address ?? undefined,
      venueName: dto.venueName ?? venueName,
      customVenueName: dto.venueName ?? null,
      ticketPrice: dto.ticketPrice ?? null,
      externalTicketLink: dto.externalTicketLink ?? null,
      freeEvent: dto.isFree ?? false,
      recurring: dto.isRecurring ?? false,
      allDay: dto.isAllDay ?? false,
      contactEmail: dto.contactEmail ?? undefined,
      attendanceLimit: dto.attendanceLimit != null ? String(dto.attendanceLimit) : undefined,
      ageRestriction: dto.ageRestriction ?? undefined,
      internalRefId: dto.internalReferenceId ?? undefined,
      tags: dto.tags?.map(tag => tag.name) ?? [],
      classification: dto.tags?.map(tag => tag.name) ?? [],
      duration: dto.duration ?? null,
      organizerName: dto.hostedBy ?? undefined,
      contactPhone: dto.contactPhone ?? undefined,
      venueDescription: dto.venueDescription ?? null,
      arrivalTips: dto.arrivalTips ?? null,
      accessibility: dto.accessibility ?? null,
      coverImageUrl,
      coverImageName: coverImageUrl ? 'Cover image' : undefined,
      galleryImages: [...galleryImages, ...fallbackGalleryImages],
      canDelete: (dto as any).canDelete ?? true,
    };

    this.cache.set(id, item);
    return item;
  }

  rememberItems(items: EventListItem[]): void {
    items.forEach(item => this.cache.set(String(item.id), item));
  }

  getAllItems(): EventListItem[] {
    return [...this.cache.values()];
  }

  addItem(_item: Partial<EventListItem>): void {
    // The Regional Manager list is backend-owned; legacy create flows should not
    // inject unsaved mock rows into this data source.
  }

  updateItem(id: string | number, patch: Partial<EventListItem>): void {
    const key = String(id);
    const existing = this.cache.get(key);
    if (!existing) return;

    this.cache.set(key, { ...existing, ...patch });
  }

  findById(id: string | number): EventListItem | undefined {
    return this.cache.get(String(id));
  }

  delete$(id: string | number, regionId?: number): Observable<void> {
    const resolvedRegionId = regionId ?? this.cache.get(String(id))?.regionId;
    if (!resolvedRegionId) {
      return throwError(() => new Error('regionId is required to delete an event.'));
    }

    return this.deleteEvent(resolvedRegionId, id);
  }

  getPage$(params: {
    regionId: number;
    page: number;
    pageSize: number;
    search: string;
    sortField: EventSortField;
    sortDir: EventSortDir;
  }): Observable<EventsPage> {
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

  saveListState(state: EventListState): void {
    this.listState = state;
  }

  getListState(): EventListState | null {
    return this.listState;
  }

  private toBackendSort(field: EventSortField, dir: EventSortDir): EventSort {
    if (field === 'name') {
      return dir === 'asc' ? 'nameAsc' : 'nameDesc';
    }

    if (field === 'eventDate') {
      return dir === 'asc' ? 'eventDateAsc' : 'eventDateDesc';
    }

    return dir === 'asc' ? 'oldest' : 'newest';
  }

  private formatDateRange(start: string | null, end: string | null): string | undefined {
    const startLabel = this.formatDate(start);
    const endLabel = this.formatDate(end);

    if (!startLabel && !endLabel) return undefined;
    if (!endLabel || startLabel === endLabel) return startLabel;
    if (!startLabel) return endLabel;
    return `${startLabel} - ${endLabel}`;
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

  private formatTime(value: string | null): string {
    if (!value) return '';

    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return '';

    const hours = String(date.getHours()).padStart(2, '0');
    const minutes = String(date.getMinutes()).padStart(2, '0');
    return `${hours}:${minutes}`;
  }

  private toTimestamp(value: string | null): number {
    if (!value) return 0;

    const timestamp = new Date(value).getTime();
    return Number.isFinite(timestamp) ? timestamp : 0;
  }

  private toOptionalNumber(value: number | null | undefined): number | undefined {
    return typeof value === 'number' && Number.isFinite(value) ? value : undefined;
  }

  private fallbackGradient(seed: number): string {
    const colors = ['#0ea5e9', '#f97316', '#10b981', '#8b5cf6', '#e11d48', '#0891b2', '#2563eb'];
    const first = colors[Math.abs(seed) % colors.length];
    const second = colors[Math.abs(seed + 3) % colors.length];
    return `linear-gradient(135deg, ${first} 0%, ${second} 100%)`;
  }
}
