import { Injectable } from '@angular/core';

export type RequestType   = 'Place';
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

@Injectable({ providedIn: 'root' })
export class ReviewRequestsService {
  requests: ReviewRequest[] = [
    {
      id: '1', title: 'Vila Sumadija Boutique Stay', type: 'Place',
      region: 'Šumadija', reqId: 'REQ-8992', author: 'Marija Petrović',
      authorInitials: 'MP', authorBg: '#fbcfe8', date: 'Oct 4, 2023',
      status: 'Pending', imageBg: 'linear-gradient(135deg, #0ea5e9 0%, #0369a1 100%)',
    },
    {
      id: '2', title: 'Ethno House Konak Rudnik', type: 'Place',
      region: 'Šumadija', reqId: 'REQ-8993', author: 'Jovan Nikolić',
      authorInitials: 'JN', authorBg: '#bfdbfe', date: 'Oct 10, 2023',
      status: 'Pending', imageBg: 'linear-gradient(135deg, #f97316 0%, #ea580c 100%)',
    },
    {
      id: '3', title: 'Planinska Kuća Divčibare', type: 'Place',
      region: 'Šumadija', reqId: 'REQ-8994', author: 'Ana Đorđević',
      authorInitials: 'AĐ', authorBg: '#a7f3d0', date: 'Oct 15, 2023',
      status: 'Pending', imageBg: 'linear-gradient(135deg, #10b981 0%, #059669 100%)',
    },
    {
      id: '4', title: 'Apartmani Šumadijska Noć', type: 'Place',
      region: 'Šumadija', reqId: 'REQ-8995', author: 'Nikola Stojanović',
      authorInitials: 'NS', authorBg: '#fde68a', date: 'Oct 18, 2023',
      status: 'Pending', imageBg: 'linear-gradient(135deg, #8b5cf6 0%, #6d28d9 100%)',
    },
    {
      id: '5', title: 'Vinska Kuca Oplenac Wine Stay', type: 'Place',
      region: 'Šumadija', reqId: 'REQ-8996', author: 'Milica Vasić',
      authorInitials: 'MV', authorBg: '#fecdd3', date: 'Oct 20, 2023',
      status: 'Pending', imageBg: 'linear-gradient(135deg, #e11d48 0%, #be123c 100%)',
    },
    {
      id: '6', title: 'Glamping Šumadija Forest Camp', type: 'Place',
      region: 'Šumadija', reqId: 'REQ-8997', author: 'Stefan Ristić',
      authorInitials: 'SR', authorBg: '#d1fae5', date: 'Oct 21, 2023',
      status: 'Pending', imageBg: 'linear-gradient(135deg, #06b6d4 0%, #0891b2 100%)',
    },
    {
      id: '7', title: 'Wine & Harvest Festival Stay', type: 'Place',
      region: 'Šumadija', reqId: 'REQ-8998', author: 'Dragana Milošević',
      authorInitials: 'DM', authorBg: '#ede9fe', date: 'Oct 30, 2023',
      status: 'Pending', imageBg: 'linear-gradient(135deg, #f59e0b 0%, #d97706 100%)',
    },
  ];

  get pendingRequests(): ReviewRequest[] {
    return this.requests.filter(r => r.status === 'Pending');
  }
  get approvedRequests(): ReviewRequest[] {
    return this.requests.filter(r => r.status === 'Approved');
  }

  get rejectedRequests(): ReviewRequest[] {
    return this.requests.filter(r => r.status === 'Rejected');
  }
  get pendingCount(): number {
    return this.pendingRequests.length;
  }
  removeRequest(id: string): void {
    this.requests = this.requests.filter(r => r.id !== id);
  }
}