import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, Subject, shareReplay, tap } from 'rxjs';
import {
  BoundaryRegionCandidateResponse,
  RegionSyncSummary,
  TerritoryAssignedTerritory,
  TerritoryContentDirectoryResponse,
  TerritoryDashboardResponse,
  TerritoryRegionCreateContext,
  TerritoryRegionCreateRequest,
  TerritoryRegionCreateResponse,
  TerritoryRegionContentResponse,
  TerritoryRegionContentType,
  TerritoryRegionDetail,
  TerritoryRegionDirectoryResponse,
  TerritoryRegionMapResponse,
  TerritoryRegionSetupStatus,
  TerritorySidebarSummary,
} from '../models/dashboard.model';

export interface TerritoryDirectoryRequest {
  territoryId?: number | null;
  search?: string | null;
  sort?: string | null;
  skip?: number;
  take?: number;
}

export interface TerritoryContentDirectoryRequest extends TerritoryDirectoryRequest {
  type?: string | null;
}

export interface TerritoryRegionContentRequest {
  territoryId?: number | null;
  regionId: number;
  entityType?: TerritoryRegionContentType | string | null;
  search?: string | null;
  sort?: string | null;
  skip?: number;
  take?: number;
}

@Injectable({ providedIn: 'root' })
export class TerritoryDashboardService {
  private static readonly activeTerritoryStorageKey = 'geotrack_admin_active_territory_id';

  private assignedTerritories$?: Observable<TerritoryAssignedTerritory[]>;
  private readonly regionSetupChangedSubject = new Subject<void>();
  private readonly activeTerritoryChangedSubject = new Subject<number | null>();
  readonly regionSetupChanged$ = this.regionSetupChangedSubject.asObservable();
  readonly activeTerritoryChanged$ = this.activeTerritoryChangedSubject.asObservable();

  constructor(private readonly http: HttpClient) {}

  getAssignedTerritories(): Observable<TerritoryAssignedTerritory[]> {
    this.assignedTerritories$ ??= this.http
      .get<TerritoryAssignedTerritory[]>('/api/admin/territory/territories')
      .pipe(
        tap(territories => {
          const activeId = this.getActiveTerritoryId();
          if (!activeId && territories.length === 1) {
            this.setActiveTerritoryId(territories[0].id);
          }
        }),
        shareReplay(1),
      );

    return this.assignedTerritories$;
  }

  refreshAssignedTerritories(): void {
    this.assignedTerritories$ = undefined;
  }

  clearSessionState(): void {
    this.assignedTerritories$ = undefined;
    this.setActiveTerritoryId(null);
  }

  getSidebarSummary(territoryId?: number | null): Observable<TerritorySidebarSummary> {
    return this.http.get<TerritorySidebarSummary>('/api/admin/territory/sidebar-summary', {
      params: this.withTerritoryId(new HttpParams(), territoryId),
    });
  }

  getDashboard(territoryId?: number | null): Observable<TerritoryDashboardResponse> {
    return this.http.get<TerritoryDashboardResponse>('/api/admin/territory/dashboard', {
      params: this.withTerritoryId(new HttpParams(), territoryId),
    });
  }

  getRegions(request: TerritoryDirectoryRequest): Observable<TerritoryRegionDirectoryResponse> {
    let params = this.withTerritoryId(new HttpParams(), request.territoryId);
    params = this.applyDirectoryParams(params, request);

    return this.http.get<TerritoryRegionDirectoryResponse>('/api/admin/territory/regions', {
      params,
    });
  }

  getRegionSetupStatus(territoryId?: number | null): Observable<TerritoryRegionSetupStatus> {
    return this.http.get<TerritoryRegionSetupStatus>('/api/admin/territory/regions/setup-status', {
      params: this.withTerritoryId(new HttpParams(), territoryId),
    });
  }

  getRegionMap(territoryId?: number | null): Observable<TerritoryRegionMapResponse> {
    return this.http.get<TerritoryRegionMapResponse>('/api/admin/territory/regions/map', {
      params: this.withTerritoryId(new HttpParams(), territoryId),
    });
  }

  getRegionCreateContext(territoryId?: number | null): Observable<TerritoryRegionCreateContext> {
    return this.http.get<TerritoryRegionCreateContext>('/api/admin/territory/regions/create-context', {
      params: this.withTerritoryId(new HttpParams(), territoryId),
    });
  }

