import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, Router } from '@angular/router';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { ReviewRequest, ReviewRequestsService } from '../../../../core/services/review-requests.service'; 
interface Activity {
  avatar: string;
  initials: string;
  avatarColor: string;
  description: string;
  type: 'place' | 'destination' | 'event' | 'request';
  timeAgo: string;
  status: 'PENDING' | 'PUBLISHED' | 'APPROVED' | null;
}

interface QueueItem {
  title: string;
  type: 'Place' | 'Destination' | 'Event';
  author: string;
  timeAgo: string;
  imageBg: string;
  typeColor: string;
}

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, SidebarComponent, TopbarComponent],
  templateUrl: './dashboard-page.component.html',
  styleUrl: './dashboard-page.component.css'
})
export class DashboardComponent {
  constructor(private router: Router, public requestsService: ReviewRequestsService) {}
  sidebarOpen = true;
  region = 'Šumadija, Srbija';
  userName = 'Sarah Jenkins';
  userRole = 'Regional Manager';

  quickActions = [
    { label: 'Create New Destination', desc: 'Define a new geographical area, route, or broader travel context.', icon: 'flag', route: '/destinations/new' },
    { label: 'Add New Location',       desc: 'Add a specific POI, business, landmark, or location to the map.',   icon: 'pin',  route: '/locations/new' },
    { label: 'Add New Place',          desc: 'Add a specific POI, business, landmark, or location to the map.',   icon: 'home',  route: '/places/new' },
    { label: 'Schedule Event',         desc: 'Publish a time-bound activity or gathering in your region.',         icon: 'calendar', route: '/events/new' },
  ];

  activities: Activity[] = [
    { avatar: '', initials: 'MC', avatarColor: '#c7d2fe', description: 'Marcus Chen submitted a new place request for Columbia River Gorge Viewpoint', type: 'place',       timeAgo: '10 mins ago', status: 'PENDING'   },
    { avatar: '', initials: 'ER', avatarColor: '#fde68a', description: 'Elena Rodriguez published an updated destination guide for Seattle Metro Area',  type: 'destination', timeAgo: '2 hours ago', status: 'PUBLISHED'  },
    { avatar: '', initials: 'DK', avatarColor: '#a7f3d0', description: 'David Kim approved the event listing for Portland Rose Festival 2024',           type: 'event',       timeAgo: '4 hours ago', status: 'APPROVED'   },
    { avatar: '', initials: 'SJ', avatarColor: '#fbcfe8', description: 'Sarah Jenkins added a new regional alert to Mt. Hood National Forest',           type: 'place',       timeAgo: 'Yesterday',   status: null        },
    { avatar: '', initials: 'SA', avatarColor: '#e5e7eb', description: 'System Admin completed weekly content sync for Pacific Northwest Region',        type: 'request',     timeAgo: 'Yesterday',   status: null        },
  ];

  get queue() {
    return this.requestsService.pendingRequests.slice(0, 4);
  }

  get stats() {
    return [
      { label: 'Pending Requests',   value: String(this.requestsService.pendingCount),
        sub: 'Requires attention', icon: 'inbox', highlight: true },
      { label: 'Total Destinations', value: '148', sub: '12 this month', icon: 'map',      highlight: false },
      { label: 'Total Places',       value: '3,240', sub: '84 this month', icon: 'pin',    highlight: false },
      { label: 'Total Events',       value: '56',  sub: '12 this month', icon: 'calendar', highlight: false },
    ];
  }
  navItems = [
    { label: 'Dashboard',          icon: 'grid',     route: '/dashboard',            active: true  },
    { label: 'Requests Queue',     icon: 'inbox',    route: '/requests',             badge: 5      },
    { label: 'Map',                icon: 'map',      route: '/map',                  active: false },
  ];

  contentItems = [
    { label: 'Destinations', icon: 'map',      route: '/destinations' },
    { label: 'Locations',    icon: 'location', route: '/locations'    },
    { label: 'Places',       icon: 'home',     route: '/places'       },
    { label: 'Events',       icon: 'calendar', route: '/events'       },
  ];

  statusClass(status: string | null): string {
    if (status === 'PENDING')   return 'status-pending';
    if (status === 'PUBLISHED') return 'status-published';
    if (status === 'APPROVED')  return 'status-approved';
    return '';
  }

  typeIcon(type: string): string {
    if (type === 'place')       return 'pin';
    if (type === 'destination') return 'map';
    if (type === 'event')       return 'calendar';
    return 'inbox';
  }
  navigate(route: string) {
    this.router.navigate([route]);
  }
  navigateToQueue(): void {
    this.router.navigate(['/requests']);
  }
}