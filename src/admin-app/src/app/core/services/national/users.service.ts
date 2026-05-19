import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable, map, tap } from 'rxjs';
import { TerritoryAssignedTerritory } from '../../models/dashboard.model';

export type UserSortValue =
  | 'status_active_first'
  | 'name_asc'
  | 'name_desc'
  | 'last_login_desc'
  | 'last_login_asc'
  | 'role'
  | 'regions_count_desc';

export interface TerritoryUserAssignedRegion {
  regionId: number;
  regionName: string;
  displayName: string;
}

export interface TerritoryUser {
  userId: number;
  displayName: string;
  firstName: string;
  lastName: string;
  email: string;
  avatarUrl?: string | null;
  initials: string;
  roleCode: string;
  roleDisplayName: string;
  assignedRegions: TerritoryUserAssignedRegion[];
  status: 'Active' | 'Inactive' | string;
  isActive: boolean;
  lastLoginAt?: string | null;
  lastLoginDisplay: string;
  canEdit: boolean;
  canAddRegion: boolean;
  canRemoveManagerRole: boolean;
  createdAt: string;
}

export interface UsersPage {
  territory?: TerritoryAssignedTerritory | null;
  items: TerritoryUser[];
  total: number;
  totalCount: number;
  skip: number;
  take: number;
  hasMore: boolean;
}

interface TerritoryUserListResponse {
  territory?: TerritoryAssignedTerritory | null;
  items: TerritoryUser[];
  totalCount: number;
  skip: number;
  take: number;
  hasMore: boolean;
}

export interface UsersPageRequest {
  territoryId?: number | null;
  search?: string | null;
  sort?: UserSortValue | string | null;
  skip?: number;
  take?: number;
}

export interface TerritoryUserUpdateRequest {
  territoryId: number;
  firstName?: string | null;
  lastName?: string | null;
  displayName?: string | null;
}

export interface TerritoryUserAvailableRegion {
  regionId: number;
  regionName: string;
  displayName: string;
}

interface AvailableRegionsResponse {
  items: TerritoryUserAvailableRegion[];
}

@Injectable({ providedIn: 'root' })
export class UsersService {
  readonly lastPage$ = new BehaviorSubject<UsersPage | null>(null);

  constructor(private readonly http: HttpClient) {}

  getPage$(request: UsersPageRequest): Observable<UsersPage> {
    let params = this.applyDirectoryParams(new HttpParams(), request);
    if (request.territoryId && request.territoryId > 0) {
      params = params.set('territoryId', String(request.territoryId));
    }

    return this.http
      .get<TerritoryUserListResponse>('/api/admin/territory/users', { params })
      .pipe(
        map(response => ({
          ...response,
          total: response.totalCount,
        })),
        tap(page => this.lastPage$.next(page)),
      );
  }

  updateUser$(userId: number, request: TerritoryUserUpdateRequest): Observable<TerritoryUser> {
    return this.http.put<TerritoryUser>(`/api/admin/territory/users/${userId}`, request);
  }

  getAvailableRegions$(
    userId: number,
    territoryId: number,
    search?: string | null,
    take = 50,
  ): Observable<TerritoryUserAvailableRegion[]> {
    let params = new HttpParams()
      .set('territoryId', String(territoryId))
      .set('take', String(Math.max(1, take)));

    if (search?.trim()) {
      params = params.set('search', search.trim());
    }

    return this.http
      .get<AvailableRegionsResponse>(`/api/admin/territory/users/${userId}/available-regions`, { params })
      .pipe(map(response => response.items ?? []));
  }

  addRegion$(userId: number, territoryId: number, regionId: number): Observable<TerritoryUser> {
    return this.http.post<TerritoryUser>(`/api/admin/territory/users/${userId}/regions`, {
      territoryId,
      regionId,
    });
  }

  removeManagerRole$(userId: number, territoryId: number): Observable<TerritoryUser> {
    return this.http.post<TerritoryUser>(`/api/admin/territory/users/${userId}/remove-manager-role`, {
      territoryId,
    });
  }

  formatLastLogin(lastLoginAt?: string | null, lastLoginDisplay?: string | null): string {
    if (lastLoginDisplay?.trim()) {
      return lastLoginDisplay.trim();
    }

    if (!lastLoginAt) {
      return 'Never';
    }

    const date = new Date(lastLoginAt);
    if (Number.isNaN(date.getTime())) {
      return 'Never';
    }

    const diffMs = Date.now() - date.getTime();
    if (diffMs < 60_000) return 'Just now';

    const diffHours = Math.floor(diffMs / 3_600_000);
    if (diffHours < 1) {
      const minutes = Math.max(1, Math.floor(diffMs / 60_000));
      return `${minutes} min ago`;
    }

    if (diffHours < 24) {
      return diffHours === 1 ? '1 hour ago' : `${diffHours} hours ago`;
    }

    const diffDays = Math.floor(diffHours / 24);
    if (diffDays < 30) {
      return diffDays === 1 ? '1 day ago' : `${diffDays} days ago`;
    }

    const diffMonths = Math.max(1, Math.floor(diffDays / 30));
    return diffMonths === 1 ? '1 month ago' : `${diffMonths} months ago`;
  }

  private applyDirectoryParams(params: HttpParams, request: UsersPageRequest): HttpParams {
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
