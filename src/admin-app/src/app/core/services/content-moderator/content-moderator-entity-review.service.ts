import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, map } from 'rxjs';
import {
  ContentModeratorEntityImage,
  ContentModeratorEntityMetadata,
  ContentModeratorEntityReviewCounts,
  ContentModeratorEntityReviewDetail,
  ContentModeratorEntityReviewListItem,
  ContentModeratorEntityReviewListResponse,
  ContentModeratorEntityReviewPageQuery,
  ContentModeratorEntityReviewPermissions,
  ContentModeratorEntityType,
  ContentModeratorModerationActionRequest,
} from '../../models/content-moderator/content-moderator-entity-review.model';

@Injectable({ providedIn: 'root' })
export class ContentModeratorEntityReviewService {
  private readonly endpoint = '/api/admin/content-moderator/entities';

  constructor(private readonly http: HttpClient) {}

  getPage$(query: ContentModeratorEntityReviewPageQuery): Observable<ContentModeratorEntityReviewListResponse> {
    let params = new HttpParams()
      .set('type', query.type)
      .set('status', query.status ?? 'all')
      .set('sort', query.sort ?? 'newest')
      .set('skip', String(query.skip ?? 0))
      .set('take', String(query.take ?? 25));

    const search = query.search?.trim();
    if (search) {
      params = params.set('search', search);
    }

    return this.http.get<ContentModeratorEntityReviewListResponse>(this.endpoint, { params }).pipe(
      map(response => this.normalizePage(response)),
    );
  }

  getDetail$(type: ContentModeratorEntityType, entityId: number): Observable<ContentModeratorEntityReviewDetail> {
    return this.http.get<ContentModeratorEntityReviewDetail>(`${this.endpoint}/${type}/${entityId}`).pipe(
      map(detail => this.normalizeDetail(detail)),
    );
  }

  suspend$(
    type: ContentModeratorEntityType,
    entityId: number,
    request?: ContentModeratorModerationActionRequest,
  ): Observable<ContentModeratorEntityReviewDetail> {
    return this.http.post<ContentModeratorEntityReviewDetail>(
      `${this.endpoint}/${type}/${entityId}/suspend`,
      request ?? {},
    ).pipe(map(detail => this.normalizeDetail(detail)));
  }

  unsuspend$(
    type: ContentModeratorEntityType,
    entityId: number,
    request?: ContentModeratorModerationActionRequest,
  ): Observable<ContentModeratorEntityReviewDetail> {
    return this.http.post<ContentModeratorEntityReviewDetail>(
      `${this.endpoint}/${type}/${entityId}/unsuspend`,
      request ?? {},
    ).pipe(map(detail => this.normalizeDetail(detail)));
  }

  delete$(
    type: ContentModeratorEntityType,
    entityId: number,
    request?: ContentModeratorModerationActionRequest,
  ): Observable<ContentModeratorEntityReviewDetail> {
    return this.http.post<ContentModeratorEntityReviewDetail>(
      `${this.endpoint}/${type}/${entityId}/delete`,
      request ?? {},
    ).pipe(map(detail => this.normalizeDetail(detail)));
  }

  formatDate(value: string | null | undefined): string {
    if (!value) {
      return 'Not available';
    }

    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return 'Not available';
    }

