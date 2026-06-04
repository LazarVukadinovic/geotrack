import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { ContributionDetail, ContributionListItem, ContributionsService, contributionCanDelete, contributionCanEdit, contributionCanRequestOwnership } from '../../services/contributions.service';
import { ImageFallbackDirective } from '../../../../shared/directives/image-fallback.directive';
import { PlaceDetailService } from '../../../place-detail/services/place-detail.service';
import { HotelDetailService } from '../../../hotel-detail/services/hotel-detail.service';

const MAX_PROOF_BYTES = 10 * 1024 * 1024;

@Component({
  selector: 'app-my-contributions',
  standalone: true,
  imports: [CommonModule, ImageFallbackDirective],
  templateUrl: './my-contributions.component.html',
  styleUrl: './my-contributions.component.css',
})
export class MyContributionsPageComponent implements OnInit {
  private readonly router = inject(Router);
  private readonly http = inject(HttpClient);
  private readonly contributions = inject(ContributionsService);
  private readonly placeDetailService = inject(PlaceDetailService);
  private readonly hotelDetailService = inject(HotelDetailService);

  private readonly PAGE_INITIAL = 2;
  private readonly PAGE_STEP = 5;

  protected readonly items = this.contributions.items;

  /** Place IDs that already have a pending or approved ownership claim submitted by this user. */
  private readonly claimedPlaceIds = computed(() => {
    const ids = new Set<number>();
    for (const item of this.items()) {
      if (item.isOwnership && item.publishedPlaceId != null &&
          (item.status === 'PendingReview' || item.status === 'Approved')) {
        ids.add(item.publishedPlaceId);
      }
    }
    return ids;
  });

  protected readonly contributionItems = computed(() =>
    this.items().filter(item =>
      !item.isOwnership &&
      // Hide original contribution once an ownership claim has been submitted for it
      !(item.publishedPlaceId != null && this.claimedPlaceIds().has(item.publishedPlaceId)),
    ),
  );
  protected readonly ownershipItems = computed(() => this.items().filter(item => item.isOwnership));
  protected readonly ownershipsVisible = signal(this.PAGE_INITIAL);
  protected readonly contributionsVisible = signal(this.PAGE_INITIAL);
  protected readonly sections = computed(() => [
    {
      title: 'Ownerships',
      items: this.ownershipItems().slice(0, this.ownershipsVisible()),
      total: this.ownershipItems().length,
      visible: this.ownershipsVisible(),
      key: 'ownerships' as const,
    },
    {
      title: 'Contributions',
      items: this.contributionItems().slice(0, this.contributionsVisible()),
      total: this.contributionItems().length,
      visible: this.contributionsVisible(),
      key: 'contributions' as const,
    },
  ].filter(section => section.total > 0));
  protected readonly isLoading = this.contributions.isLoading;
  protected readonly loadError = this.contributions.loadError;

  // ── Delete modal ──────────────────────────────────────────────────────────
  protected readonly pendingDelete = signal<ContributionListItem | null>(null);
  protected readonly deleteError = signal<string | null>(null);
  protected readonly isDeleting = signal(false);

  // ── Ownership claim modal ─────────────────────────────────────────────────
  protected readonly ownershipClaimItem = signal<ContributionListItem | null>(null);
  protected readonly ownershipClaimIsEdit = signal(false);
  protected readonly ownershipClaimDetail = signal<ContributionDetail | null>(null);
  protected readonly ownershipClaimDetailLoading = signal(false);
  protected readonly ownershipProofFile = signal<File | null>(null);
  protected readonly ownershipProofFileName = signal('');
  protected readonly ownershipClaimError = signal('');
  protected readonly ownershipClaimSubmitting = signal(false);

  ngOnInit(): void {
    this.contributions.loadMine();
  }

  protected startContribution(): void {
    this.router.navigate(['/contributions/submit']);
  }

  // ── Display helpers ───────────────────────────────────────────────────────

  protected cardImage(item: ContributionListItem): string {
    return item.coverImageUrl || item.cardImageUrl || '';
  }

