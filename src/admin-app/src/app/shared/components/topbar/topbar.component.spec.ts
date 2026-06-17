import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { TopbarComponent } from './topbar.component';
import { AuthService } from '../../../core/services/auth.service';
import { AdminNotificationsService } from '../../../core/services/administrator/admin-notifications.service';
import { AdminView, getAdminViewByCode } from '../../../core/models/admin-view.model';
import { User } from '../../../core/models/user.model';
import { AdminNotification, AdminNotificationPage } from '../../../core/models/administrator/admin-notification.model';

describe('TopbarComponent', () => {
  let component: TopbarComponent;
  let fixture: ComponentFixture<TopbarComponent>;
  let currentUser: User;
  let activeView: AdminView;
  let availableViews: AdminView[];
  let unreadCountValue: number;
  let authService: {
    readonly fullName: string;
    getUser: ReturnType<typeof vi.fn>;
    getAvailableAdminViews: ReturnType<typeof vi.fn>;
    getActiveAdminView: ReturnType<typeof vi.fn>;
    switchAdminView: ReturnType<typeof vi.fn>;
    logout: ReturnType<typeof vi.fn>;
    isAdministrator: ReturnType<typeof vi.fn>;
  };
  let notificationsService: {
    unreadCount: ReturnType<typeof vi.fn>;
    reset: ReturnType<typeof vi.fn>;
    getUnreadCount$: ReturnType<typeof vi.fn>;
    getNotifications$: ReturnType<typeof vi.fn>;
    markRead$: ReturnType<typeof vi.fn>;
    markAllRead$: ReturnType<typeof vi.fn>;
  };

  const administratorView = getAdminViewByCode('Admin')!;
  const contentModeratorView = getAdminViewByCode('ContentModerator')!;
  const territoryManagerView = getAdminViewByCode('TerritoryManager')!;
  const territoryAssociateView = getAdminViewByCode('TerritoryAssociate')!;
  const regionalManagerView = getAdminViewByCode('RegionalManager')!;

  beforeEach(async () => {
    sessionStorage.clear();
    currentUser = buildUser();
    activeView = territoryManagerView;
    availableViews = [territoryManagerView];
    unreadCountValue = 0;
    authService = {
      get fullName(): string {
        return currentUser ? `${currentUser.firstName} ${currentUser.lastName}`.trim() : '';
      },
      getUser: vi.fn(() => currentUser),
      getAvailableAdminViews: vi.fn(() => availableViews),
      getActiveAdminView: vi.fn(() => activeView),
      isAdministrator: vi.fn(() => currentUser?.activeRoleCode === 'Admin'),
      switchAdminView: vi.fn((roleCode: string) => {
        const view = availableViews.find(item => item.roleCode === roleCode);
        if (!view) return false;

        activeView = view;
        currentUser = {
          ...currentUser,
          activeRoleCode: view.roleCode,
          role: view.label,
        };
        return true;
      }),
      logout: vi.fn(),
    };
    notificationsService = {
      unreadCount: vi.fn(() => unreadCountValue),
      reset: vi.fn(),
      getUnreadCount$: vi.fn(() => of({ unreadCount: unreadCountValue })),
      getNotifications$: vi.fn(() => of(notificationPage([supportNotification()], 1))),
      markRead$: vi.fn(() => of({ unreadCount: 0, readAt: '2026-05-26T21:00:00Z' })),
      markAllRead$: vi.fn(() => of({ unreadCount: 0, readAt: '2026-05-26T21:00:00Z' })),
    };

    await TestBed.configureTestingModule({
      imports: [TopbarComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: authService },
        { provide: AdminNotificationsService, useValue: notificationsService },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(TopbarComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('opens the profile dropdown from the header trigger', () => {
    openProfileMenu();

    expect(fixture.nativeElement.querySelector('#admin-profile-menu')).toBeTruthy();
    expect(profileTrigger().getAttribute('aria-expanded')).toBe('true');
  });

  it('closes the profile dropdown when clicking outside', () => {
    openProfileMenu();

    document.body.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('#admin-profile-menu')).toBeNull();
    expect(profileTrigger().getAttribute('aria-expanded')).toBe('false');
  });

  it('closes the profile dropdown with Escape', () => {
    openProfileMenu();

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('#admin-profile-menu')).toBeNull();
  });

  it('shows current user name, email, avatar, and active view', () => {
    openProfileMenu();
    const menu = fixture.nativeElement.querySelector('#admin-profile-menu') as HTMLElement;

    expect(menu.textContent).toContain('Mika Mikic');
    expect(menu.textContent).toContain('mika@example.com');
    expect(menu.textContent).toContain('Territory Manager');
    expect(menu.querySelector('img')?.getAttribute('src')).toBe('/media/uploads/avatars/mika.png');
  });

  it('renders the current auth user avatar with a cache-busting version', () => {
    currentUser = {
      ...currentUser,
      avatarUrl: '/media/uploads/avatars/current.png',
      avatarVersion: 101,
    };

    expect(component.displayAvatarUrl).toBe('/media/uploads/avatars/current.png?v=101');
  });

  it('navigates to the settings route for the active view', () => {
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

    openProfileMenu();
    clickMenuButton('Settings');

    expect(navigateSpy).toHaveBeenCalledWith('/settings');
    expect(fixture.nativeElement.querySelector('#admin-profile-menu')).toBeNull();
  });

  it('logs out through the real auth service flow and redirects to login', () => {
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

    openProfileMenu();
    clickMenuButton('Logout');

    expect(authService.logout).toHaveBeenCalledTimes(1);
    expect(navigateSpy).toHaveBeenCalledWith('/login');
  });

  it('hides the switch view section for a single supported admin view', () => {
    openProfileMenu();

    expect(fixture.nativeElement.textContent).not.toContain('Switch view');
  });

  it('shows switch view options for a multi-role admin user', () => {
    useMultiRoleUser();
    openProfileMenu();

    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Switch view');
    expect(text).toContain('Territory Manager');
    expect(text).toContain('Regional Manager');
  });

  it('selects the Regional Manager view and navigates to the regional dashboard', () => {
    useMultiRoleUser();
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    openProfileMenu();

    clickMenuButton('Regional Manager');
    fixture.detectChanges();

    expect(authService.switchAdminView).toHaveBeenCalledWith('RegionalManager');
    expect(navigateSpy).toHaveBeenCalledWith('/dashboard');
    expect(component.displayUserRole).toBe('Regional Manager');
  });

  it('selects the Territory Manager view and navigates to territory overview', () => {
    useMultiRoleUser(regionalManagerView);
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    openProfileMenu();

    clickMenuButton('Territory Manager');

    expect(authService.switchAdminView).toHaveBeenCalledWith('TerritoryManager');
    expect(navigateSpy).toHaveBeenCalledWith('/territory/overview');
  });

  it('marks the active view with a checked menu item and text', () => {
    useMultiRoleUser();
    openProfileMenu();

    const activeItem = fixture.nativeElement.querySelector('[role="menuitemradio"][aria-checked="true"]') as HTMLElement;

    expect(activeItem).toBeTruthy();
    expect(activeItem.textContent).toContain('Territory Manager');
    expect(activeItem.textContent).toContain('Current');
  });

  it('opens the Administrator notification drawer from the bell', () => {
    createAdministratorComponent();

    const bell = notificationBell();
    expect(bell.disabled).toBe(false);

    bell.click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('#admin-notification-drawer')).toBeTruthy();
    expect(fixture.nativeElement.textContent).toContain('New support report');
    expect(loadingSkeleton()).toBeNull();
    expect(notificationsService.getNotifications$).toHaveBeenCalledWith({
      includeRead: true,
      skip: 0,
      take: 25,
    });
  });

  it('opens the Territory Manager notification drawer from the bell', () => {
    notificationsService.getNotifications$.mockReturnValue(of(notificationPage([
      supportNotification({
        type: 'ContributionPendingReview',
        title: 'New request pending review',
        message: 'New place request for Harbor View is pending in Dalmatia.',
        targetType: 'Contribution',
        targetId: 77,
        targetRoute: '/territory/regions/5?highlight=contribution%3A77',
        scopeLabel: 'Dalmatia, Croatia',
      }),
    ], 1)));

    const bell = notificationBell();
    expect(bell.disabled).toBe(false);

    bell.click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('#admin-notification-drawer')).toBeTruthy();
    expect(drawerText()).toContain('New request pending review');
    expect(drawerText()).toContain('Dalmatia, Croatia');
    expect(notificationsService.getNotifications$).toHaveBeenCalledWith({
      audienceRole: 'TerritoryManager',
      includeRead: true,
      skip: 0,
      take: 25,
    });
  });

  it('opens the Regional Manager notification drawer for the active region', () => {
    notificationsService.getNotifications$.mockReturnValue(of(notificationPage([
      supportNotification({
        type: 'ContributionPendingReview',
        title: 'New request pending review',
        message: 'Place request for Harbor View was submitted in Dalmatia.',
        targetType: 'Contribution',
        targetId: 77,
        targetRoute: '/request/detail/contribution/77?regionId=7',
        scopeType: 'Region',
        scopeId: 7,
        scopeLabel: 'Dalmatia, Croatia',
      }),
    ], 1)));
    createRegionalManagerComponent(7);

    const bell = notificationBell();
    expect(bell.disabled).toBe(false);

    bell.click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('#admin-notification-drawer')).toBeTruthy();
    expect(drawerText()).toContain('New request pending review');
    expect(drawerText()).toContain('Dalmatia, Croatia');
    expect(notificationsService.getUnreadCount$).toHaveBeenCalledWith('RegionalManager', 7);
    expect(notificationsService.getNotifications$).toHaveBeenCalledWith({
      audienceRole: 'RegionalManager',
      regionId: 7,
      includeRead: true,
      skip: 0,
      take: 25,
    });
  });

  it('opens the Content Moderator notification drawer for review reports', () => {
    notificationsService.getNotifications$.mockReturnValue(of(notificationPage([
      supportNotification({
        type: 'ReviewReported',
        title: 'Reported review needs moderation',
        message: 'A review was reported as spam.',
        targetType: 'ReviewReport',
        targetId: 88,
        targetRoute: '/content-moderator/review-management?status=reported&highlight=Place%3A88',
      }),
    ], 1)));
    createContentModeratorComponent();

    notificationBell().click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('#admin-notification-drawer')).toBeTruthy();
    expect(drawerText()).toContain('Reported review needs moderation');
    expect(notificationsService.getUnreadCount$).toHaveBeenCalledWith('ContentModerator', null);
    expect(notificationsService.getNotifications$).toHaveBeenCalledWith({
      audienceRole: 'ContentModerator',
      includeRead: true,
      skip: 0,
      take: 25,
    });
  });

  it('navigates Content Moderator review notifications to Review Management', () => {
    notificationsService.getNotifications$.mockReturnValue(of(notificationPage([
      supportNotification({
        id: 88,
        type: 'ReviewReported',
        title: 'Reported review needs moderation',
        message: 'A review was reported as spam.',
        targetType: 'ReviewReport',
        targetId: 88,
        targetRoute: null,
      }),
    ], 1)));
    createContentModeratorComponent();
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

    notificationBell().click();
    fixture.detectChanges();
    notificationItemButton('Reported review needs moderation').click();
    fixture.detectChanges();

    expect(notificationsService.markRead$).toHaveBeenCalledWith(88, 'ContentModerator', null);
    expect(navigateSpy).toHaveBeenCalledWith('/content-moderator/review-management?status=reported');
  });

  it('shows the Content Moderator empty notification copy', () => {
    notificationsService.getNotifications$.mockReturnValue(of(notificationPage([], 0)));
    createContentModeratorComponent();

    notificationBell().click();
    fixture.detectChanges();

    expect(drawerText()).toContain('No notifications yet');
    expect(drawerText()).toContain("You're all caught up. New updates will appear here.");
  });

  it('navigates Regional Manager request notifications to the regional request detail', () => {
    notificationsService.getNotifications$.mockReturnValue(of(notificationPage([
      supportNotification({
        id: 77,
        type: 'ContributionPendingReview',
        title: 'New request pending review',
        message: 'Place request for Harbor View was submitted in Dalmatia.',
        targetType: 'Contribution',
        targetId: 77,
        targetRoute: null,
        scopeType: 'Region',
        scopeId: 7,
      }),
    ], 1)));
    createRegionalManagerComponent(7);
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

    notificationBell().click();
    fixture.detectChanges();
    notificationItemButton('New request pending review').click();
    fixture.detectChanges();

    expect(notificationsService.markRead$).toHaveBeenCalledWith(77, 'RegionalManager', 7);
    expect(navigateSpy).toHaveBeenCalledWith('/request/detail/contribution/77?regionId=7');
  });

  it('pushes a single history entry then replaces it when opening notifications back to back', () => {
    notificationsService.getNotifications$.mockReturnValue(of(notificationPage([
      supportNotification({
        id: 101,
        title: 'First region update',
        targetType: 'Region',
        targetId: 5,
        targetRoute: '/territory/regions/5',
      }),
      supportNotification({
        id: 102,
        title: 'Second region update',
        targetType: 'Region',
        targetId: 7,
        targetRoute: '/territory/regions/7',
      }),
    ], 2)));

    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

    notificationBell().click();
    fixture.detectChanges();
    notificationItemButton('First region update').click();
    fixture.detectChanges();

    // The first notification keeps a normal history entry, so back returns to the origin page.
    expect(navigateSpy).toHaveBeenLastCalledWith('/territory/regions/5');

    // The browser is now sitting on the first notification's destination.
    vi.spyOn(router, 'url', 'get').mockReturnValue('/territory/regions/5');

    notificationBell().click();
    fixture.detectChanges();
    notificationItemButton('Second region update').click();
    fixture.detectChanges();

    // Opening another notification from that same page replaces the entry instead of stacking it.
    expect(navigateSpy).toHaveBeenLastCalledWith('/territory/regions/7', { replaceUrl: true });
  });

  it('shows the Regional Manager empty state copy', () => {
    notificationsService.getNotifications$.mockReturnValue(of(notificationPage([], 0)));
    createRegionalManagerComponent(7);

    notificationBell().click();
    fixture.detectChanges();

    expect(drawerText()).toContain('No notifications yet');
    expect(drawerText()).toContain("You're all caught up. New updates will appear here.");
  });

  it('opens the Territory Associate notification drawer through the shared territory audience', () => {
    notificationsService.getNotifications$.mockReturnValue(of(notificationPage([
      supportNotification({
        type: 'ContributionPendingReview',
        title: 'Associate request update',
        message: 'A territory request is ready to review.',
        targetType: 'Contribution',
      }),
    ], 1)));
    createTerritoryAssociateComponent();

    notificationBell().click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('#admin-notification-drawer')).toBeTruthy();
    expect(drawerText()).toContain('Associate request update');
    expect(notificationsService.getUnreadCount$).toHaveBeenCalledWith('TerritoryAssociate', null);
    expect(notificationsService.getNotifications$).toHaveBeenCalledWith({
      audienceRole: 'TerritoryAssociate',
      includeRead: true,
      skip: 0,
      take: 25,
    });
  });

  it('shows skeleton cards while Administrator notifications are loading', () => {
    const pending = new Subject<AdminNotificationPage>();
    notificationsService.getNotifications$.mockReturnValue(pending.asObservable());

    openNotificationDrawer();

    expect(loadingSkeleton()).toBeTruthy();
    expect(drawerText()).not.toContain('No notifications yet');

    pending.complete();
  });

  it('shows notifications when the API returns items', () => {
    notificationsService.getNotifications$.mockReturnValue(of(notificationPage([
      supportNotification({ title: 'Reported review needs attention', type: 'ReviewReported', targetType: 'ReviewReport' }),
    ], 1)));

    openNotificationDrawer();

    expect(drawerText()).toContain('Reported review needs attention');
    expect(loadingSkeleton()).toBeNull();
    expect(drawerText()).not.toContain('No notifications yet');
  });

  it('shows real notifications on the initial All tab after a pending request resolves', () => {
    const pending = new Subject<AdminNotificationPage>();
    notificationsService.getNotifications$.mockReturnValue(pending.asObservable());

    openNotificationDrawer();
    expect(loadingSkeleton()).toBeTruthy();

    pending.next(notificationPage([
      supportNotification({ title: 'Initial all notification' }),
    ], 1));
    pending.complete();
    fixture.detectChanges();

    expect(component.notificationFilter).toBe('all');
    expect(component.notificationState).toBe('success');
    expect(loadingSkeleton()).toBeNull();
    expect(drawerText()).toContain('Initial all notification');
  });

  it('keeps All on the canonical item list after switching to Unread and back', () => {
    notificationsService.getNotifications$.mockReturnValue(of(notificationPage([
      supportNotification({ title: 'Unread request', isRead: false }),
      supportNotification({ id: 8, title: 'Read request', isRead: true, readAt: '2026-05-26T21:00:00Z' }),
    ], 1)));

    openNotificationDrawer();
    expect(drawerText()).toContain('Unread request');
    expect(drawerText()).toContain('Read request');

    clickDrawerTab('Unread');
    expect(drawerText()).toContain('Unread request');
    expect(drawerText()).not.toContain('Read request');
    expect(loadingSkeleton()).toBeNull();

    clickDrawerTab('All');
    expect(component.notificationFilter).toBe('all');
    expect(loadingSkeleton()).toBeNull();
    expect(drawerText()).toContain('Unread request');
    expect(drawerText()).toContain('Read request');
  });

  it('recreates the drawer after refresh and renders All notifications without a stuck skeleton', () => {
    notificationsService.getNotifications$.mockReturnValue(of(notificationPage([
      supportNotification({ title: 'Fresh page notification' }),
    ], 1)));

    createAdministratorComponent();
    notificationBell().click();
    fixture.detectChanges();

    expect(component.notificationFilter).toBe('all');
    expect(component.notificationState).toBe('success');
    expect(loadingSkeleton()).toBeNull();
    expect(drawerText()).toContain('Fresh page notification');
  });

  it('shows the all-notifications empty state when the API returns an empty list', () => {
    notificationsService.getNotifications$.mockReturnValue(of(notificationPage([], 0)));

    openNotificationDrawer();

    expect(loadingSkeleton()).toBeNull();
    expect(drawerText()).toContain('No notifications yet');
    expect(drawerText()).toContain("You're all caught up. New updates will appear here.");
  });

  it('shows the unread empty state without reloading when no unread notifications exist', () => {
    notificationsService.getNotifications$.mockReturnValue(of(notificationPage([
      supportNotification({ isRead: true, readAt: '2026-05-26T21:00:00Z' }),
    ], 0)));

    openNotificationDrawer();
    clickDrawerTab('Unread');

    expect(notificationsService.getNotifications$).toHaveBeenCalledTimes(1);
    expect(loadingSkeleton()).toBeNull();
    expect(drawerText()).toContain('No unread notifications');
    expect(drawerText()).toContain("You're all caught up.");
  });

  it('does not show skeleton cards after loading completes with no notifications', () => {
    notificationsService.getNotifications$.mockReturnValue(of(notificationPage([], 0)));

    openNotificationDrawer();

    expect(loadingSkeleton()).toBeNull();
    expect(drawerText()).toContain('No notifications yet');
  });

  it('shows an error state with Retry when notifications fail to load', () => {
    notificationsService.getNotifications$.mockReturnValue(throwError(() => new Error('failed')));

    openNotificationDrawer();

    expect(loadingSkeleton()).toBeNull();
    expect(drawerText()).toContain('Unable to load notifications. Please try again.');
    expect(drawerButton('Retry')).toBeTruthy();
  });

  it('does not remain stuck in loading when the notification service throws synchronously', () => {
    notificationsService.getNotifications$.mockImplementation(() => {
      throw new Error('sync failure');
    });

    openNotificationDrawer();

    expect(loadingSkeleton()).toBeNull();
    expect(component.notificationsLoading).toBe(false);
    expect(drawerText()).toContain('Unable to load notifications. Please try again.');
  });

  it('retries the notification request after an API error', () => {
    notificationsService.getNotifications$
      .mockReturnValueOnce(throwError(() => new Error('failed')))
      .mockReturnValueOnce(of(notificationPage([
        supportNotification({ title: 'Recovered notification' }),
      ], 1)));

    openNotificationDrawer();
    drawerButton('Retry')?.click();
    fixture.detectChanges();

    expect(notificationsService.getNotifications$).toHaveBeenCalledTimes(2);
    expect(loadingSkeleton()).toBeNull();
    expect(drawerText()).toContain('Recovered notification');
    expect(drawerText()).not.toContain('Unable to load notifications. Please try again.');
  });

  it('cancels drawer loading state when the drawer closes before the request completes', () => {
    const pending = new Subject<AdminNotificationPage>();
    notificationsService.getNotifications$.mockReturnValue(pending.asObservable());

    openNotificationDrawer();
    expect(loadingSkeleton()).toBeTruthy();

    closeNotificationDrawerButton().click();
    fixture.detectChanges();

    expect(component.notificationsLoading).toBe(false);
    expect(fixture.nativeElement.querySelector('#admin-notification-drawer')).toBeNull();

    pending.complete();
  });

  it('disables Mark all as read when unread count is zero', () => {
    notificationsService.getNotifications$.mockReturnValue(of(notificationPage([], 0)));

    openNotificationDrawer();

    expect(markAllButton().disabled).toBe(true);
  });

  it('enables Mark all as read when unread count is positive and notifications are not loading', () => {
    unreadCountValue = 2;
    notificationsService.getNotifications$.mockReturnValue(of(notificationPage([supportNotification()], 2)));

    openNotificationDrawer();

    expect(markAllButton().disabled).toBe(false);
  });

  it('shows a loading state while marking all notifications read', () => {
    unreadCountValue = 1;
    const pending = new Subject<{ unreadCount: number; readAt: string }>();
    notificationsService.markAllRead$.mockReturnValue(pending.asObservable());

    openNotificationDrawer();
    markAllButton().click();
    fixture.detectChanges();

    expect(markAllButton().textContent).toContain('Saving...');
    expect(markAllButton().disabled).toBe(true);

    pending.next({ unreadCount: 0, readAt: '2026-05-26T21:00:00Z' });
    pending.complete();
  });

  it('marks loaded notifications as read and leaves All visible after Mark all as read succeeds', () => {
    unreadCountValue = 1;
    notificationsService.getNotifications$.mockReturnValue(of(notificationPage([
      supportNotification({ title: 'Unread before mark all', isRead: false }),
    ], 1)));

    openNotificationDrawer();
    markAllButton().click();
    fixture.detectChanges();

    expect(component.notifications.every(notification => notification.isRead)).toBe(true);
    expect(loadingSkeleton()).toBeNull();
    expect(drawerText()).toContain('Unread before mark all');

    clickDrawerTab('Unread');
    expect(drawerText()).toContain('No unread notifications');
  });

  function openProfileMenu(): void {
    profileTrigger().click();
    fixture.detectChanges();
  }

  function profileTrigger(): HTMLButtonElement {
    return fixture.nativeElement.querySelector('[aria-haspopup="menu"]') as HTMLButtonElement;
  }

  function clickMenuButton(label: string): void {
    const buttons = Array.from(
      fixture.nativeElement.querySelectorAll('#admin-profile-menu button'),
    ) as HTMLButtonElement[];
    const button = buttons.find(item => item.textContent?.includes(label));
    expect(button).toBeTruthy();
    button?.click();
    fixture.detectChanges();
  }

  function openNotificationDrawer(): void {
    createAdministratorComponent();
    notificationBell().click();
    fixture.detectChanges();
  }

  function createAdministratorComponent(): void {
    useAdministratorUser();
    fixture.destroy();
    fixture = TestBed.createComponent(TopbarComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function createRegionalManagerComponent(regionId: number): void {
    sessionStorage.setItem('geotrack_admin_active_region_id', String(regionId));
    useRegionalManagerUser();
    notificationsService.getUnreadCount$.mockClear();
    notificationsService.getNotifications$.mockClear();
    fixture.destroy();
    fixture = TestBed.createComponent(TopbarComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function createContentModeratorComponent(): void {
    useContentModeratorUser();
    notificationsService.getUnreadCount$.mockClear();
    notificationsService.getNotifications$.mockClear();
    fixture.destroy();
    fixture = TestBed.createComponent(TopbarComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function createTerritoryAssociateComponent(): void {
    useTerritoryAssociateUser();
    notificationsService.getUnreadCount$.mockClear();
    notificationsService.getNotifications$.mockClear();
    fixture.destroy();
    fixture = TestBed.createComponent(TopbarComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function notificationBell(): HTMLButtonElement {
    return fixture.nativeElement.querySelector('[aria-controls="admin-notification-drawer"]') as HTMLButtonElement;
  }

  function loadingSkeleton(): HTMLElement | null {
    return fixture.nativeElement.querySelector('[aria-label="Loading notifications"]') as HTMLElement | null;
  }

  function drawerText(): string {
    return fixture.nativeElement.textContent ?? '';
  }

  function markAllButton(): HTMLButtonElement {
    return drawerButton('Mark all as read') ?? drawerButton('Saving...')!;
  }

  function closeNotificationDrawerButton(): HTMLButtonElement {
    return fixture.nativeElement.querySelector('[aria-label="Close notifications"]') as HTMLButtonElement;
  }

  function clickDrawerTab(label: string): void {
    drawerButton(label)?.click();
    fixture.detectChanges();
  }

  function drawerButton(label: string): HTMLButtonElement | null {
    const buttons = Array.from(
      fixture.nativeElement.querySelectorAll('#admin-notification-drawer button'),
    ) as HTMLButtonElement[];
    return buttons.find(button => button.textContent?.includes(label)) ?? null;
  }

  function notificationItemButton(label: string): HTMLButtonElement {
    const buttons = Array.from(
      fixture.nativeElement.querySelectorAll('#admin-notification-drawer button'),
    ) as HTMLButtonElement[];
    const button = buttons.find(item => item.textContent?.includes(label));
    expect(button).toBeTruthy();
    return button!;
  }

  function useMultiRoleUser(view: AdminView = territoryManagerView): void {
    activeView = view;
    availableViews = [territoryManagerView, regionalManagerView];
    currentUser = {
      ...currentUser,
      role: view.label,
      activeRoleCode: view.roleCode,
      roles: ['TerritoryManager', 'RegionalManager'],
    };
  }

  function buildUser(): User {
    return {
      id: '42',
      firstName: 'Mika',
      lastName: 'Mikic',
      email: 'mika@example.com',
      role: 'Territory Manager',
      activeRoleCode: 'TerritoryManager',
      roles: ['TerritoryManager'],
      region: '',
      memberSince: '',
      bio: '',
      language: 'en-US',
      timezone: 'Europe/Belgrade',
      notifyRequests: true,
      notifyNewReview: true,
      avatarUrl: '/media/uploads/avatars/mika.png',
    };
  }

  function useRegionalManagerUser(): void {
    activeView = regionalManagerView;
    availableViews = [regionalManagerView];
    currentUser = {
      ...currentUser,
      role: 'Regional Manager',
      activeRoleCode: 'RegionalManager',
      roles: ['RegionalManager'],
    };
  }

  function useAdministratorUser(): void {
    activeView = administratorView;
    availableViews = [administratorView];
    currentUser = {
      ...currentUser,
      role: 'Administrator',
      activeRoleCode: 'Admin',
      roles: ['Admin'],
    };
  }

  function useContentModeratorUser(): void {
    activeView = contentModeratorView;
    availableViews = [contentModeratorView];
    currentUser = {
      ...currentUser,
      firstName: 'Content',
      lastName: 'Moderator',
      email: 'moderator@gmail.com',
      role: 'Content Moderator',
      activeRoleCode: 'ContentModerator',
      roles: ['ContentModerator'],
    };
  }

  function useTerritoryAssociateUser(): void {
    activeView = territoryAssociateView;
    availableViews = [territoryAssociateView];
    currentUser = {
      ...currentUser,
      role: 'Territory Associate',
      activeRoleCode: 'TerritoryAssociate',
      roles: ['TerritoryAssociate'],
    };
  }

  function supportNotification(overrides: Partial<AdminNotification> = {}): AdminNotification {
    return {
      id: 7,
      type: 'SupportTicketCreated',
      title: 'New support report',
      message: 'Location and maps: Map issue',
      severity: 'Info',
      isRead: false,
      createdAt: '2026-05-26T20:00:00Z',
      targetType: 'SupportTicket',
      targetId: 42,
      targetRoute: '/admin-support-tickets?ticketId=42',
      ...overrides,
    };
  }

  function notificationPage(
    items: AdminNotification[],
    unreadCount = items.filter(notification => !notification.isRead).length,
  ): AdminNotificationPage {
    return {
      items,
      unreadCount,
      totalCount: items.length,
      total: items.length,
      skip: 0,
      take: 25,
      nextSkip: null,
      hasMore: false,
    };
  }
});
