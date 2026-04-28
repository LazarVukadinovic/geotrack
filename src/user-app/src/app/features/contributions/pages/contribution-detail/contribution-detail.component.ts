import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { ContributionDetail, ContributionsService } from '../../services/contributions.service';
import { BackgroundImageFallbackDirective } from '../../../../shared/directives/background-image-fallback.directive';

@Component({
  selector: 'app-contribution-detail',
  standalone: true,
  imports: [CommonModule, BackgroundImageFallbackDirective],
  templateUrl: './contribution-detail.component.html',
  styleUrl: './contribution-detail.component.css',
})
export class ContributionDetailPageComponent implements OnInit {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly contributions = inject(ContributionsService);

  protected readonly detail = signal<ContributionDetail | null>(null);
  protected readonly isLoading = signal(true);
  protected readonly hasError = signal(false);

  protected readonly amenityMap: Record<string, string> = {
    pool: 'Swimming Pool',
    wifi: 'High-speed Wi-Fi',
    breakfast: 'Breakfast Included',
    parking: 'Free Parking',
    spa: 'Spa & Wellness',
    gym: 'Fitness Center',
    dining: 'On-site Dining',
    restaurant: 'On-site Dining',
    ac: 'Air Conditioning',
  };

  protected readonly partnerMap: Record<string, string> = {
    booking: 'Booking.com',
    airbnb: 'Airbnb',
    hotel: 'Hotel Website',
    expedia: 'Expedia',
  };

  protected readonly statusMessages: Record<string, string> = {
    Approved: 'Your contribution is live and visible to travelers.',
    PendingReview: 'Your contribution is under review before it appears publicly.',
    Rejected: 'This contribution was not approved. You can edit and resubmit it.',
  };

  ngOnInit(): void {
    this.fetchDetail();
  }

  protected fetchDetail(): void {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    if (!id) {
      this.router.navigate(['/contributions']);
      return;
    }

    this.isLoading.set(true);
    this.hasError.set(false);
    this.detail.set(null);

    this.contributions.getDetail(id).subscribe({
      next: (data) => {
        this.detail.set(data);
        this.isLoading.set(false);
      },
      error: () => {
        this.hasError.set(true);
        this.isLoading.set(false);
      },
    });
  }

  protected getAmenityLabel(id: string): string {
    return this.amenityMap[id] ?? id;
  }

  protected getPartnerLabel(id: string): string {
    return this.partnerMap[id] ?? id;
  }

  protected onEdit(): void {
    const detail = this.detail();
    if (!detail) return;

    this.router.navigate(['/contributions', detail.id, 'edit']);
  }

  protected goBack(): void {
    this.router.navigate(['/contributions']);
  }
}