  protected canEdit(item: ContributionListItem): boolean {
    return contributionCanEdit(item);
  }

  protected canRequestOwnership(item: ContributionListItem): boolean {
    if (!contributionCanRequestOwnership(item)) return false;
    // Hide if there's already a pending or approved ownership claim for this place
    return item.publishedPlaceId == null || !this.claimedPlaceIds().has(item.publishedPlaceId);
  }

  protected canDelete(item: ContributionListItem): boolean {
    return contributionCanDelete(item);
  }

  // ── Edit ──────────────────────────────────────────────────────────────────

  protected editItem(event: Event, item: ContributionListItem): void {
    event.stopPropagation();
    if (item.isOwnership && item.status === 'Approved' && item.publishedPlaceId != null) {
      this.router.navigate(['/contributions', item.id, 'edit'], {
        state: { locationLocked: true },
      });
      return;
    }
    if (item.isOwnership && (item.status === 'PendingReview' || item.status === 'Rejected')) {
      this.ownershipProofFile.set(null);
      this.ownershipProofFileName.set('');
      this.ownershipClaimError.set('');
      this.ownershipClaimIsEdit.set(true);
      this.ownershipClaimDetail.set(null);
      this.ownershipClaimDetailLoading.set(true);
      this.ownershipClaimItem.set(item);
      this.contributions.getDetail(item.id).subscribe({
        next: (detail) => {
          this.ownershipClaimDetail.set(detail);
          this.ownershipClaimDetailLoading.set(false);
        },
        error: () => {
          this.ownershipClaimDetailLoading.set(false);
          this.ownershipClaimError.set('Could not load contribution details. Please try again.');
        },
      });
      return;
    }
    this.router.navigate(['/contributions', item.id, 'edit']);
  }

  // ── Ownership claim modal ─────────────────────────────────────────────────

  protected openOwnershipModal(event: Event, item: ContributionListItem): void {
    event.stopPropagation();
    this.ownershipProofFile.set(null);
    this.ownershipProofFileName.set('');
    this.ownershipClaimError.set('');
    this.ownershipClaimIsEdit.set(false);
    this.ownershipClaimDetail.set(null);
    this.ownershipClaimItem.set(item);
  }

  protected closeOwnershipModal(): void {
    if (this.ownershipClaimSubmitting()) return;
    this.ownershipClaimItem.set(null);
    this.ownershipClaimIsEdit.set(false);
    this.ownershipClaimDetail.set(null);
    this.ownershipClaimDetailLoading.set(false);
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
    const item = this.ownershipClaimItem();
    const file = this.ownershipProofFile();
    if (!item || this.ownershipClaimSubmitting()) return;
    if (!file) { this.ownershipClaimError.set('Please attach a PDF document.'); return; }
    if (item.publishedPlaceId == null) return;

    const isEdit = this.ownershipClaimIsEdit();
    const detail = this.ownershipClaimDetail();
    if (isEdit && !detail) { this.ownershipClaimError.set('Could not load contribution details. Please try again.'); return; }

    const images: { url: string; imageRole: string; sortOrder: number }[] = [];
    const cover = item.coverImageUrl;
    const card = item.cardImageUrl;
    if (cover) images.push({ url: cover, imageRole: 'cover', sortOrder: -10 });
    if (card && card !== cover) images.push({ url: card, imageRole: 'card', sortOrder: -9 });

    const basePayload = {
      type: 'ClaimOwnership',
      placeId: item.publishedPlaceId,
      placeType: item.placeType || 'Place',
      name: item.name,
      images: images.length > 0 ? images : undefined,
    };

    const payload = isEdit && detail ? {
      ...basePayload,
      placeTypeId: detail.placeTypeId ?? null,
      description: detail.description || '',
      locationId: detail.locationId ?? null,
      address: detail.address || '',
      city: detail.city || '',
      region: detail.region || '',
      country: detail.country || '',
      latitude: detail.latitude ?? null,
      longitude: detail.longitude ?? null,
      imagePosition: detail.imagePosition || '50% 50%',
      imageSize: detail.imageSize || 'cover',
      cardImagePosition: detail.cardImagePosition || '50% 50%',
      cardImageSize: detail.cardImageSize || 'cover',
      tags: detail.tags ?? [],
      amenities: detail.amenities ?? [],
      bookingPartners: detail.bookingPartners ?? [],
    } : { ...basePayload, placeTypeId: null, description: '' };

    const formData = new FormData();
    formData.append('payload', JSON.stringify(payload));
    formData.append('ownershipProofDocument', file);

    this.ownershipClaimSubmitting.set(true);
    this.ownershipClaimError.set('');

    const request$ = isEdit
      ? this.http.put(`/api/contributions/places/${item.id}`, formData)
      : this.http.post('/api/contributions/places', formData);

    request$.subscribe({
      next: () => {
        this.ownershipClaimSubmitting.set(false);
        this.ownershipClaimItem.set(null);
        this.ownershipClaimIsEdit.set(false);
        this.ownershipClaimDetail.set(null);
        this.ownershipProofFile.set(null);
        this.ownershipProofFileName.set('');
        this.contributions.loadMine();
      },
      error: (err: { error?: { message?: string } }) => {
        this.ownershipClaimSubmitting.set(false);
        this.ownershipClaimError.set(err?.error?.message || 'Could not submit ownership request. Please try again.');
      },
    });
  }

