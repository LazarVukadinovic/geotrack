import { Component, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { ContributionItem } from '../my-contributions/my-contributions.component';
import { ContributionDraft } from '../submit-contribution/submit-contribution.component';
import { BookingPartner } from '../submit-contribution/steps/step-details/step-details.component';

export interface ContributionDetail {
  id: string;
  name: string;
  location: string;
  address?: string;
  imageUrl: string;
  imgBgPosition?: string;
  imgBgSize?: string;
  status: 'APPROVED' | 'UNDER REVIEW' | 'REJECTED' | 'PENDING';
  updatedLabel: string;
  entityType: string;
  stayType?: string;
  lat?: number | null;
  lng?: number | null;
  description?: string;
  tags?: string[];
  amenities?: string[];
  bookingPartners?: BookingPartner[];
  phone?: string;
  website?: string;
}

@Component({
  selector: 'app-contribution-detail',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './contribution-detail.component.html',
})
export class ContributionDetailPageComponent implements OnInit {
  detail = signal<ContributionDetail | null>(null);
  isLoading = signal(true);
  hasError = signal(false);

  private item: ContributionItem | null = null;

  amenityMap: Record<string, string> = {
    pool: 'Swimming Pool',
    wifi: 'High-speed Wi-Fi',
    breakfast: 'Breakfast Included',
    parking: 'Free Parking',
    spa: 'Spa & Wellness',
    gym: 'Fitness Center',
    dining: 'On-site Dining',
    ac: 'Air Conditioning',
  };

  partnerMap: Record<string, string> = {
    booking: 'Booking.com',
    airbnb: 'Airbnb',
    hotel: 'Hotel Website',
  };

  statusMessages: Record<string, string> = {
    APPROVED: 'Your contribution is live and visible to all travelers.',
    'UNDER REVIEW': 'Our team is currently reviewing this contribution.',
    REJECTED: 'This contribution was not approved. You can edit and resubmit.',
    PENDING: 'Your contribution is pending submission.',
  };

  constructor(private router: Router, private http: HttpClient) {
    const nav = this.router.getCurrentNavigation();
    const state = (nav?.extras?.state ?? history.state) as any;
    if (state?.item) this.item = state.item;
  }

  ngOnInit(): void {
    if (!this.item) {
      this.router.navigate(['/contributions']);
      return;
    }
    this.fetchDetail(this.item.id);
  }

  fetchDetail(id: string): void {
    this.isLoading.set(true);
    this.hasError.set(false);
    this.detail.set(null);

    // Simulacija dok API nije spreman
    setTimeout(() => {
      this.detail.set({
        id: this.item!.id,
        name: this.item!.name,
        location: this.item!.location,
        imageUrl: this.item!.imageUrl,
        imgBgPosition: '50% 50%',
        imgBgSize: 'cover',
        status: this.item!.status,
        updatedLabel: this.item!.updatedLabel,
        entityType: this.item!.entityType,
        stayType: 'Villa',
        address: '2847 Pacific Coast Hwy',
        lat: 34.03523,
        lng: -118.67418,
        description: 'A stunning property with panoramic views. Wake up to breathtaking scenery and enjoy world-class amenities in a peaceful setting.',
        tags: ['Luxury', 'Beachfront', 'Romantic'],
        amenities: ['pool', 'wifi', 'breakfast', 'spa'],
        bookingPartners: [ { id: 'airbnb', url: '' }, { id: 'booking', url: '' }, ] as BookingPartner[],
        phone: '+1 (310) 555-0142',
        website: 'example.com',
      });
      this.isLoading.set(false);
    }, 600);

    // Kada API bude spreman:
    // this.http.get<ContributionDetail>(`/api/contributions/${id}`).subscribe({
    //   next: (data) => { this.detail.set(data); this.isLoading.set(false); },
    //   error: () => { this.hasError.set(true); this.isLoading.set(false); },
    // });
  }

  getAmenityLabel(id: string): string { return this.amenityMap[id] ?? id; }
  getPartnerLabel(id: string): string { return this.partnerMap[id] ?? id; }

  onEdit(): void {
    if (!this.item) return;
    const d = this.detail();
    const editDraft: ContributionDraft = {
      basics: {
        name: d?.name ?? this.item.name,
        stayType: d?.stayType ?? 'Hotel',
        address: d?.address ?? '',
        city: this.item.location.split(',')[0]?.trim() ?? '',
        region: '',
        country: this.item.location.split(',')[1]?.trim() ?? '',
        imageUrl: d?.imageUrl ?? this.item.imageUrl,
        lat: d?.lat ?? null,
        lng: d?.lng ?? null,
        imgBgPosition: d?.imgBgPosition ?? '50% 50%',
        imgBgSize: d?.imgBgSize ?? 'cover',
      },
      features: { tags: d?.tags ?? [], amenities: d?.amenities ?? [] },
      details: {
        description: d?.description ?? '',
        phone: d?.phone ?? '',
        website: d?.website ?? '',
        bookingPartners: (d?.bookingPartners ?? []) as BookingPartner[],
      },
    };
    this.router.navigate(['/contributions/submit'], {
      state: { entityType: this.item.entityType, editId: this.item.id, editDraft },
    });
  }

  goBack(): void { this.router.navigate(['/contributions']); }
}