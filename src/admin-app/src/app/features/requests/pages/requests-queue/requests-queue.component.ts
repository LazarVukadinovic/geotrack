import { Component, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';

export type RequestType = 'Place' /*| 'Location' | 'Event'*/;
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
      title: 'Vila Sumadija Boutique Stay',
      type: 'Place',
      region: 'Šumadija',
      reqId: 'REQ-8992',
      author: 'Marija Petrović',
      authorInitials: 'MP',
      authorBg: '#fbcfe8',
      date: 'Oct 24, 2023',
      status: 'Pending',
      imageBg: 'linear-gradient(135deg, #0ea5e9 0%, #0369a1 100%)',
    },
    {
      id: '2',
      title: 'Ethno House Konak Rudnik',
      type: 'Place',
      region: 'Šumadija',
      reqId: 'REQ-8993',
      author: 'Jovan Nikolić',
      authorInitials: 'JN',
      authorBg: '#bfdbfe',
      date: 'Oct 23, 2023',
      status: 'Pending',
      imageBg: 'linear-gradient(135deg, #f97316 0%, #ea580c 100%)',
    },
    {
      id: '3',
      title: 'Planinska Kuća Divčibare',
      type: 'Place',
      region: 'Šumadija',
      reqId: 'REQ-8994',
      author: 'Ana Đorđević',
      authorInitials: 'AĐ',
      authorBg: '#a7f3d0',
      date: 'Oct 22, 2023',
      status: 'Pending',
      imageBg: 'linear-gradient(135deg, #10b981 0%, #059669 100%)',
    },
    {
      id: '4',
      title: 'Apartmani Šumadijska Noć',
      type: 'Place',
      region: 'Šumadija',
      reqId: 'REQ-8995',
      author: 'Nikola Stojanović',
      authorInitials: 'NS',
      authorBg: '#fde68a',
      date: 'Oct 21, 2023',
      status: 'Pending',
      imageBg: 'linear-gradient(135deg, #8b5cf6 0%, #6d28d9 100%)',
    },
    {
      id: '5',
      title: 'Vinska Kuca Oplenac Wine Stay',
      type: 'Place',
      region: 'Šumadija',
      reqId: 'REQ-8996',
      author: 'Milica Vasić',
      authorInitials: 'MV',
      authorBg: '#fecdd3',
      date: 'Oct 20, 2023',
      status: 'Pending',
      imageBg: 'linear-gradient(135deg, #e11d48 0%, #be123c 100%)',
    },
    {
      id: '6',
      title: 'Glamping Šumadija Forest Camp',
      type: 'Place',
      region: 'Šumadija',
      reqId: 'REQ-8997',
      author: 'Stefan Ristić',
      authorInitials: 'SR',
      authorBg: '#d1fae5',
      date: 'Oct 19, 2023',
      status: 'Pending',
      imageBg: 'linear-gradient(135deg, #06b6d4 0%, #0891b2 100%)',
    },
    {
      id: '7',
      title: 'Wine & Harvest Festival Stay',
      type: 'Place',
      region: 'Šumadija',
      reqId: 'REQ-8998',
      author: 'Dragana Milošević',
      authorInitials: 'DM',
      authorBg: '#ede9fe',
      date: 'Oct 18, 2023',
      status: 'Pending',
      imageBg: 'linear-gradient(135deg, #f59e0b 0%, #d97706 100%)',
    },
  ];

  tabs: Tab[] = [
    { key: 'all',      label: 'All Requests', count: 7 },
    { key: 'Location', label: 'Locations',    count: null },
    { key: 'Place',    label: 'Places',       count: null },
    { key: 'Event',    label: 'Events',       count: null },
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
    this.router.navigate(['/request/detail', req.id], { state: req });
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
      Place:    'bg-blue-50 text-blue-700 border border-blue-200',
      // Location: 'bg-indigo-50 text-indigo-700 border border-indigo-200',
      // Event:    'bg-yellow-50 text-yellow-700 border border-yellow-200',
    };
    return map[type] ?? 'bg-gray-100 text-gray-600';
  }
}