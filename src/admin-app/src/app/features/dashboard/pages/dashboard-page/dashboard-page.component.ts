import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
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
  imports: [CommonModule, RouterLink, SidebarComponent, TopbarComponent],
  templateUrl: './dashboard-page.component.html',
  styleUrl: './dashboard-page.component.css'
})
export class DashboardComponent {
  sidebarOpen = true;
  region = 'Šumadija, Srbija';
  userName = 'Sarah Jenkins';
  userRole = 'Regional Manager';

  stats = [
    { label: 'Pending Requests', value: '5',    sub: 'Requires attention',   icon: 'inbox',   highlight: true },
    { label: 'Total Destinations', value: '148',  sub: '12 this month',        icon: 'map',     highlight: false },
    { label: 'Total Places',       value: '3,240', sub: '84 this month',       icon: 'pin',     highlight: false },
    { label: 'Total Events',       value: '56',   sub: '12 this month',        icon: 'calendar',highlight: false },
  ];

  quickActions = [
    { label: 'Create New Destination', desc: 'Define a new geographical area, route, or broader travel context.', icon: 'flag' },
    { label: 'Add New Location',       desc: 'Add a specific POI, business, landmark, or location to the map.',   icon: 'pin' },
    { label: 'Add New Place',          desc: 'Add a specific POI, business, landmark, or location to the map.',   icon: 'home' },
    { label: 'Schedule Event',         desc: 'Publish a time-bound activity or gathering in your region.',         icon: 'calendar' },
  ];

  activities: Activity[] = [
    { avatar: '', initials: 'MC', avatarColor: '#c7d2fe', description: 'Marcus Chen submitted a new place request for Columbia River Gorge Viewpoint', type: 'place',       timeAgo: '10 mins ago', status: 'PENDING'   },
    { avatar: '', initials: 'ER', avatarColor: '#fde68a', description: 'Elena Rodriguez published an updated destination guide for Seattle Metro Area',  type: 'destination', timeAgo: '2 hours ago', status: 'PUBLISHED'  },
    { avatar: '', initials: 'DK', avatarColor: '#a7f3d0', description: 'David Kim approved the event listing for Portland Rose Festival 2024',           type: 'event',       timeAgo: '4 hours ago', status: 'APPROVED'   },
    { avatar: '', initials: 'SJ', avatarColor: '#fbcfe8', description: 'Sarah Jenkins added a new regional alert to Mt. Hood National Forest',           type: 'place',       timeAgo: 'Yesterday',   status: null        },
    { avatar: '', initials: 'SA', avatarColor: '#e5e7eb', description: 'System Admin completed weekly content sync for Pacific Northwest Region',        type: 'request',     timeAgo: 'Yesterday',   status: null        },
  ];

  queue: QueueItem[] = [
    { title: 'Pike Place Market',  type: 'Place',       author: 'Marcus Chen',     timeAgo: '15 mins ago', imageBg: '#bfdbfe', typeColor: 'bg-blue-100 text-blue-700'  },
    { title: 'Olympic Peninsula',  type: 'Destination', author: 'Local Guides Co.', timeAgo: '3 hours ago', imageBg: '#d1fae5', typeColor: 'bg-green-100 text-green-700'},
    { title: 'Summer Solstice P…', type: 'Event',       author: 'Elena Rodriguez',  timeAgo: '5 hours ago', imageBg: '#fde68a', typeColor: 'bg-yellow-100 text-yellow-700'},
    { title: 'Crater Lake Lodge',  type: 'Place',       author: 'David Kim',        timeAgo: '1 day ago',   imageBg: '#e0e7ff', typeColor: 'bg-blue-100 text-blue-700'  },
  ];

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
}