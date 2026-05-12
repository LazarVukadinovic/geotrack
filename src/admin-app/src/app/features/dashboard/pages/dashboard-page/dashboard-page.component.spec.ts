import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { vi } from 'vitest';
import { DashboardComponent } from './dashboard-page.component';
import { RegionalDashboard } from '../../../../core/models/dashboard.model';

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
    flushRegions([{ id: 10, name: 'Dalmatia', displayName: 'Dalmatia, Croatia', territoryName: 'Croatia' }]);
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

  it('navigates stat cards with active region context', () => {
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

    fixture.detectChanges();
    flushRegions([{ id: 10, name: 'Dalmatia', displayName: 'Dalmatia, Croatia', territoryName: 'Croatia' }]);
    flushDashboard(10, dashboardResponse(10));

    component.navigateStat(component.stats[2]);

    expect(navigateSpy).toHaveBeenCalledWith('/places?regionId=10');
  });

  it('renders empty states for activity and request preview lists', () => {
    fixture.detectChanges();
    flushRegions([{ id: 10, name: 'Dalmatia', displayName: 'Dalmatia, Croatia', territoryName: 'Croatia' }]);
    flushDashboard(10, {
      ...dashboardResponse(10),
      requestQueuePreview: [],
      recentActivity: [],
    });
    fixture.changeDetectorRef.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('No recent activity for this region.');
    expect(text).toContain('No pending requests.');
  });

  function flushRegions(regions: Array<{ id: number; name: string; displayName: string; territoryName: string }>): void {
    const request = http.expectOne('/api/admin/regional/regions');
    expect(request.request.method).toBe('GET');
    request.flush(regions);
  }

  function flushDashboard(regionId: number, response: RegionalDashboard): void {
    const request = http.expectOne(req =>
      req.url === '/api/admin/regional/dashboard' &&
      req.params.get('regionId') === String(regionId));
    expect(request.request.method).toBe('GET');
    request.flush(response);
  }

  function dashboardResponse(regionId: number, displayName = 'Dalmatia, Croatia'): RegionalDashboard {
    return {
      activeRegion: { id: regionId, name: displayName.split(',')[0], displayName, territoryName: 'Croatia' },
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
