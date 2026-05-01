import { Component, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';

export type RequestType = 'Destination' | 'Place' | 'Location' | 'Event';
export type RequestStatus = 'Pending' | 'Approved' | 'Rejected';

export interface ReviewRequest {
  id: string;
  title: string;
  type: RequestType;
  region: string;
  reqId: string;
  author: string;
  authorInitials: string;
  authorImg?: string;
  authorBg: string;
  date: string;
  status: RequestStatus;
  imageBg: string;
}

interface Tab {
  key: string;
  label: string;
  count: number | null;
}

@Component({
  selector: 'app-review-queue',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './requests-queue.component.html',
})
export class ReviewQueueComponent {
  constructor(private router: Router) {}

  region   = 'Šumadija, Srbija';
  userName = 'Sarah Jenkins';
  userRole = 'Regional Manager';

  requests: ReviewRequest[] = [
    {
      id: '1',
      title: 'Olympic National Park Trails Update',
      type: 'Destination',
      region: 'Pacific Northwest',
      reqId: 'REQ-001',
      author: 'David Chen',
      authorInitials: 'DC',
      authorBg: '#bfdbfe',
      date: 'Oct 24, 2023',
      status: 'Pending',
      imageBg: 'linear-gradient(135deg, #0ea5e9 0%, #0369a1 100%)',
    },
    {
      id: '2',
      title: 'Pike Place Market Artisan Directory',
      type: 'Place',
      region: 'Pacific Northwest',
      reqId: 'REQ-002',
      author: 'Elena Rodriguez',
      authorInitials: 'ER',
      authorBg: '#fde68a',
      date: 'Oct 23, 2023',
      status: 'Pending',
      imageBg: 'linear-gradient(135deg, #f97316 0%, #ea580c 100%)',
    },
    {
      id: '3',
      title: 'Seattle Summer Solstice Festival',
      type: 'Event',
      region: 'Pacific Northwest',
      reqId: 'REQ-003',
      author: 'Marcus Johnson',
      authorInitials: 'MJ',
      authorBg: '#a7f3d0',
      date: 'Oct 22, 2023',
      status: 'Pending',
      imageBg: 'linear-gradient(135deg, #8b5cf6 0%, #6d28d9 100%)',
    },
    {
      id: '4',
      title: 'Mount Rainier Basecamp Facilities',
      type: 'Place',
      region: 'Pacific Northwest',
      reqId: 'REQ-004',
      author: 'Sarah Jenkins',
      authorInitials: 'SJ',
      authorBg: '#fbcfe8',
      date: 'Oct 21, 2023',
      status: 'Pending',
      imageBg: 'linear-gradient(135deg, #10b981 0%, #059669 100%)',
    },
    {
      id: '5',
      title: 'San Juan Islands Ferry Routes',
      type: 'Destination',
      region: 'Pacific Northwest',
      reqId: 'REQ-005',
      author: 'David Chen',
      authorInitials: 'DC',
      authorBg: '#bfdbfe',
      date: 'Oct 20, 2023',
      status: 'Pending',
      imageBg: 'linear-gradient(135deg, #06b6d4 0%, #0891b2 100%)',
    },
  ];

  tabs: Tab[] = [
    { key: 'all',         label: 'All Requests',  count: 5 },
    { key: 'Destination', label: 'Destinations',  count: null },
    { key: 'Location',    label: 'Locations',     count: null },
    { key: 'Place',       label: 'Places',        count: null },
    { key: 'Event',       label: 'Events',        count: null },
  ];

  activeTab    = 'all';
  searchQuery  = '';
  sortOpen     = false;
  currentSort  = 'Newest First';
  sortOptions  = ['Newest First', 'Oldest First', 'A–Z', 'Z–A'];

  selectedIds = new Set<string>();

  get allSelected(): boolean {
    return this.filteredRequests.length > 0 &&
           this.filteredRequests.every(r => this.selectedIds.has(r.id));
  }

  get filteredRequests(): ReviewRequest[] {
    let list = [...this.requests];

    if (this.activeTab !== 'all') {
      list = list.filter(r => r.type === this.activeTab);
    }

    if (this.searchQuery.trim()) {
      const q = this.searchQuery.toLowerCase();
      list = list.filter(r =>
        r.title.toLowerCase().includes(q) ||
        r.author.toLowerCase().includes(q) ||
        r.reqId.toLowerCase().includes(q)
      );
    }

    switch (this.currentSort) {
      case 'Newest First': list.sort((a, b) => b.reqId.localeCompare(a.reqId)); break;
      case 'Oldest First': list.sort((a, b) => a.reqId.localeCompare(b.reqId)); break;
      case 'A–Z':          list.sort((a, b) => a.title.localeCompare(b.title));  break;
      case 'Z–A':          list.sort((a, b) => b.title.localeCompare(a.title));  break;
    }

    return list;
  }

  toggleSelectAll(event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    if (checked) {
      this.filteredRequests.forEach(r => this.selectedIds.add(r.id));
    } else {
      this.selectedIds.clear();
    }
  }

  toggleSelect(id: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    if (checked) this.selectedIds.add(id);
    else         this.selectedIds.delete(id);
  }

  approveSelected(): void {
    this.requests
      .filter(r => this.selectedIds.has(r.id))
      .forEach(r => r.status = 'Approved');
    this.selectedIds.clear();
  }

  rejectSelected(): void {
    this.requests
      .filter(r => this.selectedIds.has(r.id))
      .forEach(r => r.status = 'Rejected');
    this.selectedIds.clear();
  }

  approveRequest(req: ReviewRequest): void {
    req.status = 'Approved';
  }

  rejectRequest(req: ReviewRequest): void {
    req.status = 'Rejected';
  }

  previewRequest(req: ReviewRequest): void {
    this.router.navigate(['/request/detail', req.id]);
  }

  // Context menu
  contextMenu: { req: ReviewRequest; x: number; y: number } | null = null;

  openContextMenu(req: ReviewRequest, event: MouseEvent): void {
    event.stopPropagation();
    this.contextMenu = { req, x: event.clientX - 136, y: event.clientY + 8 };
  }

  @HostListener('document:click')
  onDocumentClick(): void {
    this.contextMenu = null;
    this.sortOpen    = false;
  }

  typeBadgeClass(type: RequestType): string {
    const map: Record<RequestType, string> = {
      Destination: 'bg-green-50 text-green-700 border border-green-200',
      Place:       'bg-blue-50 text-blue-700 border border-blue-200',
      Location:    'bg-indigo-50 text-indigo-700 border border-indigo-200',
      Event:       'bg-yellow-50 text-yellow-700 border border-yellow-200',
    };
    return map[type] ?? 'bg-gray-100 text-gray-600';
  }
}