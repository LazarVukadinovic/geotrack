import { Location } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import {
  AppNotification,
  AppNotificationsService,
} from '../../../../core/services/app-notifications.service';
import { EmptyStateComponent } from '../../../../shared/components/empty-state/empty-state.component';
import { PageTopNavComponent } from '../../../../shared/components/page-top-nav/page-top-nav.component';

type NotificationFilter = 'all' | 'unread' | 'updates';

@Component({
  selector: 'app-notifications-center-page',
  standalone: true,
  imports: [EmptyStateComponent, PageTopNavComponent],
  templateUrl: './notifications-center-page.component.html',
  styleUrl: './notifications-center-page.component.scss',
})
export class NotificationsCenterPageComponent {
  private readonly notifications = inject(AppNotificationsService);
  private readonly location = inject(Location);
  private readonly router = inject(Router);

  protected readonly activeFilter = signal<NotificationFilter>('all');
  protected readonly items = this.notifications.items;
  protected readonly loading = this.notifications.loading;
  protected readonly loadingMore = this.notifications.loadingMore;
  protected readonly loadError = this.notifications.loadError;
  protected readonly hasMore = this.notifications.hasMore;
  protected readonly unreadCount = this.notifications.unreadCount;
  protected readonly hasUnread = computed(() => this.unreadCount() > 0);

  constructor() {
    this.notifications.loadInitial(true);
  }

  protected markAllRead(): void {
    this.notifications.markAllRead();
  }

  protected loadMore(): void {
    this.notifications.loadMore();
  }

  protected retry(): void {
    this.notifications.loadInitial(true);
  }

  protected selectFilter(filter: NotificationFilter): void {
    this.activeFilter.set(filter);
  }

  protected goBack(): void {
    const navigationId = Number(globalThis.history?.state?.navigationId ?? 0);

    if (navigationId > 1) {
      this.location.back();
      return;
    }

    void this.router.navigateByUrl('/explore');
  }

  protected openNotification(notification: AppNotification): void {
    this.notifications.markRead(notification.id);

    if (notification.actionUrl) {
      this.router.navigateByUrl(notification.actionUrl);
    }
  }

  protected iconKind(notification: AppNotification): 'approved' | 'rejected' | 'default' {
    if (notification.type === 'contribution_approved') return 'approved';
    if (notification.type === 'contribution_rejected') return 'rejected';
    return 'default';
  }

  protected timeLabel(value: string): string {
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return '';

    const seconds = Math.max(0, Math.floor((Date.now() - date.getTime()) / 1000));
    if (seconds < 60) return 'Just now';
    if (seconds < 3600) return `${Math.floor(seconds / 60)}m ago`;
    if (seconds < 86400) return `${Math.floor(seconds / 3600)}h ago`;
    if (seconds < 604800) return `${Math.floor(seconds / 86400)}d ago`;

    return date.toLocaleDateString(undefined, {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
    });
  }
}