  // ── Delete modal ──────────────────────────────────────────────────────────

  protected requestDelete(event: Event, item: ContributionListItem): void {
    event.stopPropagation();
    if (!this.canDelete(item)) return;
    this.deleteError.set(null);
    this.pendingDelete.set(item);
  }

  protected cancelDelete(): void {
    if (this.isDeleting()) return;
    this.pendingDelete.set(null);
    this.deleteError.set(null);
  }

  protected confirmDelete(): void {
    const item = this.pendingDelete();
    if (!item || this.isDeleting()) return;
    if (!this.canDelete(item)) {
      this.pendingDelete.set(null);
      return;
    }

    this.isDeleting.set(true);
    this.deleteError.set(null);

    this.contributions.deleteContribution(item.id).subscribe({
      next: () => {
        this.pendingDelete.set(null);
        this.isDeleting.set(false);
      },
      error: () => {
        this.deleteError.set('Could not delete this contribution. Please try again.');
        this.isDeleting.set(false);
      },
    });
  }

  // ── Navigation ────────────────────────────────────────────────────────────

  protected goBack(): void {
    this.router.navigate(['/profile']);
  }

  protected openDetail(item: ContributionListItem): void {
    this.router.navigate(['/contributions', item.id]);
  }

  protected viewPlace(event: Event, item: ContributionListItem): void {
    event.stopPropagation();
    if (item.publishedPlaceId != null) {
      this.placeDetailService.invalidate(item.publishedPlaceId);
      this.hotelDetailService.invalidate(item.publishedPlaceId);
      this.router.navigate([this.isStay(item) ? '/stays' : '/places', item.publishedPlaceId]);
    }
  }

  private isStay(item: ContributionListItem): boolean {
    return (item.placeType ?? '').trim().toLowerCase() === 'stay' ||
      !!(item.stayType ?? '').trim();
  }

  protected reload(): void {
    this.contributions.loadMine();
  }

  protected loadMore(key: 'ownerships' | 'contributions'): void {
    const sig = key === 'ownerships' ? this.ownershipsVisible : this.contributionsVisible;
    const total = key === 'ownerships' ? this.ownershipItems().length : this.contributionItems().length;
    sig.set(Math.min(sig() + this.PAGE_STEP, total));
  }

  protected showLess(key: 'ownerships' | 'contributions'): void {
    const sig = key === 'ownerships' ? this.ownershipsVisible : this.contributionsVisible;
    sig.set(Math.max(sig() - this.PAGE_STEP, this.PAGE_INITIAL));
  }
}
