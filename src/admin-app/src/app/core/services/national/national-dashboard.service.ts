import { Injectable } from '@angular/core';
import { TerritoryDashboardService } from '../territory-dashboard.service';

@Injectable({ providedIn: 'root' })
export class NationalDashboardService {
  constructor(private readonly territoryDashboard: TerritoryDashboardService) {}

  getAssignedTerritories() {
    return this.territoryDashboard.getAssignedTerritories();
  }

  getDashboard(territoryId?: number | null) {
    return this.territoryDashboard.getDashboard(territoryId);
  }
}
