import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { AuthService } from '../../../../core/services/auth.service';

interface RegionCard {
  name: string;
  manager: string;
  destinations: number;
  places: number;
  events: number;
  pending: number;
  activityScore: number;
  initials: string[];
}

interface TopRegion {
  rank: number;
  name: string;
  visits: string;
  change: string;
}

interface TopDestination { name: string; region: string; views: string; }
interface TopPlace       { name: string; region: string; rating: number; }
interface HotEvent       { name: string; date: string; attendees: string; }

@Component({
  selector: 'app-national-dashboard',
  standalone: true,
  imports: [CommonModule, SidebarComponent, TopbarComponent],
  templateUrl: './national-dashboard.component.html',
  styleUrl: './national-dashboard.component.css',
})
export class NationalDashboardComponent {
  private readonly auth = inject(AuthService);

  get userName(): string { return this.auth.fullName; }
  get userRole():  string { return this.auth.getUser()?.role ?? ''; }
  get region():    string { return this.auth.getUser()?.region ?? ''; }

  // ── Stats ──────────────────────────────────────────────────────────────
  readonly stats = [
    { label: 'Total Regions',       value: '24',   change: '+2 vs last month',   up: true,  icon: 'globe'    },
    { label: 'Active Users',         value: '842',  change: '+12% vs last month', up: true,  icon: 'users'    },
    { label: 'Total Destinations',   value: '348',  change: '+18 vs last month',  up: true,  icon: 'compass'  },
    { label: 'Total Locations',      value: '4,215',change: '+124 vs last month', up: true,  icon: 'pin'      },
    { label: 'Total Places',         value: '4,215',change: '+124 vs last month', up: true,  icon: 'home'     },
    { label: 'Total Events',         value: '156',  change: '-12 vs last month',  up: false, icon: 'calendar' },
  ];

  // ── Region cards ───────────────────────────────────────────────────────
  readonly regions: RegionCard[] = [
    { name: 'Šumadija',   manager: 'Sarah Jenkins',  destinations: 42, places: 315, events: 18, pending: 2,  activityScore: 92, initials: ['SJ','MK','AL'] },
    { name: 'Banat',      manager: 'Marcus Thorne',  destinations: 28, places: 190, events: 5,  pending: 15, activityScore: 76, initials: ['MT','PK','JL'] },
    { name: 'Bačka',      manager: 'Emily Chen',     destinations: 65, places: 480, events: 32, pending: 4,  activityScore: 88, initials: ['EC','TR','MN'] },
    { name: 'Mačva',      manager: 'David Albers',   destinations: 35, places: 210, events: 8,  pending: 42, activityScore: 61, initials: ['DA','LP','KS'] },
    { name: 'Pomoravlje', manager: 'John Doe',       destinations: 19, places: 140, events: 12, pending: 1,  activityScore: 85, initials: ['JD','RV','TM'] },
    { name: 'Raška',      manager: 'Maria Garcia',   destinations: 40, places: 275, events: 21, pending: 18, activityScore: 79, initials: ['MG','SP','NK'] },
  ];

  // ── Top performing ─────────────────────────────────────────────────────
  readonly topRegions: TopRegion[] = [
    { rank: 1, name: 'Šumadija',   visits: '1.2M visits this month', change: '+15%' },
    { rank: 2, name: 'Pomoravlje', visits: '980K visits this month',  change: '+8%'  },
    { rank: 3, name: 'Bačka',      visits: '850K visits this month',  change: '+12%' },
  ];

  // ── Top content ────────────────────────────────────────────────────────
  readonly topDestinations: TopDestination[] = [
    { name: 'Yellowstone National Park', region: 'Rocky Mountains',  views: '450K views' },
    { name: 'Grand Canyon',              region: 'Southwest Arid',   views: '380K views' },
    { name: 'Glacier National Park',     region: 'Pacific Northwest', views: '290K views' },
  ];

  readonly topPlaces: TopPlace[] = [
    { name: 'Old Faithful Visitor Center', region: 'Rocky Mountains',    rating: 4.9 },
    { name: 'Yosemite Valley Lodge',        region: 'Coastal California', rating: 4.8 },
    { name: 'Zion Narrows Trailhead',       region: 'Southwest Arid',     rating: 4.9 },
  ];

  readonly hotEvents: HotEvent[] = [
    { name: 'Annual Ranger Symposium', date: 'Oct 15, 2024', attendees: '2.5K att.' },
    { name: 'Spring Bloom Festival',   date: 'Mar 22, 2024', attendees: '1.8K att.' },
    { name: 'Conservation Summit',     date: 'Nov 05, 2024', attendees: '1.2K att.' },
  ];
}