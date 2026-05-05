import { Injectable } from '@angular/core';
import { Activity, DashboardStat, QuickAction } from '../models/dashboard.model';

@Injectable({ providedIn: 'root' })
export class DashboardService {

  // --- MOCK: zameniti sa HTTP pozivima pri integraciji ---

  getActivities(): Activity[] {
    return [
      { avatar: '', initials: 'MC', avatarColor: '#c7d2fe', description: 'Marcus Chen submitted a new place request for Columbia River Gorge Viewpoint', type: 'place',       timeAgo: '10 mins ago', status: 'PENDING'   },
    { avatar: '', initials: 'ER', avatarColor: '#fde68a', description: 'Elena Rodriguez published an updated destination guide for Seattle Metro Area',  type: 'destination', timeAgo: '2 hours ago', status: 'PUBLISHED'  },
    { avatar: '', initials: 'DK', avatarColor: '#a7f3d0', description: 'David Kim approved the event listing for Portland Rose Festival 2024',           type: 'event',       timeAgo: '4 hours ago', status: 'APPROVED'   },
    { avatar: '', initials: 'SJ', avatarColor: '#fbcfe8', description: 'Sarah Jenkins added a new regional alert to Mt. Hood National Forest',           type: 'place',       timeAgo: 'Yesterday',   status: null        },
    { avatar: '', initials: 'SA', avatarColor: '#e5e7eb', description: 'System Admin completed weekly content sync for Pacific Northwest Region',        type: 'request',     timeAgo: 'Yesterday',   status: null        },
    ];
  }

  getStats(pendingCount: number): DashboardStat[] {
    return [
      { label: 'Pending Requests',   value: String(pendingCount), sub: 'Requires attention', icon: 'inbox', highlight: true },
      { label: 'Total Destinations', value: '148',   sub: '12 this month', icon: 'map',      highlight: false },
      { label: 'Total Places',       value: '3,240', sub: '84 this month', icon: 'pin',      highlight: false },
      { label: 'Total Events',       value: '56',    sub: '12 this month', icon: 'calendar', highlight: false },
    ];
  }

  getQuickActions(): QuickAction[] {
    return [
      { label: 'Create New Destination', desc: 'Define a new geographical area...', icon: 'flag',     route: '/destinations/new' },
      { label: 'Add New Location',       desc: 'Add a specific POI...',             icon: 'pin',      route: '/locations/new'    },
      { label: 'Add New Place',          desc: 'Add a specific POI...',             icon: 'home',     route: '/places/new'       },
      { label: 'Schedule Event',         desc: 'Publish a time-bound activity...',  icon: 'calendar', route: '/events/new'       },
    ];
  }
}