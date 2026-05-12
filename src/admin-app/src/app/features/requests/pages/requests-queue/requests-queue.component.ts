import { Component, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { ReviewRequestsService, ReviewRequest } from '../../../../core/services/review-requests.service';

export type RequestType = 'Place' /*| 'Location' | 'Event'*/;
export type RequestStatus = 'Pending' | 'Approved' | 'Rejected';

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
  constructor(private router: Router, private requestsService: ReviewRequestsService) {}

  region   = 'Šumadija, Srbija';
  userName = '';
  userRole = '';

  get requests() { return this.requestsService.requests; }

  get tabs(): Tab[] {
    return [
      { key: 'all',      label: 'All Requests', count: this.requestsService.pendingRequests.length },
      { key: 'Location', label: 'Locations',    count: null },
      { key: 'Place',    label: 'Places',       count: null },
      { key: 'Event',    label: 'Events',       count: null },
      { key: 'Approved', label: 'Approved',     count: this.requestsService.approvedRequests.length || null },
      { key: 'Rejected', label: 'Rejected',     count: this.requestsService.rejectedRequests.length || null },
    ];
  }

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
    let list: ReviewRequest[];

    if (this.activeTab === 'Approved') {
      list = [...this.requestsService.approvedRequests];
    } else if (this.activeTab === 'Rejected') {
      list = [...this.requestsService.rejectedRequests];
    } else {
      list = [...this.requestsService.pendingRequests];
      if (this.activeTab !== 'all') {
        list = list.filter(r => r.type === this.activeTab);
      }
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
    this.requestsService.pendingRequests
      .filter(r => this.selectedIds.has(r.id))
      .forEach(r => r.status = 'Approved');
    this.selectedIds.clear();
  }

  rejectSelected(): void {
    this.requestsService.pendingRequests
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
  get totalForCurrentView(): number {
    if (this.activeTab === 'Approved') return this.requestsService.approvedRequests.length;
    if (this.activeTab === 'Rejected') return this.requestsService.rejectedRequests.length;
    return this.requestsService.pendingRequests.length;
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
