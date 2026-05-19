import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable, map, of, throwError } from 'rxjs';
import { TerritoryDashboardService } from '../territory-dashboard.service';
import { TerritoryRegionDirectoryItem } from '../../models/dashboard.model';

export interface RegionListItem {
  id: string;
  name: string;
  description: string;
  isTop: boolean;
  destinations: number;
  places: number;
  locations: number;
  events: number;
  assignedPersons: number;
  status: 'Active' | 'Inactive';
  imageBg: string;
  managerDisplayName?: string | null;
  pendingRequests: number;
}

export type RegionSortField = 'name' | 'status' | 'assignedPersons' | 'destinations';
export type RegionSortDir = 'asc' | 'desc';

export interface RegionPage {
  items: RegionListItem[];
  total: number;
}

export interface RegionPageRequest {
  page: number;
  pageSize: number;
  search: string;
  sortField: RegionSortField;
  sortDir: RegionSortDir;
}

export interface RegionTeamMember {
  id: string;
  name: string;
  email: string;
  initials: string;
  avatarColor: string;
  role: string;
  status: 'Active' | 'Inactive';
  assignedDate: string;
}

export interface RegionDetail extends RegionListItem {
  fullName: string;
  internalRefId: string;
  shortSummary: string;
  fullDescription: string;
  team: RegionTeamMember[];
  createdBy: string;
  createdDate: string;
  lastUpdated: string;
  activityScore: number;
  activityLabel: string;
  processingLabel: string;
  avgProcessingTime: number;
}

export function buildDetail(item: RegionListItem): RegionDetail {
  return {
    ...item,
    fullName: item.name,
    internalRefId: item.id,
    shortSummary: item.description,
    fullDescription: item.description,
    team: [],
    createdBy: 'System',
    createdDate: '',
    lastUpdated: '',
    activityScore: 0,
    activityLabel: 'Read-only',
    processingLabel: 'Read-only',
    avgProcessingTime: 0,
  };
}

@Injectable({ providedIn: 'root' })
export class RegionsService {
  readonly lastPage$ = new BehaviorSubject<RegionPage | null>(null);

  private _listState: {
    searchQuery: string;
    sortField: string;
    sortDir: string;
    displayCount: number;
    scrollTop: number;
  } | null = null;

  constructor(private readonly territoryDashboard: TerritoryDashboardService) {}

  setActiveTerritoryId(territoryId: number): void {
    this.territoryDashboard.setActiveTerritoryId(territoryId);
  }

  getPage$(req: RegionPageRequest): Observable<RegionPage> {
    return this.territoryDashboard.getRegions({
      territoryId: this.territoryDashboard.getActiveTerritoryId(),
      search: req.search,
      sort: this.mapSort(req.sortField, req.sortDir),
      skip: 0,
      take: req.pageSize,
    }).pipe(
      map(response => ({
        items: response.items.map(item => this.mapRegion(item)),
        total: response.totalCount,
      })),
      map(page => {
        this.lastPage$.next(page);
        return page;
      }),
    );
  }

  delete$(_id?: string): Observable<void> {
    return throwError(() => new Error('Territory Manager can view regions but cannot delete them.'));
  }

  getDetail$(id: string): Observable<RegionDetail | null> {
    const item = this.lastPage$.value?.items.find(region => region.id === id) ?? null;
    return of(item ? buildDetail(item) : null);
  }

  updateDetail$(_id?: string, _patch?: Partial<RegionDetail>): Observable<RegionDetail | null> {
    return throwError(() => new Error('Territory Manager region pages are read-only.'));
  }

  updateTeam$(_id?: string, _team?: RegionTeamMember[]): Observable<void> {
    return throwError(() => new Error('Territory Manager region pages are read-only.'));
  }

  saveListState(s: {
    searchQuery: string;
    sortField: string;
    sortDir: string;
    displayCount: number;
    scrollTop: number;
  }): void {
    this._listState = s;
  }

  getListState() {
    return this._listState;
  }

  clearListState(): void {
    this._listState = null;
  }

  private mapRegion(item: TerritoryRegionDirectoryItem): RegionListItem {
    const status: 'Active' | 'Inactive' = item.activeManagersCount > 0 ? 'Active' : 'Inactive';
    const description = item.managerDisplayName
      ? `Regional Manager: ${item.managerDisplayName}`
      : 'No active Regional Manager assigned';

    return {
      id: String(item.id),
      name: item.name,
      description,
      isTop: false,
      destinations: item.destinationsCount,
      locations: item.locationsCount,
      places: item.placesCount,
      events: item.eventsCount,
      assignedPersons: item.activeManagersCount,
      status,
      imageBg: this.imageBackground(item.id),
      managerDisplayName: item.managerDisplayName,
      pendingRequests: item.pendingRequestsCount,
    };
  }

  private mapSort(field: RegionSortField, dir: RegionSortDir): string {
    if (field === 'assignedPersons') {
      return dir === 'asc' ? 'name' : 'content';
    }

    if (field === 'destinations') {
      return 'content';
    }

    return dir === 'desc' ? 'name-desc' : 'name';
  }

  private imageBackground(id: number): string {
    const variants = [
      'linear-gradient(135deg,#dbeafe 0%,#bfdbfe 100%)',
      'linear-gradient(135deg,#d1fae5 0%,#a7f3d0 100%)',
      'linear-gradient(135deg,#fef9c3 0%,#fde68a 100%)',
      'linear-gradient(135deg,#ede9fe 0%,#ddd6fe 100%)',
    ];
    return variants[Math.abs(id) % variants.length];
  }
}
