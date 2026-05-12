import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import {
  DashboardStat,
  QuickAction,
  RegionalAssignedRegion,
  RegionalDashboard,
  RegionalDashboardActivityResponse,
  RegionalDashboardStats,
} from '../models/dashboard.model';

@Injectable({ providedIn: 'root' })
export class DashboardService {
  constructor(private readonly http: HttpClient) {}

  getAssignedRegions() {
    return this.http.get<RegionalAssignedRegion[]>('/api/admin/regional/regions');
  }

  getDashboard(regionId: number) {
    const params = new HttpParams().set('regionId', regionId);
    return this.http.get<RegionalDashboard>('/api/admin/regional/dashboard', { params });
  }

  getActivity(regionId: number, skip = 0, take = 20) {
    const params = new HttpParams()
      .set('regionId', regionId)
      .set('skip', skip)
      .set('take', take);

    return this.http.get<RegionalDashboardActivityResponse>('/api/admin/regional/activity', { params });
  }

  getQuickActions(): QuickAction[] {
    return [
      { label: 'Create New Destination', desc: 'Define a new geographical area...', icon: 'flag', route: '/destinations/new' },
      { label: 'Add New Location', desc: 'Add a specific POI...', icon: 'pin', route: '/locations/new' },
      { label: 'Add New Place', desc: 'Add a specific POI...', icon: 'home', route: '/places/new' },
      { label: 'Schedule Event', desc: 'Publish a time-bound activity...', icon: 'calendar', route: '/events/new' },
    ];
  }

  buildStats(stats: RegionalDashboardStats): DashboardStat[] {
    return [
      {
        label: 'Pending Requests',
        value: String(stats.pendingRequests),
        sub: 'Requires attention',
        icon: 'inbox',
        highlight: true,
        route: '/requests',
      },
      {
        label: 'Total Destinations',
        value: stats.totalDestinations.toLocaleString(),
        sub: `${stats.destinationsAddedLastMonth.toLocaleString()} in last 30 days`,
        icon: 'map',
        highlight: false,
        route: '/destinations',
      },
      {
        label: 'Total Places',
        value: stats.totalPlaces.toLocaleString(),
        sub: `${stats.placesAddedLastMonth.toLocaleString()} in last 30 days`,
        icon: 'pin',
        highlight: false,
        route: '/places',
      },
      {
        label: 'Total Events',
        value: stats.totalEvents.toLocaleString(),
        sub: `${stats.eventsAddedLastMonth.toLocaleString()} in last 30 days`,
        icon: 'calendar',
        highlight: false,
        route: '/events',
      },
    ];
  }
}
