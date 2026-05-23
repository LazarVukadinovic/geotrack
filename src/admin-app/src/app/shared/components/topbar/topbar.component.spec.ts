import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { vi } from 'vitest';

import { TopbarComponent } from './topbar.component';
import { AuthService } from '../../../core/services/auth.service';
import { AdminView, getAdminViewByCode } from '../../../core/models/admin-view.model';
import { User } from '../../../core/models/user.model';

describe('TopbarComponent', () => {
  let component: TopbarComponent;
  let fixture: ComponentFixture<TopbarComponent>;
  let currentUser: User;
  let activeView: AdminView;
  let availableViews: AdminView[];
  let authService: {
    readonly fullName: string;
    getUser: ReturnType<typeof vi.fn>;
    getAvailableAdminViews: ReturnType<typeof vi.fn>;
    getActiveAdminView: ReturnType<typeof vi.fn>;
    switchAdminView: ReturnType<typeof vi.fn>;
    logout: ReturnType<typeof vi.fn>;
  };

  const territoryManagerView = getAdminViewByCode('TerritoryManager')!;
  const regionalManagerView = getAdminViewByCode('RegionalManager')!;

  beforeEach(async () => {
    currentUser = buildUser();
    activeView = territoryManagerView;
    availableViews = [territoryManagerView];
    authService = {
      get fullName(): string {
        return currentUser ? `${currentUser.firstName} ${currentUser.lastName}`.trim() : '';
      },
      getUser: vi.fn(() => currentUser),
      getAvailableAdminViews: vi.fn(() => availableViews),
      getActiveAdminView: vi.fn(() => activeView),
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

    await TestBed.configureTestingModule({
      imports: [TopbarComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: authService },
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
});
