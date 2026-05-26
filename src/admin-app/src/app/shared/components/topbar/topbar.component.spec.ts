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
  const territoryManagerView = getAdminViewByCode('TerritoryManager')!;
  const regionalManagerView = getAdminViewByCode('RegionalManager')!;

  beforeEach(async () => {
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

  it('shows the all-notifications empty state when the API returns an empty list', () => {
    notificationsService.getNotifications$.mockReturnValue(of(notificationPage([], 0)));

    openNotificationDrawer();

    expect(loadingSkeleton()).toBeNull();
    expect(drawerText()).toContain('No notifications yet');
    expect(drawerText()).toContain('New support reports, review reports, and system alerts will appear here.');
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
    expect(drawerText()).toContain('You are all caught up.');
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
