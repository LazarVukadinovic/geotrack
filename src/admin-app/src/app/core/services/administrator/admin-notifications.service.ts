import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, computed, signal } from '@angular/core';
import { Observable, map, tap } from 'rxjs';
import {
  AdminNotification,
  AdminNotificationListRequest,
  AdminNotificationListResponse,
  AdminNotificationMutationResponse,
  AdminNotificationPage,
  AdminNotificationSeverity,
  AdminNotificationUnreadCountResponse,
} from '../../models/administrator/admin-notification.model';

@Injectable({ providedIn: 'root' })
export class AdminNotificationsService {
  private readonly endpoint = '/api/admin/notifications';
  private readonly _unreadCount = signal(0);

  readonly unreadCount = computed(() => this._unreadCount());

  constructor(private readonly http: HttpClient) {}

  getNotifications$(request: AdminNotificationListRequest): Observable<AdminNotificationPage> {
    const params = new HttpParams()
      .set('includeRead', String(request.includeRead))
      .set('skip', String(Math.max(0, request.skip)))
      .set('take', String(Math.max(1, request.take)));

    return this.http.get<AdminNotificationListResponse>(this.endpoint, {
      params: this.withRegionId(this.withAudienceRole(params, request.audienceRole), request.regionId),
    }).pipe(
      map(response => this.normalizePage(response)),
      tap(page => this._unreadCount.set(page.unreadCount)),
    );
  }

  getUnreadCount$(audienceRole?: string | null, regionId?: number | null): Observable<AdminNotificationUnreadCountResponse> {
    return this.http.get<AdminNotificationUnreadCountResponse>(`${this.endpoint}/unread-count`, {
      params: this.withRegionId(this.withAudienceRole(new HttpParams(), audienceRole), regionId),
    }).pipe(
      map(response => ({ unreadCount: this.toNumber(response?.unreadCount) })),
      tap(response => this._unreadCount.set(response.unreadCount)),
    );
  }

  markRead$(
    notificationId: number,
    audienceRole?: string | null,
    regionId?: number | null,
  ): Observable<AdminNotificationMutationResponse> {
    return this.http
      .post<AdminNotificationMutationResponse>(
        `${this.endpoint}/${encodeURIComponent(String(notificationId))}/read`,
        {},
        { params: this.withRegionId(this.withAudienceRole(new HttpParams(), audienceRole), regionId) },
      )
      .pipe(
        map(response => this.normalizeMutation(response)),
        tap(response => this._unreadCount.set(response.unreadCount)),
      );
  }

  markAllRead$(audienceRole?: string | null, regionId?: number | null): Observable<AdminNotificationMutationResponse> {
    return this.http.post<AdminNotificationMutationResponse>(
      `${this.endpoint}/mark-all-read`,
      {},
      { params: this.withRegionId(this.withAudienceRole(new HttpParams(), audienceRole), regionId) },
    ).pipe(
      map(response => this.normalizeMutation(response)),
      tap(response => this._unreadCount.set(response.unreadCount)),
    );
  }

  reset(): void {
    this._unreadCount.set(0);
  }

  private normalizePage(response: AdminNotificationListResponse | null | undefined): AdminNotificationPage {
    const safe = response ?? {
      items: [],
      unreadCount: 0,
      totalCount: 0,
      skip: 0,
      take: 25,
      nextSkip: null,
      hasMore: false,
    };

    return {
      items: (safe.items ?? []).map(item => this.normalizeItem(item)),
      unreadCount: this.toNumber(safe.unreadCount),
      totalCount: this.toNumber(safe.totalCount),
      total: this.toNumber(safe.totalCount),
      skip: this.toNumber(safe.skip),
      take: this.toNumber(safe.take, 25),
      nextSkip: this.toNullableNumber(safe.nextSkip),
      hasMore: !!safe.hasMore,
    };
  }

  private normalizeItem(item: AdminNotification): AdminNotification {
    return {
      id: this.toNumber(item.id),
      type: item.type || 'System',
      title: item.title || 'Notification',
      message: item.message ?? '',
      severity: this.normalizeSeverity(item.severity),
      isRead: !!item.isRead,
      readAt: item.readAt ?? null,
      createdAt: item.createdAt ?? '',
      targetType: item.targetType || 'System',
      targetId: this.toNullableNumber(item.targetId),
      targetRoute: this.safeRoute(item.targetRoute),
      scopeType: item.scopeType || null,
      scopeId: this.toNullableNumber(item.scopeId),
      scopeLabel: item.scopeLabel ?? null,
      metadataSummary: item.metadataSummary ?? null,
    };
  }

  private withAudienceRole(params: HttpParams, audienceRole: string | null | undefined): HttpParams {
    const normalized = audienceRole?.trim();
    return normalized ? params.set('audienceRole', normalized) : params;
  }

  private withRegionId(params: HttpParams, regionId: number | null | undefined): HttpParams {
    return typeof regionId === 'number' && Number.isFinite(regionId) && regionId > 0
      ? params.set('regionId', String(Math.trunc(regionId)))
      : params;
  }

  private normalizeMutation(response: AdminNotificationMutationResponse | null | undefined): AdminNotificationMutationResponse {
    return {
      unreadCount: this.toNumber(response?.unreadCount),
      readAt: response?.readAt ?? null,
    };
  }

  private normalizeSeverity(value: unknown): AdminNotificationSeverity {
    return value === 'Critical' || value === 'Warning' ? value : 'Info';
  }

  private safeRoute(value: string | null | undefined): string | null {
    const route = value?.trim() ?? '';
    if (!route || !route.startsWith('/') || route.startsWith('//')) {
      return null;
    }

    return route;
  }

  private toNumber(value: unknown, fallback = 0): number {
    return typeof value === 'number' && Number.isFinite(value) ? value : fallback;
  }

  private toNullableNumber(value: unknown): number | null {
    return typeof value === 'number' && Number.isFinite(value) ? value : null;
  }
}
