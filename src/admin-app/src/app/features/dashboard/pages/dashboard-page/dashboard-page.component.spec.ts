import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { vi } from 'vitest';
import { DashboardComponent } from './dashboard-page.component';
import { RegionalAssignedRegion, RegionalDashboard } from '../../../../core/models/dashboard.model';

describe('DashboardPageComponent', () => {
  let component: DashboardComponent;
  let fixture: ComponentFixture<DashboardComponent>;
  let http: HttpTestingController;

  beforeEach(async () => {
    sessionStorage.clear();

    await TestBed.configureTestingModule({
      imports: [DashboardComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(DashboardComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    sessionStorage.clear();
  });

  it('loads assigned regions and renders backend dashboard values', () => {
    fixture.detectChanges();
    flushRegions([
      { id: 10, name: 'Dalmatia', displayName: 'Dalmatia, Croatia', territoryName: 'Croatia' },
    ]);
    flushDashboard(10, dashboardResponse(10));
    fixture.changeDetectorRef.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(component.activeRegionId).toBe(10);
    expect(text).toContain('Dalmatia, Croatia');
    expect(text).toContain('11');
    expect(text).toContain('42');
    expect(text).toContain('Real Backend Place');
    expect(text).not.toContain('Mock Admin');
    expect(text).not.toContain('Vila Sumadija');
    expect(text).not.toContain('3,240');
    expect(text).not.toContain('148');
  });

  it('clears loading and renders dashboard data after successful HTTP responses', async () => {
    fixture.detectChanges();
    flushRegions([
      { id: 10, name: 'Dalmatia', displayName: 'Dalmatia, Croatia', territoryName: 'Croatia' },
    ]);
    flushDashboard(10, dashboardResponse(10));
    await fixture.whenStable();

    const text = fixture.nativeElement.textContent as string;
    expect(component.regionLoading).toBe(false);
    expect(component.dashboardLoading).toBe(false);
    expect(text).toContain('Dalmatia, Croatia');
    expect(text).toContain('Real Backend Place');
    expect(text).not.toContain('Loading regional dashboard...');
  });

  it('shows loading while assigned regions are loading', () => {
    fixture.detectChanges();
    fixture.changeDetectorRef.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(component.regionLoading).toBe(true);
    expect(text).toContain('Loading regional dashboard...');
    expect(text).toContain('Loading...');

    flushRegions([]);
  });

  it('selects the first assigned region by default and reloads when the region changes', () => {
    fixture.detectChanges();
    flushRegions([
      { id: 10, name: 'Dalmatia', displayName: 'Dalmatia, Croatia', territoryName: 'Croatia' },
      { id: 11, name: 'Istria', displayName: 'Istria, Croatia', territoryName: 'Croatia' },
    ]);
    flushDashboard(10, dashboardResponse(10));

    component.onRegionSelected(11);
    flushDashboard(11, dashboardResponse(11, 'Istria, Croatia'));

    expect(component.activeRegionId).toBe(11);
    expect(component.dashboard?.activeRegion.displayName).toBe('Istria, Croatia');
  });

  it('ignores stale persisted active region ids that are no longer assigned', () => {
    sessionStorage.setItem('geotrack_admin_active_region_id', '99');

    fixture.detectChanges();
    flushRegions([
      { id: 10, name: 'Dalmatia', displayName: 'Dalmatia, Croatia', territoryName: 'Croatia' },
      { id: 11, name: 'Istria', displayName: 'Istria, Croatia', territoryName: 'Croatia' },
    ]);
    flushDashboard(10, dashboardResponse(10));

    expect(component.activeRegionId).toBe(10);
    expect(sessionStorage.getItem('geotrack_admin_active_region_id')).toBe('10');
  });

  it('renders an empty state when no regions are assigned', () => {
    fixture.detectChanges();
    flushRegions([]);
    fixture.changeDetectorRef.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(component.regionLoading).toBe(false);
    expect(component.dashboardLoading).toBe(false);
    expect(component.activeRegionId).toBeNull();
    expect(text).toContain('No assigned regions');
    expect(text).not.toContain('Loading regional dashboard...');
  });

  it('clears loading and shows a safe error when assigned regions fail', () => {
    fixture.detectChanges();

    const request = http.expectOne('/api/admin/regional/regions');
    request.flush(
      { message: 'Unexpected failure details should not drive UI copy.' },
      { status: 500, statusText: 'Server Error' },
    );
    fixture.changeDetectorRef.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(component.regionLoading).toBe(false);
    expect(component.dashboardLoading).toBe(false);
    expect(component.dashboard).toBeNull();
    expect(text).toContain('Unable to load regional dashboard.');
    expect(text).not.toContain('Loading regional dashboard...');
  });

  it('clears loading and shows a safe error when dashboard loading fails', () => {
    fixture.detectChanges();
    flushRegions([
      { id: 10, name: 'Dalmatia', displayName: 'Dalmatia, Croatia', territoryName: 'Croatia' },
    ]);

    const request = http.expectOne(
      (req) => req.url === '/api/admin/regional/dashboard' && req.params.get('regionId') === '10',
    );
    request.flush({ message: 'Forbidden internals' }, { status: 403, statusText: 'Forbidden' });
    fixture.changeDetectorRef.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(component.regionLoading).toBe(false);
    expect(component.dashboardLoading).toBe(false);
    expect(component.dashboard).toBeNull();
    expect(text).toContain('This account is not authorized for the selected region.');
    expect(text).not.toContain('Loading regional dashboard...');
  });

  it('navigates stat cards with active region context', () => {
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

    fixture.detectChanges();
    flushRegions([
      { id: 10, name: 'Dalmatia', displayName: 'Dalmatia, Croatia', territoryName: 'Croatia' },
    ]);
    flushDashboard(10, dashboardResponse(10));

    component.navigateStat(component.stats[2]);

    expect(navigateSpy).toHaveBeenCalledWith('/places?regionId=10');
  });

  it('renders empty states for activity and request preview lists', () => {
    fixture.detectChanges();
    flushRegions([
      { id: 10, name: 'Dalmatia', displayName: 'Dalmatia, Croatia', territoryName: 'Croatia' },
    ]);
    flushDashboard(10, {
      ...dashboardResponse(10),
      requestQueuePreview: [],
      recentActivity: [],
    });
    fixture.changeDetectorRef.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('No recent activity.');
    expect(text).toContain('No pending requests.');
  });

  function flushRegions(regions: RegionalAssignedRegion[]): void {
    const request = http.expectOne('/api/admin/regional/regions');
    expect(request.request.method).toBe('GET');
    request.flush(regions);
  }

  function flushDashboard(regionId: number, response: RegionalDashboard): void {
    const request = http.expectOne(
      (req) =>
        req.url === '/api/admin/regional/dashboard' &&
        req.params.get('regionId') === String(regionId),
    );
    expect(request.request.method).toBe('GET');
    request.flush(response);
  }

  function dashboardResponse(
    regionId: number,
    displayName = 'Dalmatia, Croatia',
  ): RegionalDashboard {
    return {
      activeRegion: {
        id: regionId,
        name: displayName.split(',')[0],
        displayName,
        territoryName: 'Croatia',
      },
      stats: {
        pendingRequests: 11,
        totalDestinations: 8,
        destinationsAddedLastMonth: 2,
        totalPlaces: 42,
        placesAddedLastMonth: 5,
        totalEvents: 6,
        eventsAddedLastMonth: 1,
      },
      requestQueuePreview: [
        {
          requestId: 100,
          title: 'Real Backend Place',
          submittedByDisplayName: 'Backend Contributor',
          entityType: 'Place',
          contributionType: 'CreatePlace',
          submittedAt: new Date().toISOString(),
          status: 'PendingReview',
          thumbnailImageUrl: null,
          detailRoute: '/request/detail/100',
        },
      ],
      recentActivity: [
        {
          id: 'place-1',
          entityType: 'place',
          entityId: 1,
          action: 'Created place',
          description: 'Place added: Real Backend Place',
          occurredAt: new Date().toISOString(),
          status: 'Created',
          detailRoute: '/places/detail/1',
        },
      ],
    };
  }
});
