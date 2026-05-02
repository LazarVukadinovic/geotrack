import { Component, OnInit, AfterViewInit, OnDestroy, ElementRef, ViewChild, NgZone, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { ReviewRequestsService } from '../../../../core/services/review-requests.service';

declare const L: any;

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

@Component({
  selector: 'app-review-request-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './request-detail.component.html',
})
export class RequestDetailComponent implements OnInit, AfterViewInit, OnDestroy {
  constructor(
    private router: Router,
    private route: ActivatedRoute,
    private zone: NgZone,
    private cdr: ChangeDetectorRef,
    private requestsService: ReviewRequestsService
  ) {}

  @ViewChild('miniMapContainer') miniMapContainer!: ElementRef<HTMLDivElement>;
  @ViewChild('modalMapContainer') modalMapContainerRef!: ElementRef<HTMLDivElement>;

  region   = 'Šumadija, Srbija';
  userName = 'Sarah Jenkins';
  userRole = 'Regional Manager';

  reviewerNotes = '';
  mapModalOpen  = false;

  private miniMap    : any = null;
  private modalMap   : any = null;
  private miniMarker : any = null;
  private modalMarker: any = null;

  request: ReviewRequestDetail = {
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

    description: 'A charming boutique villa nestled in the heart of Šumadija, offering guests a peaceful retreat surrounded by lush gardens. Each room is individually decorated with a blend of local heritage and modern comfort. Guests enjoy homemade breakfast every morning and have access to a private garden terrace. Perfect for couples and solo travelers looking for an authentic Serbian experience.',
    phone: '+381 34 123 456',
    website: 'www.vilasumadija.rs',
    bookingPartners: [
      { id: 'booking', url: 'https://www.booking.com/example' },
      { id: 'airbnb', url: 'https://www.airbnb.com/example' },
    ],

    type: 'Place',
    author: 'Marija Petrović',
    authorInitials: 'MP',
    authorBg: '#fbcfe8',
    authorEmail: 'm.petrovic@example.com',
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
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      const nav = history.state as ReviewRequestDetail;
      if (nav?.reqId) {
        this.request = { ...this.request, ...nav };
      }
    }
  }

  ngAfterViewInit(): void {
    setTimeout(() => this.initMiniMap(), 100);
  }

  ngOnDestroy(): void {
    this.destroyModalMap();
    if (this.miniMap) { this.miniMap.remove(); this.miniMap = null; }
  }

  private makePinIcon(): any {
    return L.divIcon({
      html: `<svg width="24" height="32" viewBox="0 0 24 32" xmlns="http://www.w3.org/2000/svg">
               <path d="M12 0C5.373 0 0 5.373 0 12c0 8 12 20 12 20S24 20 24 12C24 5.373 18.627 0 12 0z" fill="#2563eb" stroke="white" stroke-width="1.5"/>
               <circle cx="12" cy="12" r="4" fill="white"/>
             </svg>`,
      iconSize: [24, 32], iconAnchor: [12, 32], className: '',
    });
  }

  private initMiniMap(): void {
    if (this.miniMap || !this.miniMapContainer?.nativeElement) return;
    const el = this.miniMapContainer.nativeElement;

    const lat = this.request.lat ?? 44.0;
    const lng = this.request.lng ?? 20.9;
    const hasCoords = this.request.lat != null && this.request.lng != null;

    this.miniMap = L.map(el, {
      zoomControl: true,
      dragging: true,
      scrollWheelZoom: false,
      doubleClickZoom: false,
      touchZoom: false,
      attributionControl: false,
    }).setView([lat, lng], hasCoords ? 14 : 8);

    L.tileLayer('https://tiles.stadiamaps.com/tiles/alidade_smooth/{z}/{x}/{y}{r}.png', { maxZoom: 19 }).addTo(this.miniMap);

    if (hasCoords) {
      this.miniMarker = L.marker([lat, lng], { icon: this.makePinIcon(), draggable: false }).addTo(this.miniMap);
    }

    setTimeout(() => this.miniMap?.invalidateSize(), 200);
  }

  private initModalMap(el: HTMLDivElement): void {
    if (this.modalMap) return;
    const lat = this.request.lat ?? 44.0;
    const lng = this.request.lng ?? 20.9;
    const hasCoords = this.request.lat != null && this.request.lng != null;

    this.modalMap = L.map(el, { zoomControl: true, attributionControl: false })
      .setView([lat, lng], hasCoords ? 15 : 9);

    L.tileLayer('https://tiles.stadiamaps.com/tiles/alidade_smooth/{z}/{x}/{y}{r}.png', { maxZoom: 19 }).addTo(this.modalMap);

    if (hasCoords) {
      this.modalMarker = L.marker([lat, lng], { icon: this.makePinIcon(), draggable: false }).addTo(this.modalMap);
    }

    setTimeout(() => this.modalMap?.invalidateSize(), 200);
  }

  private destroyModalMap(): void {
    if (this.modalMap) { this.modalMap.remove(); this.modalMap = null; this.modalMarker = null; }
  }

  openMapModal(): void {
    this.mapModalOpen = true;
    setTimeout(() => this.initModalMap(this.modalMapContainerRef.nativeElement), 50);
  }

  closeMapModal(): void {
    this.mapModalOpen = false;
    this.destroyModalMap();
  }

  getAmenityLabel(id: string): string { return this.amenityMap[id] ?? id; }
  getPartnerLabel(id: string): string { return this.partnerMap[id] ?? id; }

  goBack(): void {
    this.router.navigate(['/requests']);
  }

  approveRequest(): void {
    const req = this.requestsService.requests.find(r => r.id === this.request.id);
    if (req) req.status = 'Approved';
    this.goBack();
  }

  rejectRequest(): void {
    const req = this.requestsService.requests.find(r => r.id === this.request.id);
    if (req) req.status = 'Rejected';
    this.goBack();
  }
}