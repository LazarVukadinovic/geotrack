import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, map, switchMap, throwError } from 'rxjs';
import {
  TerritoryAssignedTerritory,
  RegionSyncSummary,
  TerritoryRegionCreateContext,
  TerritoryRegionCreateRequest,
  TerritoryRegionCreateResponse,
  TerritoryRegionContentResponse,
  TerritoryRegionDetail,
  TerritoryRegionDirectoryItem,
} from '../../models/dashboard.model';
import { TerritoryDashboardService, TerritoryRegionContentRequest } from '../territory-dashboard.service';

export type RegionSortValue =
  | 'status'
  | 'name'
  | 'nameDesc'
  | 'mostDestinations'
  | 'mostLocations'
  | 'mostPlaces'
  | 'mostEvents'
  | 'mostPending'
  | 'healthHigh'
  | 'healthLow';

export interface RegionListItem {
  id: string;
  regionId: number;
  name: string;
  description: string;
  territoryId: number;
  territoryDisplayName: string;
  destinations: number;
  locations: number;
  places: number;
  stays: number;
  events: number;
  assignedPersons: number;
  managerNames: string[];
  managerDisplayName?: string | null;
  assignedManagers: TerritoryRegionDirectoryItem['assignedManagers'];
  status: 'Active' | 'Inactive';
  representativeImage?: TerritoryRegionDirectoryItem['representativeImage'];
  imageUrl?: string | null;
  imageBg: string;
  pendingRequests: number;
  healthScore?: number | null;
  health?: TerritoryRegionDirectoryItem['health'];
  activityScore: number;
  detailRoute?: string | null;
}

export interface RegionPage {
  territory?: TerritoryAssignedTerritory | null;
  items: RegionListItem[];
  total: number;
  skip: number;
  take: number;
  hasMore: boolean;
}

export interface RegionPageRequest {
  territoryId?: number | null;
  skip: number;
  take: number;
  search: string;
  sort: RegionSortValue;
}

export interface RegionListState {
  searchQuery: string;
  sort: RegionSortValue;
  loadedCount: number;
  scrollTop: number;
}

@Injectable({ providedIn: 'root' })
export class RegionsService {
  readonly lastPage$ = new BehaviorSubject<RegionPage | null>(null);

  private listState: RegionListState | null = null;

  constructor(
    private readonly territoryDashboard: TerritoryDashboardService,
    private readonly http: HttpClient,
  ) {}

  getActiveTerritoryId(): number | null {
    return this.territoryDashboard.getActiveTerritoryId();
  }

  setActiveTerritoryId(territoryId: number | null): void {
    this.territoryDashboard.setActiveTerritoryId(territoryId);
  }

  getPage$(req: RegionPageRequest): Observable<RegionPage> {
    return this.territoryDashboard
      .getRegions({
        territoryId: req.territoryId ?? this.territoryDashboard.getActiveTerritoryId(),
        search: req.search,
        sort: this.mapSort(req.sort),
        skip: req.skip,
        take: req.take,
      })
      .pipe(
        map(response => ({
          territory: response.territory,
          items: response.items.map(item => this.mapRegion(item)),
          total: response.totalCount,
          skip: response.skip,
          take: response.take,
          hasMore: response.hasMore,
        })),
        map(page => {
          this.lastPage$.next(page);
          return page;
        }),
      );
  }

  getDetail$(id: string, territoryId?: number | null): Observable<TerritoryRegionDetail> {
    const regionId = Number(id);
    return this.territoryDashboard.getRegionDetail(regionId, territoryId ?? this.getActiveTerritoryId());
  }

  getContent$(request: TerritoryRegionContentRequest): Observable<TerritoryRegionContentResponse> {
    return this.territoryDashboard.getRegionContent({
      ...request,
      territoryId: request.territoryId ?? this.getActiveTerritoryId(),
    });
  }

  getCreateContext$(territoryId?: number | null): Observable<TerritoryRegionCreateContext> {
    return this.territoryDashboard.getRegionCreateContext(territoryId ?? this.getActiveTerritoryId());
  }

  create$(request: TerritoryRegionCreateRequest): Observable<TerritoryRegionCreateResponse> {
    return this.territoryDashboard.createRegion(request);
  }

