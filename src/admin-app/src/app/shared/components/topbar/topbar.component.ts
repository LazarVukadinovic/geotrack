import { Component, ElementRef, HostListener, Input, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { Subject, finalize, interval, takeUntil } from 'rxjs';
import { AuthService } from '../../../core/services/auth.service';
import { AdminView } from '../../../core/models/admin-view.model';
import { buildAvatarDisplayUrl } from '../../../core/utils/avatar-url';
import { AdminNotificationsService } from '../../../core/services/administrator/admin-notifications.service';
import {
  AdminNotification,
  AdminNotificationFilter,
} from '../../../core/models/administrator/admin-notification.model';

@Component({
  selector: 'app-topbar',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './topbar.component.html',
})
export class TopbarComponent implements OnInit, OnDestroy {
  @Input() title = '';
  @Input() userName = '';
  @Input() userRole = '';
  @Input() showBack = false;
  @Input() backRoute: string | null = null;

  private readonly destroy$ = new Subject<void>();
  private notificationLoadSequence = 0;
  private readonly notificationPageSize = 25;

  readonly notificationSkeletonItems = [1, 2, 3, 4];
  failedAvatarUrl = '';
  profileMenuOpen = false;
  notificationDrawerOpen = false;
  notificationFilter: AdminNotificationFilter = 'all';
  notifications: AdminNotification[] = [];
  notificationsLoading = false;
  notificationsLoadingMore = false;
  notificationsError = '';
  notificationsHasMore = false;
  notificationsNextSkip: number | null = null;
  markingAllNotificationsRead = false;
  markingNotificationReadId: number | null = null;

  constructor(
    private router: Router,
    private readonly auth: AuthService,
    private readonly elementRef: ElementRef<HTMLElement>,
    private readonly notificationsService: AdminNotificationsService,
  ) {}

  ngOnInit(): void {
    if (!this.canUseAdminNotifications) {
      this.notificationsService.reset();
      return;
    }

    this.loadUnreadCount();
    interval(60000)
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => {
        if (!document.hidden && this.canUseAdminNotifications) {
          this.loadUnreadCount();
        }
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  get displayUserName(): string {
    return this.auth.fullName || this.userName;
  }

  get displayUserRole(): string {
    return this.auth.getUser()?.role || this.userRole;
  }

  get displayUserEmail(): string {
    return this.auth.getUser()?.email ?? '';
  }

  get displayAvatarUrl(): string {
    const user = this.auth.getUser();
    const avatarUrl = buildAvatarDisplayUrl(user?.avatarUrl, user?.avatarVersion);
    return avatarUrl && avatarUrl !== this.failedAvatarUrl ? avatarUrl : '';
  }

  get userInitials(): string {
    const user = this.auth.getUser();
    const parts = [
      user?.firstName ?? this.displayUserName.split(/\s+/)[0] ?? '',
      user?.lastName ?? this.displayUserName.split(/\s+/).slice(1).join(' ') ?? '',
    ]
      .map(part => part.trim())
      .filter(Boolean);

    if (parts.length > 0) {
      return parts
        .slice(0, 2)
        .map(part => part.charAt(0).toUpperCase())
        .join('');
    }

    return (this.displayUserEmail.trim().charAt(0) || '?').toUpperCase();
  }

  get availableViews(): AdminView[] {
    return this.auth.getAvailableAdminViews();
  }

  get showViewSwitcher(): boolean {
    return this.availableViews.length > 1;
  }

  get activeView(): AdminView | null {
    return this.auth.getActiveAdminView();
  }

  get avatarAlt(): string {
    const name = this.displayUserName || this.displayUserEmail || 'User';
    return `${name} profile image`;
  }

  get canUseAdminNotifications(): boolean {
    return this.auth.isAdministrator();
  }

  get unreadNotificationCount(): number {
    return this.notificationsService.unreadCount();
  }

  get unreadNotificationLabel(): string {
    const count = this.unreadNotificationCount;
    return count > 99 ? '99+' : String(count);
  }

  get filteredNotifications(): AdminNotification[] {
    if (this.notificationFilter === 'unread') {
      return this.notifications.filter(notification => !notification.isRead);
    }

    return this.notifications;
  }

  get notificationEmptyTitle(): string {
    return this.notificationFilter === 'unread'
      ? 'No unread notifications'
      : 'No notifications yet';
  }

  get notificationEmptyDescription(): string {
    return this.notificationFilter === 'unread'
      ? 'You are all caught up.'
      : 'New support reports, review reports, and system alerts will appear here.';
  }

  get canMarkAllNotificationsRead(): boolean {
    return this.unreadNotificationCount > 0
      && !this.notificationsLoading
      && !this.notificationsLoadingMore
      && !this.markingAllNotificationsRead;
  }

  onAvatarError(): void {
    this.failedAvatarUrl = this.displayAvatarUrl;
  }

  toggleProfileMenu(event: MouseEvent): void {
    event.stopPropagation();
    this.profileMenuOpen = !this.profileMenuOpen;
  }

  closeProfileMenu(): void {
    this.profileMenuOpen = false;
  }

  openNotifications(event: MouseEvent): void {
    event.stopPropagation();
    if (!this.canUseAdminNotifications) {
      return;
    }

    this.closeProfileMenu();
    this.notificationDrawerOpen = true;
    this.loadUnreadCount();
    this.resetAndLoadNotifications();
  }

  closeNotifications(): void {
    this.notificationDrawerOpen = false;
    this.notificationsError = '';
  }

  setNotificationFilter(filter: AdminNotificationFilter): void {
    if (this.notificationFilter === filter) {
      return;
    }

    this.notificationFilter = filter;
  }

  retryNotifications(): void {
    this.resetAndLoadNotifications();
  }

  loadMoreNotifications(): void {
    if (!this.notificationsHasMore || this.notificationsLoadingMore || this.notificationsNextSkip === null) {
      return;
    }

    this.loadNotifications(false);
  }

  markAllNotificationsRead(event: MouseEvent): void {
    event.stopPropagation();
    if (!this.canMarkAllNotificationsRead) {
      return;
    }

    this.markingAllNotificationsRead = true;
    this.notificationsError = '';

    this.notificationsService.markAllRead$().pipe(
      takeUntil(this.destroy$),
      finalize(() => {
        this.markingAllNotificationsRead = false;
      }),
    ).subscribe({
      next: result => {
        const readAt = result.readAt ?? new Date().toISOString();
        this.notifications = this.notifications.map(notification => ({
          ...notification,
          isRead: true,
          readAt: notification.readAt ?? readAt,
        }));
      },
      error: () => {
        this.notificationsError = 'Unable to mark notifications as read. Please try again.';
      },
    });
  }

  openNotification(notification: AdminNotification, event: MouseEvent): void {
    event.stopPropagation();
    if (this.markingNotificationReadId !== null) {
      return;
    }

    if (notification.isRead) {
      this.navigateNotification(notification);
      return;
    }

    this.markingNotificationReadId = notification.id;
    this.notificationsError = '';
    this.notificationsService.markRead$(notification.id).pipe(
      takeUntil(this.destroy$),
      finalize(() => {
        this.markingNotificationReadId = null;
      }),
    ).subscribe({
      next: result => {
        const readAt = result.readAt ?? new Date().toISOString();
        this.notifications = this.notifications.map(item =>
          item.id === notification.id
            ? { ...item, isRead: true, readAt }
            : item);
        this.navigateNotification({ ...notification, isRead: true, readAt });
      },
      error: () => {
        this.notificationsError = 'Unable to mark that notification as read. Please try again.';
      },
    });
  }

  formatNotificationTime(value: string | null | undefined): string {
    if (!value) {
      return '';
    }

    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return value;
    }

    const diffMs = date.getTime() - Date.now();
    const absMs = Math.abs(diffMs);
    const minute = 60 * 1000;
    const hour = 60 * minute;
    const day = 24 * hour;

    if (absMs < minute) {
      return 'Just now';
    }

    const formatter = new Intl.RelativeTimeFormat('en', { numeric: 'auto' });
    if (absMs < hour) {
      return formatter.format(Math.round(diffMs / minute), 'minute');
    }
    if (absMs < day) {
      return formatter.format(Math.round(diffMs / hour), 'hour');
    }
    if (absMs < 30 * day) {
      return formatter.format(Math.round(diffMs / day), 'day');
    }

    return date.toLocaleDateString('en-US', {
      month: 'short',
      day: 'numeric',
      year: date.getFullYear() === new Date().getFullYear() ? undefined : 'numeric',
    });
  }

  notificationSeverityClass(severity: string): string {
    switch (severity) {
      case 'Critical':
        return 'bg-red-50 text-red-700 ring-red-200';
      case 'Warning':
        return 'bg-amber-50 text-amber-700 ring-amber-200';
      default:
        return 'bg-blue-50 text-blue-700 ring-blue-200';
    }
  }

  notificationIconClass(notification: AdminNotification): string {
    switch (notification.type) {
      case 'SupportTicketCreated':
        return 'bg-blue-50 text-blue-600';
      case 'ReviewReported':
      case 'CommentReported':
        return 'bg-red-50 text-red-600';
      default:
        return notification.severity === 'Warning'
          ? 'bg-amber-50 text-amber-600'
          : 'bg-gray-100 text-gray-500';
    }
  }

  notificationTargetLabel(notification: AdminNotification): string {
    switch (notification.targetType) {
      case 'SupportTicket':
        return 'Support report';
      case 'ReviewReport':
      case 'Review':
        return 'Review moderation';
      case 'User':
        return 'User moderation';
      default:
        return 'System';
    }
  }

  trackByNotificationId(_: number, notification: AdminNotification): number {
    return notification.id;
  }

  isActiveView(view: AdminView): boolean {
    return this.activeView?.roleCode === view.roleCode;
  }

  selectView(view: AdminView): void {
    const changed = this.auth.switchAdminView(view.roleCode);
    this.closeProfileMenu();

    if (changed || this.router.url !== view.dashboardRoute) {
      this.router.navigateByUrl(view.dashboardRoute);
    }
  }

  openSettings(): void {
    const settingsRoute = this.activeView?.settingsRoute ?? '/settings';
    this.closeProfileMenu();
    this.router.navigateByUrl(settingsRoute);
  }

  signOut(): void {
    this.closeProfileMenu();
    this.auth.logout();
    this.router.navigateByUrl('/login');
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    const target = event.target instanceof Node ? event.target : null;
    if (this.profileMenuOpen && target && !this.elementRef.nativeElement.contains(target)) {
      this.closeProfileMenu();
    }
  }

  @HostListener('document:keydown.escape', ['$event'])
  onEscape(event: Event): void {
    if (this.notificationDrawerOpen) {
      event.preventDefault();
      this.closeNotifications();
      return;
    }

    if (!this.profileMenuOpen) {
      return;
    }

    event.preventDefault();
    this.closeProfileMenu();
  }

  goBack(): void {
    if (this.backRoute) {
      this.router.navigate([this.backRoute]);
    } else {
      history.back();
    }
  }

  private loadUnreadCount(): void {
    this.notificationsService.getUnreadCount$()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        error: () => this.notificationsService.reset(),
      });
  }

  private resetAndLoadNotifications(): void {
    this.notifications = [];
    this.notificationsHasMore = false;
    this.notificationsNextSkip = null;
    this.loadNotifications(true);
  }

  private loadNotifications(reset: boolean): void {
    const sequence = ++this.notificationLoadSequence;
    const skip = reset ? 0 : this.notificationsNextSkip ?? this.notifications.length;
    this.notificationsError = '';

    if (reset) {
      this.notificationsLoading = true;
    } else {
      this.notificationsLoadingMore = true;
    }

    this.notificationsService.getNotifications$({
      includeRead: true,
      skip,
      take: this.notificationPageSize,
    }).pipe(
      takeUntil(this.destroy$),
      finalize(() => {
        if (sequence === this.notificationLoadSequence) {
          this.notificationsLoading = false;
          this.notificationsLoadingMore = false;
        }
      }),
    ).subscribe({
      next: page => {
        if (sequence !== this.notificationLoadSequence) {
          return;
        }

        this.notifications = reset ? page.items : [...this.notifications, ...page.items];
        this.notificationsHasMore = page.hasMore;
        this.notificationsNextSkip = page.nextSkip ?? null;
      },
      error: () => {
        if (sequence !== this.notificationLoadSequence) {
          return;
        }

        this.notificationsError = 'Unable to load notifications. Please try again.';
      },
    });
  }

  private navigateNotification(notification: AdminNotification): void {
    const route = this.resolveNotificationRoute(notification);
    this.closeNotifications();
    this.router.navigateByUrl(route);
  }

  private resolveNotificationRoute(notification: AdminNotification): string {
    if (notification.targetRoute?.startsWith('/') && !notification.targetRoute.startsWith('//')) {
      return notification.targetRoute;
    }

    if (notification.targetType === 'SupportTicket' && notification.targetId) {
      return `/admin-support-tickets?ticketId=${encodeURIComponent(String(notification.targetId))}`;
    }

    if (notification.targetType === 'ReviewReport' || notification.targetType === 'Review') {
      return '/admin-reviews?status=reported';
    }

    return '/admin-dashboard';
  }
}
