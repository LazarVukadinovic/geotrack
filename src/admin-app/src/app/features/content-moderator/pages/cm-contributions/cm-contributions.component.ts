import { ChangeDetectionStrategy, ChangeDetectorRef, Component, OnDestroy, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subject, finalize, takeUntil } from 'rxjs';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { AuthService } from '../../../../core/services/auth.service';
import {
  ContentModeratorContributionsService,
  CmContributionListItem,
} from '../../../../core/services/content-moderator/content-moderator-contributions.service';

@Component({
  selector: 'app-cm-contributions',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './cm-contributions.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CmContributionsComponent implements OnInit, OnDestroy {
  private readonly auth = inject(AuthService);
  private readonly service = inject(ContentModeratorContributionsService);
  private readonly router = inject(Router);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly destroy$ = new Subject<void>();

  items: CmContributionListItem[] = [];
  isLoading = false;
  errorMessage: string | null = null;
  processingId: number | null = null;
  rejectModal: { id: number; name: string } | null = null;
  rejectNote = '';
  feedbackMessage: string | null = null;
  private feedbackTimeout: ReturnType<typeof setTimeout> | null = null;
  private readonly failedImages = new Set<number>();

  get userName(): string {
    return this.auth.fullName || 'Content Moderator';
  }

  get userRole(): string {
    return this.auth.getUser()?.role || 'Content Moderator';
  }

  get pendingItems(): CmContributionListItem[] {
    return this.items.filter(i => i.status === 'PendingReview');
  }

  ngOnInit(): void {
    const state = history.state as { feedbackMessage?: unknown } | null;
    const msg = typeof state?.feedbackMessage === 'string' ? state.feedbackMessage.trim() : '';
    if (msg) this.feedbackMessage = msg;
    this.loadContributions();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
    if (this.feedbackTimeout) clearTimeout(this.feedbackTimeout);
  }

  loadContributions(): void {
    this.isLoading = true;
    this.errorMessage = null;
    this.cdr.markForCheck();

    this.service.getAll()
      .pipe(
        takeUntil(this.destroy$),
        finalize(() => {
          this.isLoading = false;
          this.cdr.markForCheck();
        }),
      )
      .subscribe({
        next: items => {
          this.items = items;
          this.cdr.markForCheck();
        },
        error: () => {
          this.errorMessage = 'Unable to load contributions for review.';
          this.cdr.markForCheck();
        },
      });
  }

  viewDetail(item: CmContributionListItem): void {
    this.router.navigate(['/content-moderator/contributions', item.id]);
  }

  approve(item: CmContributionListItem): void {
    if (this.processingId !== null) return;
    this.processingId = item.id;
    this.cdr.markForCheck();

    this.service.approve(item.id)
      .pipe(finalize(() => {
        this.processingId = null;
        this.cdr.markForCheck();
      }))
      .subscribe({
        next: () => {
          this.items = this.items.filter(i => i.id !== item.id);
          this.showFeedback('Contribution approved.');
        },
        error: (err) => {
          this.errorMessage = err?.error?.message || 'Unable to approve this contribution.';
          this.cdr.markForCheck();
        },
      });
  }

  openReject(item: CmContributionListItem): void {
    this.rejectNote = '';
    this.rejectModal = { id: item.id, name: item.name };
    this.cdr.markForCheck();
  }

  confirmReject(): void {
    if (!this.rejectModal || this.processingId !== null) return;
    const id = this.rejectModal.id;
    const note = this.rejectNote;
    this.rejectModal = null;
    this.processingId = id;
    this.cdr.markForCheck();

    this.service.reject(id, note)
      .pipe(finalize(() => {
        this.processingId = null;
        this.cdr.markForCheck();
      }))
      .subscribe({
        next: () => {
          this.items = this.items.filter(i => i.id !== id);
          this.showFeedback('Contribution rejected.');
        },
        error: (err) => {
          this.errorMessage = err?.error?.message || 'Unable to reject this contribution.';
          this.cdr.markForCheck();
        },
      });
  }

  cancelReject(): void {
    this.rejectModal = null;
    this.rejectNote = '';
    this.cdr.markForCheck();
  }

  hasThumbnail(item: CmContributionListItem): boolean {
    return !!item.cardImageUrl && !this.failedImages.has(item.id);
  }

  onImageError(item: CmContributionListItem): void {
    this.failedImages.add(item.id);
    this.cdr.markForCheck();
  }

  typeLabel(type: string): string {
    switch ((type ?? '').toLowerCase()) {
      case 'updateplace': return 'Update Place';
      case 'claimownership': return 'Claim Ownership';
      default: return 'New Place';
    }
  }

  locationSummary(item: CmContributionListItem): string {
    return item.location || 'Location outside mapped regions';
  }

  private showFeedback(msg: string): void {
    this.feedbackMessage = msg;
    if (this.feedbackTimeout) clearTimeout(this.feedbackTimeout);
    this.feedbackTimeout = setTimeout(() => {
      this.feedbackMessage = null;
      this.cdr.markForCheck();
    }, 4000);
    this.cdr.markForCheck();
  }
}
