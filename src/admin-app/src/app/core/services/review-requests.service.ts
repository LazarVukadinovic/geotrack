import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { map } from 'rxjs';
import {
  RegionalRequestActionResult,
  RegionalRequestBulkActionResponse,
  RegionalRequestQueueCountsByType,
  RegionalRequestQueueItem,
  RegionalRequestQueueResponse,
  RequestQueueSort,
  RequestQueueStatusFilter,
  RequestQueueTypeFilter,
  RequestSource,
  ReviewRequest,
  ReviewRequestDetail,
} from '../models/review-request.model';

export interface GetRegionalQueueParams {
  regionId: number;
  type?: RequestQueueTypeFilter;
  status?: RequestQueueStatusFilter;
  search?: string;
  sort?: RequestQueueSort;
  skip?: number;
  take?: number;
}

@Injectable({ providedIn: 'root' })
export class ReviewRequestsService {
  constructor(private readonly http: HttpClient) {}

  getQueue(params: GetRegionalQueueParams) {
    let httpParams = new HttpParams()
      .set('regionId', String(params.regionId))
      .set('type', params.type ?? 'all')
      .set('status', params.status ?? 'pending')
      .set('sort', params.sort ?? 'newest')
      .set('skip', String(params.skip ?? 0))
      .set('take', String(params.take ?? 10));

    const search = params.search?.trim();
    if (search) {
      httpParams = httpParams.set('search', search);
    }

    return this.http
      .get<RegionalRequestQueueResponse>('/api/admin/regional/requests', {
        params: httpParams,
      })
      .pipe(map(response => this.normalizeQueueResponse(response)));
  }

  getDetail(regionId: number, source: RequestSource | string, requestId: number) {
    return this.http
      .get<ReviewRequestDetail>(
        `/api/admin/regional/requests/${this.toRouteSource(source)}/${requestId}`,
        { params: new HttpParams().set('regionId', String(regionId)) },
      )
      .pipe(map(response => this.normalizeDetail(response)));
  }

  approve(regionId: number, request: ReviewRequest, note?: string) {
    return this.approveBySource(regionId, request.source, request.requestId, note);
  }

  approveBySource(regionId: number, source: RequestSource | string, requestId: number, note?: string) {
    return this.http.post<RegionalRequestActionResult>(
      `/api/admin/regional/requests/${this.toRouteSource(source)}/${requestId}/approve`,
      { regionId, note: note?.trim() || undefined },
    );
  }

  reject(regionId: number, request: ReviewRequest, reason?: string, note?: string) {
    return this.rejectBySource(regionId, request.source, request.requestId, reason, note);
  }

  rejectBySource(
    regionId: number,
    source: RequestSource | string,
    requestId: number,
    reason?: string,
    note?: string,
  ) {
    return this.http.post<RegionalRequestActionResult>(
      `/api/admin/regional/requests/${this.toRouteSource(source)}/${requestId}/reject`,
      {
        regionId,
        reason: reason?.trim() || undefined,
        note: note?.trim() || undefined,
      },
    );
  }

  bulkApprove(regionId: number, requestKeys: string[], note?: string) {
    return this.http.post<RegionalRequestBulkActionResponse>(
      '/api/admin/regional/requests/bulk-approve',
      {
        regionId,
        requestKeys,
        note: note?.trim() || undefined,
      },
    );
  }

  bulkReject(regionId: number, requestKeys: string[], reason?: string, note?: string) {
    return this.http.post<RegionalRequestBulkActionResponse>(
      '/api/admin/regional/requests/bulk-reject',
      {
        regionId,
        requestKeys,
        reason: reason?.trim() || undefined,
        note: note?.trim() || undefined,
      },
    );
  }

  parseRequestKey(requestKey: string): { source: RequestSource; requestId: number } | null {
    const [source, id] = requestKey.split(':', 2);
    const requestId = Number(id);
    if (!Number.isFinite(requestId) || requestId <= 0) {
      return null;
    }

    if (source === 'contribution') {
      return { source: 'Contribution', requestId };
    }

    return null;
  }

  private toRouteSource(source: RequestSource | string): string {
    const normalized = String(source)
      .trim()
      .replace(/[\s_-]/g, '')
      .toLowerCase();

    if (normalized === 'contribution' || normalized === 'contributions') {
      return 'contribution';
    }

    throw new Error('Invalid request source.');
  }

  private normalizeQueueResponse(response: RegionalRequestQueueResponse): RegionalRequestQueueResponse {
    if (!response || !Array.isArray(response.items)) {
      throw new Error('Invalid request queue response.');
    }

    return {
      items: response.items.map(item => this.normalizeQueueItem(item)),
      totalCount: this.toSafeCount(response.totalCount),
      pendingCount: this.toSafeCount(response.pendingCount),
      approvedCount: this.toSafeCount(response.approvedCount),
      rejectedCount: this.toSafeCount(response.rejectedCount),
      countsByType: this.normalizeCounts(response.countsByType),
      skip: this.toSafeCount(response.skip),
      take: this.toSafeCount(response.take),
    };
  }

  private normalizeQueueItem(item: RegionalRequestQueueItem): RegionalRequestQueueItem {
    return {
      ...item,
      requestKey: String(item.requestKey ?? ''),
      displayRequestId: String(item.displayRequestId ?? ''),
      title: String(item.title ?? ''),
      source: String(item.source ?? ''),
      entityType: String(item.entityType ?? ''),
      status: String(item.status ?? ''),
      regionDisplayName: String(item.regionDisplayName ?? ''),
      submittedByDisplayName: String(item.submittedByDisplayName ?? ''),
    };
  }

  private normalizeDetail(detail: ReviewRequestDetail): ReviewRequestDetail {
    if (!detail) {
      throw new Error('Invalid request detail response.');
    }

    return {
      ...detail,
      ...this.normalizeQueueItem(detail),
      openingHours: detail.openingHours ?? null,
      operatingHoursEnabled: detail.operatingHoursEnabled ?? null,
      operatingHours: Array.isArray(detail.operatingHours) ? detail.operatingHours : null,
      tags: Array.isArray(detail.tags) ? detail.tags : [],
      amenities: Array.isArray(detail.amenities) ? detail.amenities : [],
      stayAmenities: Array.isArray(detail.stayAmenities) ? detail.stayAmenities : [],
      bookingPartners: Array.isArray(detail.bookingPartners) ? detail.bookingPartners : [],
      images: Array.isArray(detail.images) ? detail.images : [],
      hasOwnershipProofDocument: Boolean(detail.hasOwnershipProofDocument || detail.ownershipProofDocumentUrl),
      ownershipProofDocumentUrl: detail.ownershipProofDocumentUrl ?? null,
      ownershipProofOriginalFileName: detail.ownershipProofOriginalFileName ?? null,
      ownershipProofContentType: detail.ownershipProofContentType ?? null,
      ownershipProofUploadedAt: detail.ownershipProofUploadedAt ?? null,
    };
  }

  private normalizeCounts(
    counts: RegionalRequestQueueCountsByType | null | undefined,
  ): RegionalRequestQueueCountsByType {
    return {
      destinations: this.toSafeCount(counts?.destinations),
      locations: this.toSafeCount(counts?.locations),
      places: this.toSafeCount(counts?.places),
      events: this.toSafeCount(counts?.events),
      ownership: this.toSafeCount(counts?.ownership),
      contributions: this.toSafeCount(counts?.contributions),
    };
  }

  private toSafeCount(value: unknown): number {
    return typeof value === 'number' && Number.isFinite(value) && value >= 0 ? value : 0;
  }
}
