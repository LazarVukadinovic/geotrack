export interface Activity {
  avatar: string;
  initials: string;
  avatarColor: string;
  description: string;
  type: 'place' | 'destination' | 'event' | 'request';
  timeAgo: string;
  status: 'PENDING' | 'PUBLISHED' | 'APPROVED' | null;
}

export interface QueueItem {
  title: string;
  type: 'Place' | 'Destination' | 'Event';
  author: string;
  timeAgo: string;
  imageBg: string;
  typeColor: string;
}
export interface DashboardStat {
  label: string;
  value: string;
  sub: string;
  icon: string;
  highlight: boolean;
}

export interface QuickAction {
  label: string;
  desc: string;
  icon: string;
  route: string;
}
