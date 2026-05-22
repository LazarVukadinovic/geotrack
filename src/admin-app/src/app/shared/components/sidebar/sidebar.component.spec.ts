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

describe('SidebarComponent', () => {
  let component: SidebarComponent;
  let fixture: ComponentFixture<SidebarComponent>;
  let http: HttpTestingController;
  let currentRole: string | null;

  const regions: RegionalAssignedRegion[] = [
    { id: 7, name: 'Backend Region', displayName: 'Backend Region' },
    { id: 11, name: 'Northern Region', displayName: 'Northern Region' },
  ];

  const authService = {
    getUser: vi.fn(() => currentRole ? { role: currentRole, region: '' } : null),
    isAdministrator: vi.fn(() => currentRole === 'Administrator'),
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
    await fixture.whenStable();
  });

  afterEach(() => {
    http.verify({ ignoreCancelled: true });
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

  it('emits a numeric region id when the selector changes', async () => {
    const regionChange = vi.spyOn(component.regionChange, 'emit');
    fixture.componentRef.setInput('regions', regions);
    fixture.componentRef.setInput('activeRegionId', 7);
    await fixture.whenStable();

    const select = fixture.nativeElement.querySelector('select') as HTMLSelectElement | null;
    expect(select).not.toBeNull();

    select!.value = '11';
    select!.dispatchEvent(new Event('change'));

    expect(regionChange).toHaveBeenCalledWith(11);
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

  it('does not attach the Regional Manager request badge to the Territory Manager sidebar', () => {
    currentRole = 'Territory Manager';
    fixture.componentRef.setInput('regions', [{ id: 3, name: 'Testland', displayName: 'Testland' }]);
    fixture.componentRef.setInput('activeRegionId', 3);
    fixture.detectChanges();

    http.expectNone(req => req.url === '/api/admin/regional/sidebar-summary');
    expect(normalizedText()).toContain('Territory Overview');
    expect(normalizedText()).not.toContain('Requests Queue');
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

  function normalizedText(): string {
    return (fixture.nativeElement.textContent as string).replace(/\s+/g, ' ').trim();
  }
});
