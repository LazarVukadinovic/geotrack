import { Component, ElementRef, HostListener, Input, OnDestroy, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { Subject, defer, finalize, interval, takeUntil } from 'rxjs';
import { AuthService } from '../../../core/services/auth.service';
import { AdminView } from '../../../core/models/admin-view.model';
import { buildAvatarDisplayUrl } from '../../../core/utils/avatar-url';
import { AdminNotificationsService } from '../../../core/services/administrator/admin-notifications.service';
import { NotificationReuseStrategy } from '../../../core/routing/notification-reuse-strategy';
import {
  AdminNotification,
  AdminNotificationFilter,
} from '../../../core/models/administrator/admin-notification.model';

type NotificationDrawerStatus = 'closed' | 'idle' | 'loading' | 'success' | 'empty' | 'error';

interface NotificationDrawerState {
  status: NotificationDrawerStatus;
  items: AdminNotification[];
  selectedTab: AdminNotificationFilter;
  errorMessage: string;
  loadedAt: number;
  hasMore: boolean;
  nextSkip: number | null;
  loadingMore: boolean;
  markingAllRead: boolean;
  markingReadId: number | null;
}

function initialNotificationDrawerState(): NotificationDrawerState {
  return {
    status: 'closed',
    items: [],
    selectedTab: 'all',
    errorMessage: '',
    loadedAt: 0,
    hasMore: false,
    nextSkip: null,
    loadingMore: false,
    markingAllRead: false,
    markingReadId: null,
  };
}

@Component({
  selector: 'app-topbar',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './topbar.component.html',
})
export class TopbarComponent implements OnInit, OnDestroy {
  private static readonly activeRegionStorageKey = 'geotrack_admin_active_region_id';
  private static readonly notificationCacheTtlMs = 60000;

  @Input() title = '';
  @Input() userName = '';
  @Input() userRole = '';
  @Input() showBack = false;
  @Input() backRoute: string | null = null;

  private readonly destroy$ = new Subject<void>();
  private readonly notificationRequestCancel$ = new Subject<void>();
  private notificationLoadSequence = 0;
  private readonly notificationPageSize = 25;
  private lastNotificationNavigationUrl: string | null = null;

  readonly notificationSkeletonItems = [1, 2, 3, 4];
  private readonly notificationDrawerState = signal<NotificationDrawerState>(initialNotificationDrawerState());

  failedAvatarUrl = '';
  profileMenuOpen = false;
  notificationDrawerOpen = false;

  constructor(
    private router: Router,
    private readonly auth: AuthService,
    private readonly elementRef: ElementRef<HTMLElement>,
    private readonly notificationsService: AdminNotificationsService,
    private readonly reuseStrategy: NotificationReuseStrategy,
  ) {}

  ngOnInit(): void {
    if (this.canUseAdminNotifications) {
      this.loadUnreadCount();
    } else {
      this.notificationsService.reset();
    }

    interval(60000)
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => {
        if (!document.hidden && this.canUseAdminNotifications) {
          this.loadUnreadCount();
        }
      });
  }

  ngOnDestroy(): void {
    this.notificationRequestCancel$.next();
    this.notificationRequestCancel$.complete();
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
    return typeof this.auth.getAvailableAdminViews === 'function'
      ? this.auth.getAvailableAdminViews()
      : [];
  }

  get showViewSwitcher(): boolean {
    return this.availableViews.length > 1;
  }

  get activeView(): AdminView | null {
    return typeof this.auth.getActiveAdminView === 'function'
      ? this.auth.getActiveAdminView()
      : null;
  }

  get avatarAlt(): string {
    const name = this.displayUserName || this.displayUserEmail || 'User';
    return `${name} profile image`;
  }

  get canUseAdminNotifications(): boolean {
    const roleCode = this.currentRoleCode();
    return roleCode === 'Admin'
      || roleCode === 'ContentModerator'
      || roleCode === 'TerritoryManager'
      || roleCode === 'TerritoryAssociate'
      || roleCode === 'RegionalManager';
  }

  get notificationAudienceRole(): string | null {
    const roleCode = this.currentRoleCode();
    return roleCode === 'ContentModerator' || roleCode === 'TerritoryManager' || roleCode === 'TerritoryAssociate' || roleCode === 'RegionalManager'
      ? roleCode
      : null;
  }

  get notificationRegionId(): number | null {
    return this.notificationAudienceRole === 'RegionalManager'
      ? this.readActiveRegionId()
      : null;
  }

  get unreadNotificationCount(): number {
    return this.notificationsService.unreadCount();
  }

  get unreadNotificationLabel(): string {
    const count = this.unreadNotificationCount;
    return count > 99 ? '99+' : String(count);
  }

  get notificationState(): NotificationDrawerStatus {
    return this.notificationDrawerState().status;
  }

  get notificationFilter(): AdminNotificationFilter {
    return this.notificationDrawerState().selectedTab;
  }

  get notifications(): AdminNotification[] {
    return this.notificationDrawerState().items;
  }

  get notificationsLoading(): boolean {
    return this.notificationDrawerState().status === 'loading';
  }

  get notificationsLoadingMore(): boolean {
    return this.notificationDrawerState().loadingMore;
  }

  get notificationsHasLoaded(): boolean {
    return this.notificationDrawerState().loadedAt > 0;
  }

  get notificationsLoadedAt(): number {
    return this.notificationDrawerState().loadedAt;
  }

  get notificationsError(): string {
    return this.notificationDrawerState().errorMessage;
  }

  get notificationsHasMore(): boolean {
    return this.notificationDrawerState().hasMore;
  }

  get notificationsNextSkip(): number | null {
    return this.notificationDrawerState().nextSkip;
  }

  get markingAllNotificationsRead(): boolean {
    return this.notificationDrawerState().markingAllRead;
  }

  get markingNotificationReadId(): number | null {
    return this.notificationDrawerState().markingReadId;
  }

  get filteredNotifications(): AdminNotification[] {
    const state = this.notificationDrawerState();
    if (this.notificationFilter === 'unread') {
      return state.items.filter(notification => !notification.isRead);
    }

    return state.items;
  }

  get showNotificationSkeleton(): boolean {
    const state = this.notificationDrawerState();
    return state.status === 'loading'
      && state.loadedAt === 0
      && state.items.length === 0
      && !state.errorMessage;
  }

  get showNotificationError(): boolean {
    const state = this.notificationDrawerState();
    return state.status === 'error' && !!state.errorMessage;
  }

  get showNotificationEmptyState(): boolean {
    const status = this.notificationState;
    return (status === 'success' || status === 'empty' || (status === 'loading' && this.notificationsHasLoaded))
      && !this.showNotificationSkeleton
      && !this.showNotificationError
      && this.filteredNotifications.length === 0;
  }

  get showNotificationList(): boolean {
    return !this.showNotificationError && this.filteredNotifications.length > 0;
  }

  get notificationEmptyTitle(): string {
    return this.notificationFilter === 'unread'
      ? 'No unread notifications'
      : 'No notifications yet';
  }

  get notificationEmptyDescription(): string {
    if (this.notificationFilter === 'unread') {
      return "You're all caught up.";
    }

    return "You're all caught up. New updates will appear here.";
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
    this.notificationDrawerState.update(state => ({
      ...state,
      status: state.loadedAt > 0
        ? (state.items.length > 0 ? 'success' : 'empty')
        : 'idle',
      errorMessage: '',
    }));
    this.loadUnreadCount();

    if (this.shouldLoadNotificationsOnOpen()) {
      this.resetAndLoadNotifications(this.notificationsHasLoaded);
    }
  }

  closeNotifications(): void {
    this.cancelNotificationRequest();
    this.notificationDrawerOpen = false;
    this.notificationDrawerState.update(state => ({
      ...state,
      status: 'closed',
      errorMessage: '',
      loadingMore: false,
    }));
  }

  setNotificationFilter(filter: AdminNotificationFilter): void {
    if (this.notificationFilter === filter) {
      return;
    }

    this.notificationDrawerState.update(state => ({
      ...state,
      selectedTab: filter,
    }));
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

    this.notificationDrawerState.update(state => ({
      ...state,
      markingAllRead: true,
      errorMessage: '',
    }));

    const audienceRole = this.notificationAudienceRole;
    const regionId = this.notificationRegionId;

    this.notificationsService.markAllRead$(audienceRole, regionId).pipe(
      takeUntil(this.destroy$),
      finalize(() => {
        this.notificationDrawerState.update(state => ({
          ...state,
          markingAllRead: false,
        }));
      }),
    ).subscribe({
      next: result => {
        const readAt = result.readAt ?? new Date().toISOString();
        this.notificationDrawerState.update(state => ({
          ...state,
          status: state.items.length > 0 ? 'success' : 'empty',
          items: state.items.map(notification => ({
            ...notification,
            isRead: true,
            readAt: notification.readAt ?? readAt,
          })),
          errorMessage: '',
        }));
      },
      error: () => {
        this.notificationDrawerState.update(state => ({
          ...state,
          status: 'error',
          errorMessage: 'Unable to mark notifications as read. Please try again.',
        }));
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

    this.notificationDrawerState.update(state => ({
      ...state,
      markingReadId: notification.id,
      errorMessage: '',
    }));
    const audienceRole = this.notificationAudienceRole;
    const regionId = this.notificationRegionId;

    this.notificationsService.markRead$(notification.id, audienceRole, regionId).pipe(
      takeUntil(this.destroy$),
      finalize(() => {
        this.notificationDrawerState.update(state => ({
          ...state,
          markingReadId: null,
        }));
      }),
    ).subscribe({
      next: result => {
        const readAt = result.readAt ?? new Date().toISOString();
        this.notificationDrawerState.update(state => ({
          ...state,
          items: state.items.map(item =>
            item.id === notification.id
              ? { ...item, isRead: true, readAt }
              : item),
          status: state.items.length > 0 ? 'success' : 'empty',
          errorMessage: '',
        }));
        this.navigateNotification({ ...notification, isRead: true, readAt });
      },
      error: () => {
        this.notificationDrawerState.update(state => ({
          ...state,
          status: 'error',
          errorMessage: 'Unable to mark that notification as read. Please try again.',
        }));
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
      case 'ContributionPendingReview':
        return 'bg-emerald-50 text-emerald-600';
      case 'RegionCreated':
      case 'RegionalManagerAssigned':
      case 'RegionalManagerRemoved':
        return 'bg-violet-50 text-violet-600';
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
      case 'Contribution':
        return 'Requests queue';
      case 'Region':
        return 'Regional management';
      case 'User':
        return this.notificationAudienceRole === 'RegionalManager'
          ? 'Regional assignment'
          : this.notificationAudienceRole ? 'User administration' : 'User moderation';
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
    this.closeNotifications();
    this.notificationsService.reset();
    this.resetNotificationDrawerState();

    if (changed || this.router.url !== view.dashboardRoute) {
      this.router.navigateByUrl(view.dashboardRoute);
    }

    if (this.canUseAdminNotifications) {
      this.loadUnreadCount();
    }
  }

  openSettings(): void {
    const settingsRoute = this.activeView?.settingsRoute ?? '/settings';
    this.closeProfileMenu();
    this.router.navigateByUrl(settingsRoute);
  }

  signOut(): void {
    this.closeProfileMenu();
    this.closeNotifications();
    this.resetNotificationDrawerState();
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
    const audienceRole = this.notificationAudienceRole;
    const regionId = this.notificationRegionId;

    if (audienceRole === 'RegionalManager' && regionId === null) {
      this.notificationsService.reset();
      return;
    }

    this.notificationsService.getUnreadCount$(audienceRole, regionId)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        error: () => this.notificationsService.reset(),
      });
  }

  private resetAndLoadNotifications(preserveExisting = false): void {
    this.cancelNotificationRequest();

    this.notificationDrawerState.update(state => ({
      ...state,
      status: 'loading',
      items: preserveExisting ? state.items : [],
      loadedAt: preserveExisting ? state.loadedAt : 0,
      hasMore: false,
      nextSkip: null,
      loadingMore: false,
      errorMessage: '',
    }));
    this.loadNotifications(true);
  }

  private loadNotifications(reset: boolean): void {
    const sequence = ++this.notificationLoadSequence;
    const skip = reset ? 0 : this.notificationsNextSkip ?? this.notifications.length;
    let receivedPage = false;
    let failed = false;

    if (reset) {
      this.notificationDrawerState.update(state => ({
        ...state,
        status: 'loading',
        errorMessage: '',
      }));
    } else {
      this.notificationDrawerState.update(state => ({
        ...state,
        loadingMore: true,
        errorMessage: '',
      }));
    }

    const audienceRole = this.notificationAudienceRole;
    const regionId = this.notificationRegionId;

    const request = {
      includeRead: true,
      skip,
      take: this.notificationPageSize,
      ...(audienceRole ? { audienceRole } : {}),
      ...(regionId ? { regionId } : {}),
    };

    defer(() => this.notificationsService.getNotifications$(request)).pipe(
      takeUntil(this.destroy$),
      takeUntil(this.notificationRequestCancel$),
      finalize(() => {
        if (sequence === this.notificationLoadSequence) {
          this.notificationDrawerState.update(state => {
            if (receivedPage || failed || state.status !== 'loading') {
              return {
                ...state,
                loadingMore: false,
              };
            }

            return {
              ...state,
              status: state.items.length > 0 ? 'success' : 'empty',
              loadedAt: state.loadedAt || Date.now(),
              loadingMore: false,
              errorMessage: '',
            };
          });
        }
      }),
    ).subscribe({
      next: page => {
        if (sequence !== this.notificationLoadSequence) {
          return;
        }

        receivedPage = true;
        this.notificationDrawerState.update(state => {
          const items = reset ? page.items : [...state.items, ...page.items];

          return {
            ...state,
            status: items.length > 0 ? 'success' : 'empty',
            items,
            loadedAt: Date.now(),
            hasMore: page.hasMore,
            nextSkip: page.nextSkip ?? null,
            loadingMore: false,
            errorMessage: '',
          };
        });
      },
      error: () => {
        if (sequence !== this.notificationLoadSequence) {
          return;
        }

        failed = true;
        this.notificationDrawerState.update(state => ({
          ...state,
          status: 'error',
          loadingMore: false,
          errorMessage: 'Unable to load notifications. Please try again.',
        }));
      },
    });
  }

  private shouldLoadNotificationsOnOpen(): boolean {
    if (!this.notificationsHasLoaded || this.notificationsError) {
      return true;
    }

    return Date.now() - this.notificationsLoadedAt > TopbarComponent.notificationCacheTtlMs;
  }

  private cancelNotificationRequest(): void {
    this.notificationLoadSequence += 1;
    this.notificationRequestCancel$.next();
    this.notificationDrawerState.update(state => ({
      ...state,
      status: state.status === 'loading'
        ? state.loadedAt > 0
          ? (state.items.length > 0 ? 'success' : 'empty')
          : 'idle'
        : state.status,
      loadingMore: false,
    }));
  }

  private resetNotificationDrawerState(): void {
    this.notificationLoadSequence += 1;
    this.notificationRequestCancel$.next();
    this.lastNotificationNavigationUrl = null;
    this.notificationDrawerState.set(initialNotificationDrawerState());
  }

  private navigateNotification(notification: AdminNotification): void {
    const route = this.resolveNotificationRoute(notification);
    this.closeNotifications();

    // If the user is still on the page opened by the previous notification, replace that
    // history entry instead of stacking a new one. This keeps the browser back button
    // returning to wherever the user actually came from rather than walking back through
    // every notification they clicked while staying on the same destination.
    const stillOnPreviousNotificationPage =
      this.lastNotificationNavigationUrl !== null
      && this.router.url === this.lastNotificationNavigationUrl;

    // Force the destination page to re-create itself so that opening a different notification
    // while already on a notification target reloads with the new target, even when the
    // destination component reads its id/region from a one-time route snapshot.
    this.reuseStrategy.forceReloadOnNextNavigation();
    const navigation = stillOnPreviousNotificationPage
      ? this.router.navigateByUrl(route, { replaceUrl: true })
      : this.router.navigateByUrl(route);
    navigation.finally(() => this.reuseStrategy.reset());

    this.lastNotificationNavigationUrl = route;
  }

  private resolveNotificationRoute(notification: AdminNotification): string {
    if (notification.targetRoute?.startsWith('/') && !notification.targetRoute.startsWith('//')) {
      return notification.targetRoute;
    }

    if (notification.targetType === 'SupportTicket' && notification.targetId) {
      return `/admin-support-tickets?ticketId=${encodeURIComponent(String(notification.targetId))}`;
    }

    if (notification.targetType === 'ReviewReport' || notification.targetType === 'Review') {
      if (this.notificationAudienceRole === 'ContentModerator') {
        return '/content-moderator/review-management?status=reported';
      }

      return this.notificationAudienceRole === 'RegionalManager'
        ? this.withRegionQuery('/dashboard', notification)
        : this.notificationAudienceRole
        ? '/territory/regions'
        : '/admin-dashboard';
    }

    if (notification.targetType === 'Contribution') {
      if (this.notificationAudienceRole === 'RegionalManager') {
        return notification.targetId
          ? this.withRegionQuery(`/request/detail/contribution/${encodeURIComponent(String(notification.targetId))}`, notification)
          : this.withRegionQuery('/requests', notification);
      }

      return '/territory/regions';
    }

    if (notification.targetType === 'Region' && notification.targetId) {
      if (this.notificationAudienceRole === 'RegionalManager') {
        return this.withRegionQuery('/dashboard', notification);
      }

      return `/territory/regions/${encodeURIComponent(String(notification.targetId))}`;
    }

    if (notification.targetType === 'User' && this.notificationAudienceRole) {
      if (this.notificationAudienceRole === 'RegionalManager') {
        return this.withRegionQuery('/dashboard', notification);
      }

      return '/territory/users';
    }

    if (this.notificationAudienceRole === 'RegionalManager') {
      return this.withRegionQuery('/dashboard', notification);
    }

    if (this.notificationAudienceRole === 'ContentModerator') {
      return '/content-moderator/dashboard';
    }

    return this.notificationAudienceRole ? '/territory/overview' : '/admin-dashboard';
  }

  private currentRoleCode(): string | null {
    return this.activeView?.roleCode ?? this.auth.getUser()?.activeRoleCode ?? null;
  }

  private readActiveRegionId(): number | null {
    try {
      const value = sessionStorage.getItem(TopbarComponent.activeRegionStorageKey);
      const regionId = Number(value);
      return Number.isInteger(regionId) && regionId > 0 ? regionId : null;
    } catch {
      return null;
    }
  }

  private withRegionQuery(route: string, notification: AdminNotification): string {
    const regionId = notification.scopeType === 'Region' && notification.scopeId
      ? notification.scopeId
      : this.notificationRegionId;

    if (!regionId) {
      return route;
    }

    const separator = route.includes('?') ? '&' : '?';
    return `${route}${separator}regionId=${encodeURIComponent(String(regionId))}`;
  }
}
