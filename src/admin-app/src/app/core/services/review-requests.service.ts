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
export interface BookingPartner {
  id: string;
  url: string;
}

export interface ReviewRequestDetail {
  id: string;
  reqId: string;
  internalId?: string;
  title: string;
  stayType: string;
  imageUrl?: string;
  imageBg: string;
  address?: string;
  city?: string;
  region?: string;
  country?: string;
  lat?: number | null;
  lng?: number | null;
  tags?: string[];
  amenities?: string[];
  description?: string;
  phone?: string;
  website?: string;
  bookingPartners?: BookingPartner[];
  type: 'Place' | 'Location' | 'Event';
  author: string;
  authorInitials: string;
  authorBg: string;
  authorEmail?: string;
  date: string;
  status: 'Pending' | 'Approved' | 'Rejected';
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
  requestDetails: ReviewRequestDetail[] = [
    {
      id: '1',
      reqId: 'REQ-8992',
      internalId: 'REG-SC-042',
      title: 'Vila Sumadija Boutique Stay',
      stayType: 'Stay',
      imageBg: 'linear-gradient(135deg, #0ea5e9 0%, #0369a1 100%)',
      address: 'Svetozara Markovića 14',
      city: 'Kragujevac',
      region: 'Sumadija',
      country: 'Serbia',
      lat: 44.013725,
      lng: 20.915097,
      tags: ['Stay', 'Boutique', 'Peaceful', 'Garden', 'Breakfast Included'],
      amenities: ['wifi', 'breakfast', 'parking', 'ac'],
      description: 'A charming boutique villa nestled in the heart of Šumadija, offering guests a peaceful retreat surrounded by lush gardens.',
      phone: '+381 34 123 456',
      website: 'www.vilasumadija.rs',
      bookingPartners: [
        { id: 'booking', url: 'https://www.booking.com/example' },
        { id: 'airbnb',  url: 'https://www.airbnb.com/example'  },
      ],
      type: 'Place',
      author: 'Marija Petrović',
      authorInitials: 'MP',
      authorBg: '#fbcfe8',
      authorEmail: 'm.petrovic@example.com',
      date: 'Oct 24, 2023',
      status: 'Pending',
    },
    {
      id: '2', reqId: 'REQ-8993', title: 'Ethno House Konak Rudnik',
      stayType: 'Stay', imageBg: 'linear-gradient(135deg, #f97316 0%, #ea580c 100%)',
      address: 'Rudnik bb', city: 'Rudnik', region: 'Šumadija', country: 'Serbia',
      lat: 44.1150, lng: 20.5230,
      tags: ['Ethno', 'Rural', 'Traditional'],
      amenities: ['breakfast', 'parking'],
      description: 'Traditional Serbian ethno house at the foot of Rudnik mountain.',
      phone: '+381 32 456 789', website: 'www.konakrudnik.rs',
      bookingPartners: [{ id: 'booking', url: 'https://www.booking.com' }],
      type: 'Place', author: 'Jovan Nikolić', authorInitials: 'JN',
      authorBg: '#bfdbfe', authorEmail: 'j.nikolic@example.com',
      date: 'Oct 10, 2023', status: 'Pending',
    },
    {
      id: '3', reqId: 'REQ-8994', title: 'Planinska Kuća Divčibare',
      stayType: 'Stay', imageBg: 'linear-gradient(135deg, #10b981 0%, #059669 100%)',
      address: 'Divčibare 12', city: 'Divčibare', region: 'Šumadija', country: 'Serbia',
      lat: 44.1340, lng: 20.1120,
      tags: ['Mountain', 'Nature', 'Hiking'],
      amenities: ['wifi', 'parking'],
      description: 'Mountain house on Maljen mountain with stunning views.',
      bookingPartners: [ { id: 'booking', url: 'https://www.booking.com' },],
      type: 'Place', author: 'Ana Đorđević', authorInitials: 'AĐ',
      authorBg: '#a7f3d0', date: 'Oct 15, 2023', status: 'Pending',
    },
    {
      id: '4', reqId: 'REQ-8995', title: 'Apartmani Šumadijska Noć',
      stayType: 'Stay', imageBg: 'linear-gradient(135deg, #8b5cf6 0%, #6d28d9 100%)',
      address: 'Cara Dušana 5', city: 'Kragujevac', region: 'Šumadija', country: 'Serbia',
      lat: 44.0120, lng: 20.9150,
      tags: ['Apartment', 'City', 'Modern'],
      amenities: ['wifi', 'ac'],
      description: 'Modern apartments in the center of Kragujevac.',
      bookingPartners: [ { id: 'booking', url: 'https://www.booking.com' }, { id: 'airbnb', url: 'https://www.airbnb.com' },],
      type: 'Place', author: 'Nikola Stojanović', authorInitials: 'NS',
      authorBg: '#fde68a', date: 'Oct 18, 2023', status: 'Pending',
    },
    {
      id: '5', reqId: 'REQ-8996', title: 'Vinska Kuca Oplenac Wine Stay',
      stayType: 'Stay', imageBg: 'linear-gradient(135deg, #e11d48 0%, #be123c 100%)',
      address: 'Oplenac bb', city: 'Topola', region: 'Šumadija', country: 'Serbia',
      lat: 44.2500, lng: 20.6800,
      tags: ['Wine', 'Vineyard', 'Romantic'],
      amenities: ['wifi', 'breakfast', 'parking'],
      description: 'Wine estate stay at the foot of Oplenac hill.',
      bookingPartners: [ { id: 'booking', url: 'https://www.booking.com' }, { id: 'hotel', url: 'https://www.oplenacwinestay.rs' }, ],
      type: 'Place', author: 'Milica Vasić', authorInitials: 'MV',
      authorBg: '#fecdd3', date: 'Oct 20, 2023', status: 'Pending',
    },
    {
      id: '6', reqId: 'REQ-8997', title: 'Glamping Šumadija Forest Camp',
      stayType: 'Glamping', imageBg: 'linear-gradient(135deg, #06b6d4 0%, #0891b2 100%)',
      address: 'Šumska 1', city: 'Kragujevac', region: 'Šumadija', country: 'Serbia',
      lat: 44.0350, lng: 20.8800,
      tags: ['Glamping', 'Forest', 'Outdoor'],
      amenities: ['parking'],
      description: 'Unique glamping experience in the heart of Šumadija forests.',
      bookingPartners: [ { id: 'airbnb', url: 'https://www.airbnb.com' }, ],
      type: 'Place', author: 'Stefan Ristić', authorInitials: 'SR',
      authorBg: '#d1fae5', date: 'Oct 21, 2023', status: 'Pending',
    },
    {
      id: '7', reqId: 'REQ-8998', title: 'Wine & Harvest Festival Stay',
      stayType: 'Stay', imageBg: 'linear-gradient(135deg, #f59e0b 0%, #d97706 100%)',
      address: 'Vinogradska 8', city: 'Topola', region: 'Šumadija', country: 'Serbia',
      lat: 44.2480, lng: 20.6750,
      tags: ['Festival', 'Wine', 'Seasonal'],
      amenities: ['breakfast', 'parking'],
      description: 'Stay during the annual wine and harvest festival season.',
      bookingPartners: [ { id: 'booking', url: 'https://www.booking.com' }, { id: 'airbnb', url: 'https://www.airbnb.com' }, { id: 'hotel', url: 'https://www.wineharvestfestival.rs' }, ],
      type: 'Place', author: 'Dragana Milošević', authorInitials: 'DM',
      authorBg: '#ede9fe', date: 'Oct 30, 2023', status: 'Pending',
    },
  ];

  getDetailById(id: string): ReviewRequestDetail | undefined {
    // MOCK → zameniti sa: return this.http.get<ReviewRequestDetail>(`/api/requests/${id}/detail`);
    return this.requestDetails.find(r => r.id === id);
  }
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
  getById(id: string): ReviewRequest | undefined {
    // MOCK → zameniti sa: return this.http.get<ReviewRequest>(`/api/requests/${id}`);
    return this.requests.find(r => r.id === id);
  }
}