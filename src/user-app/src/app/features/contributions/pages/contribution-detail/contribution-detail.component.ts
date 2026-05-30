import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { ContributionDetail, ContributionsService, contributionCanEdit, contributionCanRequestOwnership } from '../../services/contributions.service';
import { BackgroundImageFallbackDirective } from '../../../../shared/directives/background-image-fallback.directive';

const MAX_PROOF_BYTES = 10 * 1024 * 1024;

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
  private readonly http = inject(HttpClient);

  protected readonly detail = signal<ContributionDetail | null>(null);
  protected readonly isLoading = signal(true);
  protected readonly hasError = signal(false);

  protected readonly ownershipClaimOpen = signal(false);
  protected readonly ownershipProofFile = signal<File | null>(null);
  protected readonly ownershipProofFileName = signal('');
  protected readonly ownershipClaimError = signal('');
  protected readonly ownershipClaimSubmitting = signal(false);

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

  protected readonly canEdit = computed(() => {
    const d = this.detail();
    return d ? contributionCanEdit(d) : false;
  });

  protected readonly canRequestOwnership = computed(() => {
    const d = this.detail();
    return d ? contributionCanRequestOwnership(d) : false;
  });

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

    if (detail.isOwnership && detail.status === 'Approved' && detail.publishedPlaceId != null) {
      this.router.navigate(['/contributions', detail.id, 'edit'], {
        state: {
          locationLocked: true,
        },
      });
      return;
    }

    const shouldLockLocation = detail.status === 'Approved' && detail.publishedPlaceId != null;
    this.router.navigate(['/contributions', detail.id, 'edit'], {
      state: shouldLockLocation ? { locationLocked: true } : undefined,
    });
  }

  protected openOwnershipModal(): void {
    this.ownershipProofFile.set(null);
    this.ownershipProofFileName.set('');
    this.ownershipClaimError.set('');
    this.ownershipClaimOpen.set(true);
  }

  protected closeOwnershipModal(): void {
    if (this.ownershipClaimSubmitting()) return;
    this.ownershipClaimOpen.set(false);
    this.ownershipProofFile.set(null);
    this.ownershipProofFileName.set('');
    this.ownershipClaimError.set('');
  }

  protected onOwnershipProofSelected(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    (event.target as HTMLInputElement).value = '';
    if (!file) return;
    this.ownershipClaimError.set('');
    const isPdf = file.type === 'application/pdf' || file.name.toLowerCase().endsWith('.pdf');
    if (!isPdf) { this.ownershipClaimError.set('Only PDF documents are accepted.'); return; }
    if (file.size > MAX_PROOF_BYTES) { this.ownershipClaimError.set('Document must be 10 MB or smaller.'); return; }
    this.ownershipProofFile.set(file);
    this.ownershipProofFileName.set(file.name);
  }

  protected submitOwnershipClaim(): void {
    const detail = this.detail();
    const file = this.ownershipProofFile();
    if (!detail || this.ownershipClaimSubmitting()) return;
    if (!file) { this.ownershipClaimError.set('Please attach a PDF document.'); return; }
    if (detail.publishedPlaceId == null) return;

    const images: { url: string; imageRole: string; sortOrder: number }[] = [];
    const cover = detail.coverImageUrl;
    const card = detail.cardImageUrl;
    if (cover) images.push({ url: cover, imageRole: 'cover', sortOrder: -10 });
    if (card && card !== cover) images.push({ url: card, imageRole: 'card', sortOrder: -9 });

    const formData = new FormData();
    formData.append('payload', JSON.stringify({
      type: 'ClaimOwnership',
      placeId: detail.publishedPlaceId,
      placeType: detail.placeType || 'Place',
      placeTypeId: null,
      name: detail.name,
      description: '',
      images: images.length > 0 ? images : undefined,
    }));
    formData.append('ownershipProofDocument', file);

    this.ownershipClaimSubmitting.set(true);
    this.ownershipClaimError.set('');

    this.http.post('/api/contributions/places', formData).subscribe({
      next: () => {
        this.ownershipClaimSubmitting.set(false);
        this.ownershipClaimOpen.set(false);
        this.ownershipProofFile.set(null);
        this.ownershipProofFileName.set('');
        this.fetchDetail();
      },
      error: (err: { error?: { message?: string } }) => {
        this.ownershipClaimSubmitting.set(false);
        this.ownershipClaimError.set(err?.error?.message || 'Could not submit ownership request. Please try again.');
      },
    });
  }

  protected goBack(): void {
    this.router.navigate(['/contributions']);
  }
}
