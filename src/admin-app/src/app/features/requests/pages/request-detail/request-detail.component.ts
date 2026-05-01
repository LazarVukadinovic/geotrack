import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';

export interface BookingPartner {
  id: string;
  url: string;
}

export interface ReviewRequestDetail {
  id: string;
  reqId: string;
  internalId?: string;

  // Basics (Step 1)
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

  // Features (Step 2)
  tags?: string[];
  amenities?: string[];

  // Details (Step 3)
  description?: string;
  phone?: string;
  website?: string;
  bookingPartners?: BookingPartner[];

  // Meta
  type: 'Destination' | 'Place' | 'Location' | 'Event';
  author: string;
  authorInitials: string;
  authorBg: string;
  authorEmail?: string;
  date: string;
  status: 'Pending' | 'Approved' | 'Rejected';
}

@Component({
  selector: 'app-review-request-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './request-detail.component.html',
})
export class RequestDetailComponent implements OnInit {
  constructor(private router: Router, private route: ActivatedRoute) {}

  region   = 'Šumadija, Srbija';
  userName = 'Sarah Jenkins';
  userRole = 'Regional Manager';

  reviewerNotes = '';

  request: ReviewRequestDetail = {
    id: '1',
    reqId: 'REQ-8992',
    internalId: 'REG-SC-042',

    title: 'Emerald Cove Secret Beach',
    stayType: 'Villa',
    imageBg: 'linear-gradient(135deg, #0ea5e9 0%, #0369a1 100%)',
    address: '14 Mile Marker, Coastal Highway 1',
    city: 'South Region',
    region: 'Pacific Northwest',
    country: 'United States',
    lat: 34.0522,
    lng: 118.2437,

    tags: ['Beach', 'Nature', 'Hidden Gem', 'Snorkeling', 'Hiking'],
    amenities: ['wifi', 'parking', 'pool'],

    description: 'A pristine, secluded beach accessible only via a short hike through the coastal forest. Known for its crystal-clear emerald waters and unique rock formations. Perfect for snorkeling and escaping the crowds.',
    phone: '+1 (555) 123-4567',
    website: 'https://www.example.com',
    bookingPartners: [
      { id: 'booking', url: 'https://www.booking.com/example' },
      { id: 'airbnb', url: '' },
    ],

    type: 'Location',
    author: 'Sarah Jenkins',
    authorInitials: 'SJ',
    authorBg: '#fbcfe8',
    authorEmail: 's.jenkins@example.com',
    date: 'Oct 24, 2023',
    status: 'Pending',
  };

  amenityMap: Record<string, string> = {
    pool:      'Swimming Pool',
    wifi:      'High-speed Wi-Fi',
    breakfast: 'Breakfast Included',
    parking:   'Free Parking',
    spa:       'Spa & Wellness',
    gym:       'Fitness Center',
    dining:    'On-site Dining',
    ac:        'Air Conditioning',
  };

  partnerMap: Record<string, string> = {
    booking: 'Booking.com',
    airbnb:  'Airbnb',
    hotel:   'Hotel Website',
  };

  ngOnInit(): void {
    // const id = this.route.snapshot.paramMap.get('id');
    // this.requestService.getById(id).subscribe(r => this.request = r);
  }

  getAmenityLabel(id: string): string {
    return this.amenityMap[id] ?? id;
  }

  getPartnerLabel(id: string): string {
    return this.partnerMap[id] ?? id;
  }

  goBack(): void {
    this.router.navigate(['../'], { relativeTo: this.route });
  }

  approveRequest(): void {
    this.request.status = 'Approved';
    this.goBack();
  }

  rejectRequest(): void {
    this.request.status = 'Rejected';
    this.goBack();
  }
}