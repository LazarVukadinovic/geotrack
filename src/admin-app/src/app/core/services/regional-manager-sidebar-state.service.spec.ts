import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { RegionalManagerSidebarStateService } from './regional-manager-sidebar-state.service';
import { RegionalAssignedRegion } from '../models/dashboard.model';

describe('RegionalManagerSidebarStateService', () => {
  let service: RegionalManagerSidebarStateService;
  let http: HttpTestingController;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(RegionalManagerSidebarStateService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify({ ignoreCancelled: true });
    sessionStorage.clear();
  });

  it('loads the real pending request badge count for the active region', () => {
    service.selectRegion(7, [region(7, 'North')]);
    flushSummary(7, 5, [region(7, 'North')]);

    expect(service.activeRegionId()).toBe(7);
    expect(service.activeRegion()?.displayName).toBe('North');
    expect(service.pendingRequestsCount()).toBe(5);
    expect(sessionStorage.getItem('geotrack_admin_active_region_id')).toBe('7');
  });

  it('reloads the badge count when the active region changes', () => {
    service.selectRegion(7, [region(7, 'North'), region(8, 'South')]);
    flushSummary(7, 5, [region(7, 'North'), region(8, 'South')]);

    service.selectRegion(8, [region(7, 'North'), region(8, 'South')]);
    expect(service.pendingRequestsCount()).toBeNull();
    flushSummary(8, 2, [region(7, 'North'), region(8, 'South')]);

    expect(service.activeRegionId()).toBe(8);
    expect(service.activeRegion()?.displayName).toBe('South');
    expect(service.pendingRequestsCount()).toBe(2);
  });

  it('refreshes the current region after moderation actions', () => {
    service.selectRegion(7, [region(7, 'North')]);
    flushSummary(7, 3, [region(7, 'North')]);

    service.refresh(7);
    flushSummary(7, 1, [region(7, 'North')]);

    expect(service.pendingRequestsCount()).toBe(1);
  });

  it('normalizes an empty summary to a zero count', () => {
    service.ensureLoaded();

    const request = http.expectOne('/api/admin/regional/sidebar-summary');
    expect(request.request.method).toBe('GET');
    request.flush({
      assignedRegions: [],
      activeRegion: null,
      pendingRequestsCount: null,
    });

    expect(service.activeRegionId()).toBeNull();
    expect(service.pendingRequestsCount()).toBe(0);
  });

  it('clears the visible badge count when the selected region is rejected', () => {
    service.selectRegion(99, [region(99, 'Outside')]);

    const request = http.expectOne(req =>
      req.url === '/api/admin/regional/sidebar-summary' &&
      req.params.get('regionId') === '99',
    );
    request.flush({ message: 'Forbidden.' }, { status: 403, statusText: 'Forbidden' });

    expect(service.pendingRequestsCount()).toBeNull();
    expect(service.error()).toBe('This account is not authorized for the selected region.');
  });

  function flushSummary(
    regionId: number,
    pendingRequestsCount: number,
    assignedRegions: RegionalAssignedRegion[],
  ): void {
    const request = http.expectOne(req =>
      req.url === '/api/admin/regional/sidebar-summary' &&
      req.params.get('regionId') === String(regionId),
    );
    expect(request.request.method).toBe('GET');
    request.flush({
      assignedRegions,
      activeRegion: assignedRegions.find(item => item.id === regionId) ?? null,
      pendingRequestsCount,
    });
  }

  function region(id: number, displayName: string): RegionalAssignedRegion {
    return { id, name: displayName, displayName };
  }
});
