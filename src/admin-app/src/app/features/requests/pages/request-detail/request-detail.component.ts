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

  ngOnInit(): void {}


  goBack(): void {
    this.router.navigate(['../'], { relativeTo: this.route });
  }
}