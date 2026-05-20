import { Injectable, DestroyRef, inject, signal, computed } from '@angular/core';
import { Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ReviewsService } from '../../features/reviews/services/reviews.service';
import type { ReviewReason, ReviewReportTargetType } from '../../features/reviews/services/reviews.service';

export interface ReviewLike {
  id: string;
  rating: number;
  body: string;
  authorUserId: number;
  authorName: string;
  reportedByCurrentUser: boolean;
  isOwnReview?: boolean;
}

export interface ReviewInteractionsConfig {
  targetType: ReviewReportTargetType | (() => ReviewReportTargetType | null);
  onEditSuccess?: (reviewId: string, updated: { rating: number; body: string }) => void;
  onDeleteSuccess?: (reviewId: string) => void;
  onReportSuccess?: (reviewId: string) => void;
}

@Injectable()
export class ReviewInteractionsService {
  private readonly reviewsService = inject(ReviewsService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly router = inject(Router);

  private config!: ReviewInteractionsConfig;

  // --- Report ---
  readonly reportingReview = signal<ReviewLike | null>(null);
  readonly reportReasons = signal<ReviewReason[]>([]);
  readonly selectedReason = signal('');
  readonly customReason = signal('');
  readonly reasonsLoaded = signal(false);
  readonly reportMessage = signal('');
  readonly reportError = signal('');
  readonly isReportReasonOther = computed(() => this.selectedReason() === 'other');
  readonly reportSheetDragOffset = signal(0);

  private reportSheetIsDragging = false;
  private reportSheetDragStartY = 0;
  private readonly reportingReviewIds = signal<Record<string, boolean>>({});
  private readonly reportedReviewIds = signal<Record<string, boolean>>({});

  // --- Edit ---
  readonly editingReview = signal<ReviewLike | null>(null);
  readonly editRating = signal(0);
  readonly editBody = signal('');
  readonly isSavingEdit = signal(false);
  readonly editError = signal('');
  readonly editSheetDragOffset = signal(0);

  private editSheetIsDragging = false;
  private editSheetDragStartY = 0;

  // --- Delete ---
  readonly deletingReview = signal<ReviewLike | null>(null);
  readonly isDeleting = signal(false);
  readonly deleteError = signal('');

  configure(config: ReviewInteractionsConfig): void {
    this.config = config;
  }

  isReviewReported(review: { id: string; reportedByCurrentUser: boolean }): boolean {
    return review.reportedByCurrentUser || !!this.reportedReviewIds()[review.id];
  }

  isReviewReporting(reviewId: string): boolean {
    return !!this.reportingReviewIds()[reviewId];
  }

  goToAuthorReviews(review: { authorUserId: number; authorName: string; isOwnReview?: boolean }): void {
    if (!review.authorUserId) return;
    void this.router.navigate(['/users', review.authorUserId, 'reviews'], {
      state: { authorName: review.authorName, isOwn: !!review.isOwnReview },
    });
  }

  // --- Report flow ---

  openReportModal(review: ReviewLike, isLoggedIn: boolean): void {
    if (!isLoggedIn) {
      this.reportMessage.set('');
      this.reportError.set('Log in to report a review.');
      return;
    }
    this.reportingReview.set(review);
    this.selectedReason.set('');
    this.customReason.set('');
    this.reportMessage.set('');
    this.reportError.set('');
    if (!this.reasonsLoaded()) {
      this.reviewsService.getReportReasons()
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (reasons) => {
            this.reportReasons.set(reasons);
            this.reasonsLoaded.set(true);
          },
        });
    }
  }

  closeReportModal(): void {
    this.reportingReview.set(null);
    this.reportSheetDragOffset.set(0);
  }

  onReportSheetHandlePointerDown(event: PointerEvent): void {
    (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
    this.reportSheetIsDragging = true;
    this.reportSheetDragStartY = event.clientY;
    this.reportSheetDragOffset.set(0);
  }

  onReportSheetHandlePointerMove(event: PointerEvent): void {
    if (!this.reportSheetIsDragging) return;
    this.reportSheetDragOffset.set(Math.max(0, event.clientY - this.reportSheetDragStartY));
  }

  onReportSheetHandlePointerUp(): void {
    if (!this.reportSheetIsDragging) return;
    this.reportSheetIsDragging = false;
    if (this.reportSheetDragOffset() > 100) {
      this.closeReportModal();
    } else {
      this.reportSheetDragOffset.set(0);
    }
  }

  submitReport(): void {
    const review = this.reportingReview();
    if (!review) return;

    const targetType = this.resolveTargetType();
    const reviewId = parseInt(review.id, 10);
    const reason = this.selectedReason();
    if (!targetType || isNaN(reviewId) || !reason || this.isReviewReporting(review.id)) return;

    const note = reason === 'other' ? this.customReason().trim() : '';
    if (reason === 'other' && !note) return;

    this.reportMessage.set('');
    this.reportError.set('');
    this.setReviewReporting(review.id, true);

    this.reviewsService.reportReview(targetType, reviewId, reason, note)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          this.setReviewReporting(review.id, false);
          this.markReviewReported(review.id);
          this.config.onReportSuccess?.(review.id);
          this.reportMessage.set(response.alreadyReported
            ? 'You have already reported this review.'
            : 'Thanks. An Administrator will review this report.');
          this.reportingReview.set(null);
        },
        error: (err) => {
          this.setReviewReporting(review.id, false);
          this.reportError.set(err?.error?.message || 'Could not report this review. Please try again.');
          this.reportingReview.set(null);
        },
      });
  }

  // --- Edit flow ---

  openEditModal(review: ReviewLike): void {
    this.editingReview.set(review);
    this.editRating.set(Math.round(review.rating));
    this.editBody.set(review.body);
    this.editError.set('');
    this.editSheetDragOffset.set(0);
  }

  closeEditModal(): void {
    if (this.isSavingEdit()) return;
    this.editingReview.set(null);
    this.editSheetDragOffset.set(0);
  }

  onEditSheetHandlePointerDown(event: PointerEvent): void {
    (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
    this.editSheetIsDragging = true;
    this.editSheetDragStartY = event.clientY;
    this.editSheetDragOffset.set(0);
  }

  onEditSheetHandlePointerMove(event: PointerEvent): void {
    if (!this.editSheetIsDragging) return;
    this.editSheetDragOffset.set(Math.max(0, event.clientY - this.editSheetDragStartY));
  }

  onEditSheetHandlePointerUp(): void {
    if (!this.editSheetIsDragging) return;
    this.editSheetIsDragging = false;
    if (this.editSheetDragOffset() > 100) {
      this.closeEditModal();
    } else {
      this.editSheetDragOffset.set(0);
    }
  }

  setEditRating(rating: number): void {
    this.editRating.set(rating);
  }

  saveEdit(): void {
    const review = this.editingReview();
    if (!review || this.isSavingEdit()) return;

    const targetType = this.resolveTargetType();
    const reviewId = parseInt(review.id, 10);
    if (!targetType || isNaN(reviewId)) return;

    this.isSavingEdit.set(true);
    this.editError.set('');

    this.reviewsService.updateReview(targetType, reviewId, this.editRating(), this.editBody())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (updated) => {
          this.isSavingEdit.set(false);
          this.editingReview.set(null);
          this.config.onEditSuccess?.(review.id, { rating: updated.rating, body: updated.body ?? review.body });
        },
        error: (err) => {
          this.editError.set(err?.error?.message || 'Could not save your review. Please try again.');
          this.isSavingEdit.set(false);
        },
      });
  }

  // --- Delete flow ---

  openDeleteConfirm(review: ReviewLike): void {
    this.deletingReview.set(review);
    this.deleteError.set('');
  }

  cancelDelete(): void {
    if (this.isDeleting()) return;
    this.deletingReview.set(null);
  }

  confirmDelete(): void {
    const review = this.deletingReview();
    if (!review || this.isDeleting()) return;

    const targetType = this.resolveTargetType();
    const reviewId = parseInt(review.id, 10);
    if (!targetType || isNaN(reviewId)) return;

    this.isDeleting.set(true);
    this.deleteError.set('');

    this.reviewsService.deleteReview(targetType, reviewId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.isDeleting.set(false);
          this.deletingReview.set(null);
          this.config.onDeleteSuccess?.(review.id);
        },
        error: (err) => {
          this.deleteError.set(err?.error?.message || 'Could not delete this review. Please try again.');
          this.isDeleting.set(false);
        },
      });
  }

  private resolveTargetType(): ReviewReportTargetType | null {
    const t = this.config.targetType;
    return typeof t === 'function' ? t() : t;
  }

  private markReviewReported(reviewId: string): void {
    this.reportedReviewIds.update(current => ({ ...current, [reviewId]: true }));
  }

  private setReviewReporting(reviewId: string, isReporting: boolean): void {
    this.reportingReviewIds.update(current => {
      const next = { ...current };
      if (isReporting) {
        next[reviewId] = true;
      } else {
        delete next[reviewId];
      }
      return next;
    });
  }
}
