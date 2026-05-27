import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { vi } from 'vitest';

import { SidebarComponent } from './sidebar.component';
import { AuthService } from '../../../core/services/auth.service';
import { TerritoryDashboardService } from '../../../core/services/territory-dashboard.service';
import { RegionalAssignedRegion } from '../../../core/models/dashboard.model';
import { RegionalManagerSidebarStateService } from '../../../core/services/regional-manager-sidebar-state.service';

describe('SidebarComponent', () => {
  let component: SidebarComponent;
  let fixture: ComponentFixture<SidebarComponent>;
  let http: HttpTestingController;
  let currentRole: string | null;
  let regionalSidebarState: RegionalManagerSidebarStateService;

  const regions: RegionalAssignedRegion[] = [
    { id: 7, name: 'Backend Region', displayName: 'Backend Region' },
    { id: 11, name: 'Northern Region', displayName: 'Northern Region' },
  ];

  const authService = {
    getUser: vi.fn(() => currentRole ? { role: currentRole, activeRoleCode: activeRoleCode(currentRole), region: '' } : null),
    isAdministrator: vi.fn(() => currentRole === 'Administrator'),
    isContentModerator: vi.fn(() => currentRole === 'Content Moderator'),
    isRegionalManager: vi.fn(() => currentRole === 'Regional Manager'),
    canAccessTerritoryAdmin: vi.fn(() => currentRole === 'Territory Manager'),
    logout: vi.fn(),
  };

  const territoryDashboard = {
    getAssignedTerritories: vi.fn(() => of([{ id: 3, name: 'Testland', displayName: 'Testland' }])),
    getActiveTerritoryId: vi.fn(() => 3),
    setActiveTerritoryId: vi.fn(),
  };

  beforeEach(async () => {
    currentRole = null;
    vi.clearAllMocks();

    await TestBed.configureTestingModule({
      imports: [SidebarComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: authService },
        { provide: TerritoryDashboardService, useValue: territoryDashboard },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SidebarComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    regionalSidebarState = TestBed.inject(RegionalManagerSidebarStateService);
    regionalSidebarState.clear();
    clearPersistedRegion();
  });

  afterEach(() => {
    http.verify({ ignoreCancelled: true });
    regionalSidebarState.clear();
    clearPersistedRegion();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('shows an available active region label instead of loading forever', async () => {
    fixture.componentRef.setInput('region', 'Coastal Test Region, Testland');
    fixture.componentRef.setInput('regionLoading', true);
    await fixture.whenStable();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Coastal Test Region, Testland');
    expect(text).not.toContain('Loading...');
  });

  it('opens the custom picker and emits a numeric region id when an option is selected', async () => {
    const regionChange = vi.spyOn(component.regionChange, 'emit');
    fixture.componentRef.setInput('regions', regions);
    fixture.componentRef.setInput('activeRegionId', 7);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('select')).toBeNull();

    const trigger = pickerTrigger();
    expect(trigger).not.toBeNull();
    trigger!.click();
    fixture.detectChanges();

    const menu = fixture.nativeElement.querySelector('[role="listbox"]') as HTMLElement | null;
    expect(menu).not.toBeNull();

    const options = pickerOptions();
    expect(options).toHaveLength(2);
    expect(options[0].getAttribute('aria-selected')).toBe('true');

    options[1].click();

    expect(regionChange).toHaveBeenCalledWith(11);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="listbox"]')).toBeNull();
  });

  it('keeps the region pill static when only one assigned region exists', () => {
    fixture.componentRef.setInput('regions', [{ id: 7, name: 'Only Region', displayName: 'Only Region' }]);
    fixture.componentRef.setInput('activeRegionId', 7);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('select')).toBeNull();
    expect(pickerTrigger()).toBeNull();

    component.toggleRegionPicker();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="listbox"]')).toBeNull();
  });

  it.each(['Dashboard', 'Destinations', 'Locations', 'Places', 'Events', 'Map', 'Analytics', 'Settings'])(
    'shows the Requests Queue badge on the %s Regional Manager sidebar when pending count is greater than zero',
    () => {
      currentRole = 'Regional Manager';
      fixture.componentRef.setInput('regions', regions);
      fixture.componentRef.setInput('activeRegionId', 7);
      fixture.detectChanges();
      flushSidebarSummary(7, 4);
      fixture.detectChanges();

      const text = normalizedText();
      expect(text).toContain('Requests Queue');
      expect(text).toContain('4');
    },
  );

  it('keeps the Requests Queue badge hidden when the backend count is zero', () => {
    currentRole = 'Regional Manager';
    fixture.componentRef.setInput('regions', regions);
    fixture.componentRef.setInput('activeRegionId', 7);
    fixture.detectChanges();
    flushSidebarSummary(7, 0);
    fixture.detectChanges();

    const queueLink = Array.from(fixture.nativeElement.querySelectorAll('a') as NodeListOf<HTMLAnchorElement>)
      .find((link): link is HTMLAnchorElement => link.textContent?.includes('Requests Queue') ?? false);

    expect(queueLink?.textContent?.replace(/\s+/g, ' ').trim()).toBe('Requests Queue');
  });

  it('reloads the Requests Queue badge when the active region changes', () => {
    currentRole = 'Regional Manager';
    fixture.componentRef.setInput('regions', regions);
    fixture.componentRef.setInput('activeRegionId', 7);
    fixture.detectChanges();
    flushSidebarSummary(7, 4);

    fixture.componentRef.setInput('activeRegionId', 11);
    fixture.detectChanges();
    flushSidebarSummary(11, 2);
    fixture.detectChanges();

    expect(normalizedText()).toContain('2');
  });

  it('loads assigned regions into the Regional Manager selector when no region list input is provided', () => {
    currentRole = 'Regional Manager';
    const regionChange = vi.spyOn(component.regionChange, 'emit');
    fixture.detectChanges();
    flushSidebarSummaryWithoutRegion(7, 4);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('select')).toBeNull();

    pickerTrigger()?.click();
    fixture.detectChanges();

    expect(pickerOptionLabels()).toEqual([
      'Backend Region',
      'Northern Region',
    ]);
    expect(pickerOptions()[0].getAttribute('aria-selected')).toBe('true');

    pickerOptions()[1].click();
    expect(regionChange).toHaveBeenCalledWith(11);
    flushSidebarSummary(11, 2);
  });

  it('closes the custom picker with Escape and outside clicks', () => {
    fixture.componentRef.setInput('regions', regions);
    fixture.componentRef.setInput('activeRegionId', 7);
    fixture.detectChanges();

    pickerTrigger()?.click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="listbox"]')).not.toBeNull();

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="listbox"]')).toBeNull();

    pickerTrigger()?.click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="listbox"]')).not.toBeNull();

    document.body.click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="listbox"]')).toBeNull();
  });

  it('opens the custom picker from the keyboard and marks the active option', () => {
    fixture.componentRef.setInput('regions', regions);
    fixture.componentRef.setInput('activeRegionId', 7);
    fixture.detectChanges();

    pickerTrigger()?.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown' }));
    fixture.detectChanges();

    const options = pickerOptions();
    expect(options).toHaveLength(2);
    expect(options[0].classList.contains('region-picker__option--active')).toBe(true);
    expect(options[0].getAttribute('aria-selected')).toBe('true');
  });

  it('does not attach the Regional Manager request badge to the Territory Manager sidebar', () => {
    currentRole = 'Territory Manager';
    fixture.componentRef.setInput('regions', [{ id: 3, name: 'Testland', displayName: 'Testland' }]);
    fixture.componentRef.setInput('activeRegionId', 3);
    fixture.detectChanges();

    http.expectNone(req => req.url === '/api/admin/regional/sidebar-summary');
    expect(normalizedText()).toContain('Territory Overview');
    expect(normalizedText()).not.toContain('Requests Queue');
  });

  it('shows the Administrator Support Reports badge from the provided unresolved support count', () => {
    currentRole = 'Administrator';
    fixture.componentRef.setInput('supportReportsCount', 7);
    fixture.detectChanges();

    const text = normalizedText();
    expect(text).not.toContain('Review Management');
    expect(text).toContain('Support Reports');
    expect(text).toContain('7');
  });

  it('shows only moderation navigation and review badge for Content Moderator', () => {
    currentRole = 'Content Moderator';
    fixture.componentRef.setInput('reportedReviewsCount', 3);
    fixture.detectChanges();

    const text = normalizedText();
    expect(text).toContain('CONTENT MODERATION');
    expect(text).toContain('Dashboard');
    expect(text).toContain('Review Management');
    expect(text).toContain('3');
    expect(text).toContain('Settings');
    expect(text).toContain('Sign out');
    expect(text).not.toContain('Support Reports');
    expect(text).not.toContain('Territory Management');
    expect(text).not.toContain('User Management');
  });

  function flushSidebarSummary(regionId: number, pendingRequestsCount: number): void {
    const request = http.expectOne(req =>
      req.url === '/api/admin/regional/sidebar-summary' &&
      req.params.get('regionId') === String(regionId),
    );
    expect(request.request.method).toBe('GET');
    request.flush({
      assignedRegions: regions,
      activeRegion: regions.find(region => region.id === regionId) ?? regions[0],
      pendingRequestsCount,
    });
  }

  function flushSidebarSummaryWithoutRegion(regionId: number, pendingRequestsCount: number): void {
    const request = http.expectOne(req =>
      req.url === '/api/admin/regional/sidebar-summary' &&
      !req.params.has('regionId'),
    );
    expect(request.request.method).toBe('GET');
    request.flush({
      assignedRegions: regions,
      activeRegion: regions.find(region => region.id === regionId) ?? regions[0],
      pendingRequestsCount,
    });
  }

  function normalizedText(): string {
    return (fixture.nativeElement.textContent as string).replace(/\s+/g, ' ').trim();
  }

  function pickerTrigger(): HTMLButtonElement | null {
    return fixture.nativeElement.querySelector('button[aria-haspopup="listbox"]') as HTMLButtonElement | null;
  }

  function pickerOptions(): HTMLButtonElement[] {
    return Array.from(
      fixture.nativeElement.querySelectorAll('.region-picker__option') as NodeListOf<HTMLButtonElement>,
    );
  }

  function pickerOptionLabels(): string[] {
    return Array.from(
      fixture.nativeElement.querySelectorAll('.region-picker__option-label') as NodeListOf<HTMLElement>,
    ).map(option => option.textContent?.trim() ?? '');
  }

  function clearPersistedRegion(): void {
    try {
      sessionStorage.removeItem('geotrack_admin_active_region_id');
    } catch {
      // Session storage is not always available in test hosts.
    }
  }

  function activeRoleCode(role: string): string {
    switch (role) {
      case 'Administrator':
        return 'Admin';
      case 'Content Moderator':
        return 'ContentModerator';
      case 'Territory Manager':
        return 'TerritoryManager';
      case 'Territory Associate':
        return 'TerritoryAssociate';
      case 'Regional Manager':
        return 'RegionalManager';
      default:
        return role;
    }
  }
});