    return new Intl.DateTimeFormat('en', {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
    }).format(date);
  }

  formatDatetime(value: string | null | undefined): string {
    if (!value) {
      return 'Not available';
    }

    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return 'Not available';
    }

    return new Intl.DateTimeFormat('en', {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    }).format(date);
  }

  private normalizePage(value: ContentModeratorEntityReviewListResponse | null | undefined): ContentModeratorEntityReviewListResponse {
    const items = (value?.items ?? []).map(item => this.normalizeItem(item));
    const totalCount = this.toNumber(value?.totalCount);
    const skip = this.toNumber(value?.skip);
    const take = this.toNumber(value?.take, 25);
    const nextSkip = this.toNullableNumber(value?.nextSkip);

    return {
      items,
      totalCount,
      skip,
      take,
      nextSkip,
      hasMore: Boolean(value?.hasMore),
      counts: this.normalizeCounts(value?.counts),
    };
  }

  private normalizeDetail(value: ContentModeratorEntityReviewDetail): ContentModeratorEntityReviewDetail {
    const item = this.normalizeItem(value);

    return {
      ...item,
      description: this.toNullableString(value?.description),
      images: (value?.images ?? []).map(image => this.normalizeImage(image)),
      tags: (value?.tags ?? []).map(tag => ({
        id: this.toNumber(tag?.id),
        name: this.toString(tag?.name),
        slug: this.toString(tag?.slug),
      })),
      metadata: this.normalizeMetadata(value?.metadata),
    };
  }

  private normalizeItem(value: ContentModeratorEntityReviewListItem): ContentModeratorEntityReviewListItem {
    return {
      entityId: this.toNumber(value?.entityId),
      entityType: this.normalizeType(value?.entityType),
      name: this.toString(value?.name, 'Untitled entity'),
      summary: this.toNullableString(value?.summary),
      descriptionPreview: this.toNullableString(value?.descriptionPreview),
      thumbnailUrl: this.toNullableString(value?.thumbnailUrl),
      status: this.toString(value?.status, 'Unknown'),
      rawStatus: this.toString(value?.rawStatus, 'Unknown'),
      isPublished: Boolean(value?.isPublished),
      isSuspended: Boolean(value?.isSuspended),
      isDeleted: Boolean(value?.isDeleted),
      createdAt: this.toNullableString(value?.createdAt),
      updatedAt: this.toNullableString(value?.updatedAt),
      publishedAt: this.toNullableString(value?.publishedAt),
      regionId: this.toNullableNumber(value?.regionId),
      regionName: this.toNullableString(value?.regionName),
      territoryName: this.toNullableString(value?.territoryName),
      destinationId: this.toNullableNumber(value?.destinationId),
      destinationName: this.toNullableString(value?.destinationName),
      locationId: this.toNullableNumber(value?.locationId),
      locationName: this.toNullableString(value?.locationName),
      placeId: this.toNullableNumber(value?.placeId),
      placeName: this.toNullableString(value?.placeName),
      typeName: this.toNullableString(value?.typeName),
      createdByUserId: this.toNullableNumber(value?.createdByUserId),
      startsAt: this.toNullableString(value?.startsAt),
      endsAt: this.toNullableString(value?.endsAt),
      publicDetailRoute: this.toNullableString(value?.publicDetailRoute),
      canOpenInUserApp: Boolean(value?.canOpenInUserApp),
      permissions: this.normalizePermissions(value?.permissions),
    };
  }

  private normalizeCounts(value: ContentModeratorEntityReviewCounts | null | undefined): ContentModeratorEntityReviewCounts {
    return {
      allCount: this.toNumber(value?.allCount),
      activeCount: this.toNumber(value?.activeCount),
      suspendedCount: this.toNumber(value?.suspendedCount),
      deletedCount: this.toNumber(value?.deletedCount),
    };
  }

  private normalizePermissions(
    value: ContentModeratorEntityReviewPermissions | null | undefined,
  ): ContentModeratorEntityReviewPermissions {
    return {
      canView: value?.canView !== false,
      canSuspend: Boolean(value?.canSuspend),
      canUnsuspend: Boolean(value?.canUnsuspend),
      canDelete: Boolean(value?.canDelete),
      canRestore: Boolean(value?.canRestore),
    };
  }

  private normalizeImage(value: ContentModeratorEntityImage): ContentModeratorEntityImage {
    return {
      id: this.toNumber(value?.id),
      url: this.toString(value?.url),
      altText: this.toNullableString(value?.altText),
      caption: this.toNullableString(value?.caption),
      imageRole: this.toString(value?.imageRole, 'gallery'),
      isMain: Boolean(value?.isMain),
      sortOrder: this.toNumber(value?.sortOrder),
    };
  }

  private normalizeMetadata(value: ContentModeratorEntityMetadata | null | undefined): ContentModeratorEntityMetadata {
    return {
      latitude: this.toNullableNumber(value?.latitude),
      longitude: this.toNullableNumber(value?.longitude),
      address: this.toNullableString(value?.address),
      boundaryNote: this.toNullableString(value?.boundaryNote),
      boundaryShapeType: this.toNullableString(value?.boundaryShapeType),
      parentEntityType: this.toNullableString(value?.parentEntityType),
      venueName: this.toNullableString(value?.venueName),
      ticketPrice: this.toNullableString(value?.ticketPrice),
      isFree: typeof value?.isFree === 'boolean' ? value.isFree : null,
      isAllDay: typeof value?.isAllDay === 'boolean' ? value.isAllDay : null,
      isRecurring: typeof value?.isRecurring === 'boolean' ? value.isRecurring : null,
      contactEmail: this.toNullableString(value?.contactEmail),
      phoneNumber: this.toNullableString(value?.phoneNumber),
      website: this.toNullableString(value?.website),
      contentCounts: value?.contentCounts ?? {},
    };
  }

  private normalizeType(value: unknown): ContentModeratorEntityType {
    return value === 'location' || value === 'place' || value === 'event'
      ? value
      : 'destination';
  }

  private toString(value: unknown, fallback = ''): string {
    return typeof value === 'string' && value.trim() ? value.trim() : fallback;
  }

  private toNullableString(value: unknown): string | null {
    return typeof value === 'string' && value.trim() ? value.trim() : null;
  }

  private toNumber(value: unknown, fallback = 0): number {
    return typeof value === 'number' && Number.isFinite(value) ? value : fallback;
  }

  private toNullableNumber(value: unknown): number | null {
    return typeof value === 'number' && Number.isFinite(value) ? value : null;
  }
}
