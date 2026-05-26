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

    return this.http.get<AdminNotificationListResponse>(this.endpoint, { params }).pipe(
      map(response => this.normalizePage(response)),
      tap(page => this._unreadCount.set(page.unreadCount)),
    );
  }

  getUnreadCount$(): Observable<AdminNotificationUnreadCountResponse> {
    return this.http.get<AdminNotificationUnreadCountResponse>(`${this.endpoint}/unread-count`).pipe(
      map(response => ({ unreadCount: this.toNumber(response?.unreadCount) })),
      tap(response => this._unreadCount.set(response.unreadCount)),
    );
  }

  markRead$(notificationId: number): Observable<AdminNotificationMutationResponse> {
    return this.http
      .post<AdminNotificationMutationResponse>(`${this.endpoint}/${encodeURIComponent(String(notificationId))}/read`, {})
      .pipe(
        map(response => this.normalizeMutation(response)),
        tap(response => this._unreadCount.set(response.unreadCount)),
      );
  }

  markAllRead$(): Observable<AdminNotificationMutationResponse> {
    return this.http.post<AdminNotificationMutationResponse>(`${this.endpoint}/mark-all-read`, {}).pipe(
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
      metadataSummary: item.metadataSummary ?? null,
    };
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
