import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
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

@Component({
  selector: 'app-review-queue',
  standalone: true,
  imports: [CommonModule, SidebarComponent, TopbarComponent],
  templateUrl: './requests-queue.component.html',
})
export class ReviewQueueComponent {
  constructor(private router: Router) {}

  region   = 'Šumadija, Srbija';
  userName = 'Sarah Jenkins';
  userRole = 'Regional Manager';

  requests: ReviewRequest[] = [];
}