import { Component, inject } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { AuthService } from '../../../../core/services/auth.service';
import { NationalDashboardService, NationalStat, RegionCard, TopRegion, TopDestination, TopLocation, TopPlace, HotEvent } from '../../../../core/services/national/national-dashboard.service';

@Component({
  selector: 'app-national-dashboard',
  standalone: true,
  imports: [CommonModule, DatePipe, SidebarComponent, TopbarComponent],
  templateUrl: './national-dashboard.component.html',
  styleUrl: './national-dashboard.component.css',
})
export class NationalDashboardComponent {
  private readonly auth      = inject(AuthService);
  private readonly dashboard = inject(NationalDashboardService);

  get userName(): string { return this.auth.fullName; }
  get userRole():  string { return this.auth.getUser()?.role ?? ''; }
  get region():    string { return this.auth.getUser()?.region ?? ''; }

  readonly stats:           NationalStat[]   = this.dashboard.getStats();
  readonly regions:         RegionCard[]     = this.dashboard.getRegions();
  readonly topRegions:      TopRegion[]      = this.dashboard.getTopRegions();
  readonly topDestinations: TopDestination[] = this.dashboard.getTopDestinations();
  readonly topLocations:    TopLocation[]    = this.dashboard.getTopLocations();
  readonly topPlaces:       TopPlace[]       = this.dashboard.getTopPlaces();
  readonly hotEvents:       HotEvent[]       = this.dashboard.getHotEvents();
}