  syncFromAdm1$(
    territoryId?: number | null,
    options: { dryRun?: boolean; forceUpdateGeometry?: boolean } = {},
  ): Observable<RegionSyncSummary> {
    return this.territoryDashboard.syncRegionsFromAdm1(
      territoryId ?? this.getActiveTerritoryId(),
      options.dryRun ?? false,
      options.forceUpdateGeometry ?? false,
    );
  }

  delete$(_id?: string): Observable<void> {
    return throwError(() => new Error('Territory Manager can view regions but cannot delete them.'));
  }

  updateDetail$(
    id: string | number,
    patch: { name?: string | null; description?: string | null; summary?: string | null } = {},
  ): Observable<TerritoryRegionDetail> {
    const regionId = Number(id);
    if (!Number.isFinite(regionId) || regionId <= 0) {
      return throwError(() => new Error('A valid region id is required.'));
    }

    return this.http
      .put(`/api/regions/${regionId}`, {
        name: patch.name ?? undefined,
      })
      .pipe(switchMap(() => this.getDetail$(String(regionId))));
  }

  updateTeam$(): Observable<never> {
    return throwError(() => new Error('Territory Manager region pages are read-only.'));
  }

  saveListState(state: RegionListState): void {
    this.listState = state;
  }

  getListState(): RegionListState | null {
    return this.listState;
  }

  clearListState(): void {
    this.listState = null;
  }

  private mapRegion(item: TerritoryRegionDirectoryItem): RegionListItem {
    const managerNames = item.managerNames?.length
      ? item.managerNames
      : item.managerDisplayName
        ? [item.managerDisplayName]
        : [];
    const assignedPersons = item.assignedPersonsCount ?? item.activeManagersCount ?? managerNames.length;
    const status = (item.status || (assignedPersons > 0 ? 'Active' : 'Inactive')).toLowerCase() === 'active'
      ? 'Active'
      : 'Inactive';
    const representativeImage = item.representativeImage ?? null;

    return {
      id: String(item.regionId ?? item.id),
      regionId: item.regionId ?? item.id,
      name: item.displayName || item.regionName || item.name,
      description: item.description?.trim() || this.managerSummary(managerNames),
      territoryId: item.territoryId,
      territoryDisplayName: item.territoryDisplayName || item.territoryName,
      destinations: item.destinationsCount,
      locations: item.locationsCount,
      places: item.placesCount,
      stays: item.staysCount,
      events: item.eventsCount,
      assignedPersons,
      managerNames,
      managerDisplayName: item.managerDisplayName,
      assignedManagers: item.assignedManagers ?? [],
      status,
      representativeImage,
      imageUrl: representativeImage?.url ?? null,
      imageBg: this.imageBackground(item.regionId ?? item.id),
      pendingRequests: item.pendingRequestsCount,
      healthScore: item.healthScore ?? item.health?.score ?? null,
      health: item.health,
      activityScore: item.healthScore ?? item.health?.score ?? item.activityScore,
      detailRoute: item.detailRoute,
    };
  }

  private managerSummary(managerNames: string[]): string {
    if (managerNames.length === 0) {
      return 'No active Regional Manager assigned';
    }

    if (managerNames.length === 1) {
      return `Regional Manager: ${managerNames[0]}`;
    }

    return `Regional Managers: ${managerNames[0]} +${managerNames.length - 1}`;
  }

  private mapSort(sort: RegionSortValue): string {
    switch (sort) {
      case 'name':
        return 'name';
      case 'nameDesc':
        return 'name-desc';
      case 'mostDestinations':
        return 'most-destinations';
      case 'mostLocations':
        return 'most-locations';
      case 'mostPlaces':
        return 'most-places';
      case 'mostEvents':
        return 'most-events';
      case 'mostPending':
        return 'most-pending-requests';
      case 'healthHigh':
        return 'health-high';
      case 'healthLow':
        return 'health-low';
      case 'status':
      default:
        return 'status-active-first';
    }
  }

  private imageBackground(id: number): string {
    const variants = [
      'linear-gradient(135deg,#dbeafe 0%,#bfdbfe 100%)',
      'linear-gradient(135deg,#d1fae5 0%,#a7f3d0 100%)',
      'linear-gradient(135deg,#fef9c3 0%,#fde68a 100%)',
      'linear-gradient(135deg,#fce7f3 0%,#fbcfe8 100%)',
    ];
    return variants[Math.abs(id) % variants.length];
  }
}
