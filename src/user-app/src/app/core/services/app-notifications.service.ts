import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { AuthService } from './auth.service';

export interface AppNotification {
  id: number;
  type: string;
  title: string;
  message: string;
  relatedEntityType?: string | null;
  relatedEntityId?: number | null;
  actionUrl?: string | null;
  targetType?: string | null;
  targetId?: number | null;
  targetRoute?: string | null;
  createdAt: string;
  readAt?: string | null;
  isRead: boolean;
}

interface NotificationsResponse {
  notifications?: AppNotification[];
  items?: AppNotification[];
  unreadCount: number;
  totalCount?: number;
  skip: number;
  take: number;
  nextSkip: number;
  hasMore: boolean;
}

interface UnreadCountResponse {
  unreadCount: number;
}

const PAGE_SIZE = 20;

@Injectable({ providedIn: 'root' })
export class AppNotificationsService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);

  private readonly _items = signal<AppNotification[]>([]);
  private readonly _unreadCount = signal(0);
  private readonly _loading = signal(false);
  private readonly _loadingMore = signal(false);
  private readonly _loadError = signal<string | null>(null);
  private readonly _hasMore = signal(false);
  private readonly _nextSkip = signal(0);

  readonly items = computed(() => this._items());
  readonly unreadCount = computed(() => this._unreadCount());
  readonly loading = computed(() => this._loading());
  readonly loadingMore = computed(() => this._loadingMore());
  readonly loadError = computed(() => this._loadError());
  readonly hasMore = computed(() => this._hasMore());

  constructor() {
    effect(() => {
      if (this.auth.isLoggedIn()) {
        this.loadUnreadCount();
      } else {
        this.reset();
      }
    });
  }

  loadInitial(force = false): void {
    if (!this.auth.isLoggedIn() || this._loading()) return;
    if (!force && this._items().length > 0) return;

    this._loading.set(true);
    this._loadError.set(null);
    this.fetch(0, false);
  }

  loadMore(): void {
    if (!this.auth.isLoggedIn() || this._loadingMore() || !this._hasMore()) return;

    this._loadingMore.set(true);
    this._loadError.set(null);
    this.fetch(this._nextSkip(), true);
  }

  loadUnreadCount(): void {
    if (!this.auth.isLoggedIn()) {
      this._unreadCount.set(0);
      return;
    }

    this.http.get<UnreadCountResponse>('/api/notifications/unread-count').subscribe({
      next: (res) => this._unreadCount.set(res.unreadCount ?? 0),
      error: () => this._unreadCount.set(0),
    });
  }

  markRead(id: number): void {
    const item = this._items().find(notification => notification.id === id);
    if (!item || item.isRead) return;

    this.http.put<void>(`/api/notifications/${id}/read`, {}).subscribe({
      next: () => {
        const readAt = new Date().toISOString();
        this._items.update(items => items.map(notification =>
          notification.id === id
            ? { ...notification, isRead: true, readAt }
            : notification,
        ));
        this._unreadCount.update(count => Math.max(0, count - 1));
      },
      error: () => {},
    });
  }

  markAllRead(): void {
    if (this._unreadCount() === 0) return;

    this.http.put<void>('/api/notifications/read-all', {}).subscribe({
      next: () => {
        const readAt = new Date().toISOString();
        this._items.update(items => items.map(notification => ({
          ...notification,
          isRead: true,
          readAt: notification.readAt ?? readAt,
        })));
        this._unreadCount.set(0);
      },
      error: () => {},
    });
  }

  private fetch(skip: number, append: boolean): void {
    const params = new HttpParams()
      .set('skip', skip)
      .set('take', PAGE_SIZE);

    this.http.get<NotificationsResponse>('/api/notifications', { params }).subscribe({
      next: (res) => {
        const mapped = (res.items ?? res.notifications ?? []).map(notification => this.normalizeNotification(notification));
        this._items.set(append ? [...this._items(), ...mapped] : mapped);
        this._unreadCount.set(res.unreadCount ?? 0);
        this._nextSkip.set(res.nextSkip ?? mapped.length);
        this._hasMore.set(res.hasMore ?? false);
        this._loadError.set(null);
        this.finishLoading(append);
      },
      error: () => {
        if (!append) {
          this._items.set([]);
        }
        this._loadError.set("We couldn't load your notifications. Please try again.");
        this.finishLoading(append);
      },
    });
  }

  private normalizeNotification(notification: AppNotification): AppNotification {
    const actionUrl = this.normalizeInternalRoute(notification.actionUrl ?? notification.targetRoute);

    return {
      ...notification,
      relatedEntityType: notification.relatedEntityType ?? notification.targetType ?? null,
      relatedEntityId: notification.relatedEntityId ?? notification.targetId ?? null,
      actionUrl,
      targetRoute: actionUrl,
      targetType: notification.targetType ?? notification.relatedEntityType ?? null,
      targetId: notification.targetId ?? notification.relatedEntityId ?? null,
      isRead: Boolean(notification.isRead),
    };
  }

  private normalizeInternalRoute(value: string | null | undefined): string | null {
    if (!value?.startsWith('/')) {
      return null;
    }

    return value.startsWith('//') ? null : value;
  }

  private finishLoading(append: boolean): void {
    if (append) {
      this._loadingMore.set(false);
    } else {
      this._loading.set(false);
    }
  }

  private reset(): void {
    this._items.set([]);
    this._unreadCount.set(0);
    this._loading.set(false);
    this._loadingMore.set(false);
    this._loadError.set(null);
    this._hasMore.set(false);
    this._nextSkip.set(0);
  }
}