  getRegionBoundaryCandidates(
    territoryId?: number | null,
    level = 'ADM1',
  ): Observable<BoundaryRegionCandidateResponse> {
    let params = this.withTerritoryId(new HttpParams(), territoryId)
      .set('level', level);

    return this.http.get<BoundaryRegionCandidateResponse>('/api/admin/territory/regions/boundary-candidates', {
      params,
    });
  }

  createRegion(request: TerritoryRegionCreateRequest): Observable<TerritoryRegionCreateResponse> {
    return this.http.post<TerritoryRegionCreateResponse>('/api/admin/territory/regions', request);
  }

  syncRegionsFromAdm1(
    territoryId?: number | null,
    dryRun = false,
    forceUpdateGeometry = false,
  ): Observable<RegionSyncSummary> {
    const params = this.withTerritoryId(new HttpParams(), territoryId)
      .set('dryRun', String(dryRun))
      .set('forceUpdateGeometry', String(forceUpdateGeometry));

    return this.http.post<RegionSyncSummary>('/api/admin/territory/regions/sync-from-adm1', {}, { params });
  }

  notifyRegionSetupChanged(): void {
    this.regionSetupChangedSubject.next();
  }

  getRegionDetail(
    regionId: number,
    territoryId?: number | null,
    period?: string | null,
  ): Observable<TerritoryRegionDetail> {
    let params = this.withTerritoryId(new HttpParams(), territoryId);
    if (period?.trim()) {
      params = params.set('period', period.trim());
    }

    return this.http.get<TerritoryRegionDetail>(`/api/admin/territory/regions/${regionId}/detail`, {
      params,
    });
  }

  getRegionContent(request: TerritoryRegionContentRequest): Observable<TerritoryRegionContentResponse> {
    let params = this.withTerritoryId(new HttpParams(), request.territoryId);
    params = this.applyDirectoryParams(params, request);

    if (request.entityType) {
      params = params.set('entityType', request.entityType);
    }

    return this.http.get<TerritoryRegionContentResponse>(
      `/api/admin/territory/regions/${request.regionId}/content`,
      { params },
    );
  }

  getContent(request: TerritoryContentDirectoryRequest): Observable<TerritoryContentDirectoryResponse> {
    let params = this.withTerritoryId(new HttpParams(), request.territoryId);
    params = this.applyDirectoryParams(params, request);
    if (request.type) {
      params = params.set('type', request.type);
    }

    return this.http.get<TerritoryContentDirectoryResponse>('/api/admin/territory/content', {
      params,
    });
  }

  getActiveTerritoryId(): number | null {
    try {
      const raw = sessionStorage.getItem(TerritoryDashboardService.activeTerritoryStorageKey);
      const parsed = raw ? Number(raw) : NaN;
      return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
    } catch {
      return null;
    }
  }

  setActiveTerritoryId(territoryId: number | null): void {
    const previous = this.getActiveTerritoryId();
    const next = territoryId && territoryId > 0 ? territoryId : null;

    try {
      if (next) {
        sessionStorage.setItem(TerritoryDashboardService.activeTerritoryStorageKey, String(next));
      } else {
        sessionStorage.removeItem(TerritoryDashboardService.activeTerritoryStorageKey);
      }
    } catch {
      // Active territory persistence is a UI convenience only.
    }

    if (previous !== next) {
      this.activeTerritoryChangedSubject.next(next);
    }
  }

  private withTerritoryId(params: HttpParams, territoryId?: number | null): HttpParams {
    return territoryId && territoryId > 0
      ? params.set('territoryId', String(territoryId))
      : params;
  }

  private applyDirectoryParams(
    params: HttpParams,
    request: TerritoryDirectoryRequest,
  ): HttpParams {
    if (request.search?.trim()) {
      params = params.set('search', request.search.trim());
    }

    if (request.sort?.trim()) {
      params = params.set('sort', request.sort.trim());
    }

    if (request.skip !== undefined) {
      params = params.set('skip', String(Math.max(0, request.skip)));
    }

    if (request.take !== undefined) {
      params = params.set('take', String(Math.max(1, request.take)));
    }

    return params;
  }
}
