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

    return this.http.get<AdminNotificationListResponse | AdminNotification[]>(this.endpoint, {
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
      map(response => {
        const safe = this.asRecord(response);
        return { unreadCount: this.toNumber(this.readField(safe, 'unreadCount', 'UnreadCount')) };
      }),
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

  private normalizePage(
    response: AdminNotificationListResponse | AdminNotification[] | null | undefined,
  ): AdminNotificationPage {
    if (Array.isArray(response)) {
      const items = this.normalizeItems(response);
      return {
        items,
        unreadCount: items.filter(item => !item.isRead).length,
        totalCount: items.length,
        total: items.length,
        skip: 0,
        take: items.length,
        nextSkip: null,
        hasMore: false,
      };
    }

    const safe = this.asRecord(response);
    const rawItems = this.readNotificationItems(safe);
    const items = this.normalizeItems(rawItems);
    const totalCount = this.toNumber(
      this.readField(safe, 'totalCount', 'TotalCount', 'total', 'Total'),
      items.length,
    );
    const nextSkip = this.toNullableNumber(this.readField(safe, 'nextSkip', 'NextSkip'));

    return {
      items,
      unreadCount: this.toNumber(this.readField(safe, 'unreadCount', 'UnreadCount')),
      totalCount,
      total: totalCount,
      skip: this.toNumber(this.readField(safe, 'skip', 'Skip')),
      take: this.toNumber(this.readField(safe, 'take', 'Take'), 25),
      nextSkip,
      hasMore: this.toBoolean(this.readField(safe, 'hasMore', 'HasMore'), nextSkip !== null),
    };
  }

  private normalizeItems(items: unknown[]): AdminNotification[] {
    return items
      .map(item => this.normalizeItem(item))
      .filter((item): item is AdminNotification => item !== null);
  }

  private readNotificationItems(source: Record<string, unknown>): unknown[] {
    const direct = this.readField(
      source,
      'items',
      'Items',
      'notifications',
      'Notifications',
      'results',
      'Results',
      'data',
      'Data',
    );

    if (Array.isArray(direct)) {
      return direct;
    }

    const nested = this.asRecord(direct);
    return this.toArray(this.readField(
      nested,
      'items',
      'Items',
      'notifications',
      'Notifications',
      'results',
      'Results',
    ));
  }

  private normalizeItem(item: unknown): AdminNotification | null {
    const safe = this.asRecord(item);
    const id = this.toNumber(this.readField(safe, 'id', 'Id'));

    if (id <= 0) {
      return null;
    }

    return {
      id,
      type: this.toString(this.readField(safe, 'type', 'Type'), 'System'),
      title: this.toString(this.readField(safe, 'title', 'Title'), 'Notification'),
      message: this.toString(this.readField(safe, 'message', 'Message')),
      severity: this.normalizeSeverity(this.readField(safe, 'severity', 'Severity')),
      isRead: this.toBoolean(this.readField(safe, 'isRead', 'IsRead')),
      readAt: this.toNullableString(this.readField(safe, 'readAt', 'ReadAt')),
      createdAt: this.toString(this.readField(safe, 'createdAt', 'CreatedAt')),
      targetType: this.toString(this.readField(safe, 'targetType', 'TargetType'), 'System'),
      targetId: this.toNullableNumber(this.readField(safe, 'targetId', 'TargetId')),
      targetRoute: this.safeRoute(this.toNullableString(this.readField(safe, 'targetRoute', 'TargetRoute'))),
      scopeType: this.toNullableString(this.readField(safe, 'scopeType', 'ScopeType')),
      scopeId: this.toNullableNumber(this.readField(safe, 'scopeId', 'ScopeId')),
      scopeLabel: this.toNullableString(this.readField(safe, 'scopeLabel', 'ScopeLabel')),
      metadataSummary: this.toNullableString(this.readField(safe, 'metadataSummary', 'MetadataSummary')),
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
    const safe = this.asRecord(response);

    return {
      unreadCount: this.toNumber(this.readField(safe, 'unreadCount', 'UnreadCount')),
      readAt: this.toNullableString(this.readField(safe, 'readAt', 'ReadAt')),
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

  private asRecord(value: unknown): Record<string, unknown> {
    return value !== null && typeof value === 'object' && !Array.isArray(value)
      ? value as Record<string, unknown>
      : {};
  }

  private readField(source: Record<string, unknown>, ...keys: string[]): unknown {
    for (const key of keys) {
      if (Object.prototype.hasOwnProperty.call(source, key)) {
        return source[key];
      }
    }

    return undefined;
  }

  private toArray<T>(value: unknown): T[] {
    return Array.isArray(value) ? value as T[] : [];
  }

  private toString(value: unknown, fallback = ''): string {
    return typeof value === 'string' && value.trim() ? value : fallback;
  }

  private toNullableString(value: unknown): string | null {
    return typeof value === 'string' && value.trim() ? value : null;
  }

  private toNumber(value: unknown, fallback = 0): number {
    if (typeof value === 'number' && Number.isFinite(value)) {
      return value;
    }

    if (typeof value === 'string' && value.trim()) {
      const parsed = Number(value);
      return Number.isFinite(parsed) ? parsed : fallback;
    }

    return fallback;
  }

  private toNullableNumber(value: unknown): number | null {
    if (typeof value === 'number' && Number.isFinite(value)) {
      return value;
    }

    if (typeof value === 'string' && value.trim()) {
      const parsed = Number(value);
      return Number.isFinite(parsed) ? parsed : null;
    }

    return null;
  }

  private toBoolean(value: unknown, fallback = false): boolean {
    if (typeof value === 'boolean') {
      return value;
    }

    if (typeof value === 'string') {
      const normalized = value.trim().toLowerCase();
      if (normalized === 'true') {
        return true;
      }
      if (normalized === 'false') {
        return false;
      }
    }

    return fallback;
  }
}
