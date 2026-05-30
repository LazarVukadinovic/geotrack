import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { ContributionListItem, ContributionsService, contributionCanEdit, contributionCanRequestOwnership } from '../../services/contributions.service';
import { ImageFallbackDirective } from '../../../../shared/directives/image-fallback.directive';

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

  // ── Edit ──────────────────────────────────────────────────────────────────

  protected editItem(event: Event, item: ContributionListItem): void {
    event.stopPropagation();
    if (item.isOwnership && item.status === 'Approved' && item.publishedPlaceId != null) {
      this.router.navigate(['/contributions', item.id, 'edit'], {
        state: {
          locationLocked: true,
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
    this.ownershipClaimItem.set(item);
  }

  protected closeOwnershipModal(): void {
    if (this.ownershipClaimSubmitting()) return;
    this.ownershipClaimItem.set(null);
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

    const images: { url: string; imageRole: string; sortOrder: number }[] = [];
    const cover = item.coverImageUrl;
    const card = item.cardImageUrl;
    if (cover) images.push({ url: cover, imageRole: 'cover', sortOrder: -10 });
    if (card && card !== cover) images.push({ url: card, imageRole: 'card', sortOrder: -9 });

    const formData = new FormData();
    formData.append('payload', JSON.stringify({
      type: 'ClaimOwnership',
      placeId: item.publishedPlaceId,
      placeType: item.placeType || 'Place',
      placeTypeId: null,
      name: item.name,
      description: '',
      images: images.length > 0 ? images : undefined,
    }));
    formData.append('ownershipProofDocument', file);

    this.ownershipClaimSubmitting.set(true);
    this.ownershipClaimError.set('');

    this.http.post('/api/contributions/places', formData).subscribe({
      next: () => {
        this.ownershipClaimSubmitting.set(false);
        this.ownershipClaimItem.set(null);
        this.ownershipProofFile.set(null);
        this.ownershipProofFileName.set('');
        this.contributions.loadMine();
      },
      error: (err) => {
        this.ownershipClaimSubmitting.set(false);
        this.ownershipClaimError.set(err?.error?.message || 'Could not submit ownership request. Please try again.');
      },
    });
  }

  // ── Delete modal ──────────────────────────────────────────────────────────

  protected requestDelete(event: Event, item: ContributionListItem): void {
    event.stopPropagation();
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
