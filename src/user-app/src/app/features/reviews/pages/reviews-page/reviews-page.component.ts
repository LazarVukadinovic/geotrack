import { Component, DestroyRef, computed, effect, inject, signal } from '@angular/core';
import { Location } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs/operators';
import { ImageFallbackDirective } from '../../../../shared/directives/image-fallback.directive';
import { AuthService } from '../../../../core/services/auth.service';
import { ReviewsService } from '../../services/reviews.service';
import type { FullReview, ReviewReportTargetType, ReviewsEntityType } from '../../services/reviews.service';

type ReviewsLoadState = 'idle' | 'loading' | 'loaded' | 'notFound' | 'error';

@Component({
  selector: 'app-reviews-page',
  standalone: true,
  imports: [ImageFallbackDirective],
  templateUrl: './reviews-page.component.html',
  styleUrl: './reviews-page.component.scss',
})
export class ReviewsPageComponent {
  private readonly pageSize = 10;
  private readonly route = inject(ActivatedRoute);
  private readonly location = inject(Location);
  private readonly destroyRef = inject(DestroyRef);
  private readonly reviewsService = inject(ReviewsService);
  private readonly auth = inject(AuthService);

  private readonly entityTypeParam = toSignal(
    this.route.paramMap.pipe(map((params) => params.get('entityType'))),
    { initialValue: null },
  );
  private readonly entityIdParam = toSignal(
    this.route.paramMap.pipe(map((params) => params.get('id'))),
    { initialValue: null },
  );
  private readonly queryTitle = toSignal(
    this.route.queryParamMap.pipe(map((params) => params.get('title'))),
    { initialValue: null },
  );

  private readonly stateTitle = signal(this.readNavigationTitle());
  private requestSeq = 0;
  private lastRequestKey: string | null = null;

  protected readonly reviews = signal<FullReview[]>([]);
  protected readonly loadState = signal<ReviewsLoadState>('idle');
  protected readonly averageRating = signal<number | null>(null);
  protected readonly reviewsCount = signal(0);
  protected readonly hasMore = signal(false);
  protected readonly nextSkip = signal(0);
  protected readonly loadingMore = signal(false);
  protected readonly reportMessage = signal('');
  protected readonly reportError = signal('');
  private readonly reportingReviewIds = signal<Record<string, boolean>>({});
  private readonly reportedReviewIds = signal<Record<string, boolean>>({});

  protected readonly entityKindLabel = computed(() => {
    switch (this.normalizeEntityType(this.entityTypeParam())) {
      case 'stay': return 'Stay';
      case 'event': return 'Event';
      case 'place': return 'Place';
      default: return 'Item';
    }
  });

  protected readonly entityTitle = computed(() => {
    const title = this.queryTitle() || this.stateTitle();
    return title?.trim() || `${this.entityKindLabel()} Reviews`;
  });

  protected readonly reviewsCountLabel = computed(() => {
    const count = this.reviewsCount();
    if (count === 1) return '1 review';
    return `${count} reviews`;
  });

  constructor() {
    effect(() => {
      this.loadReviews(this.entityTypeParam(), this.entityIdParam());
    });
  }

  protected goBack(): void {
    this.location.back();
  }

  protected retry(): void {
    this.lastRequestKey = null;
    this.loadReviews(this.entityTypeParam(), this.entityIdParam());
  }

  protected loadMore(): void {
    const entityType = this.normalizeEntityType(this.entityTypeParam());
    const entityId = parseInt(this.entityIdParam() ?? '', 10);

    if (!entityType || isNaN(entityId) || entityId <= 0 || !this.hasMore() || this.loadingMore()) {
      return;
    }

    const seq = this.requestSeq;
    this.loadingMore.set(true);

    this.reviewsService.getReviews(entityType, entityId, this.nextSkip(), this.pageSize)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          if (seq !== this.requestSeq) return;
          this.reviews.update(current => this.mergeReviews(current, result.reviews));
          this.averageRating.set(result.averageRating);
          this.reviewsCount.set(result.reviewsCount);
          this.nextSkip.set(result.nextSkip);
          this.hasMore.set(result.hasMore);
          this.loadingMore.set(false);
        },
        error: () => {
          if (seq !== this.requestSeq) return;
          this.loadingMore.set(false);
          this.reportMessage.set('');
          this.reportError.set('Could not load more reviews. Please try again.');
        },
      });
  }

  protected reviewInitials(authorName: string): string {
    const parts = authorName.trim().split(/\s+/).filter(Boolean);
    if (parts.length === 0) return 'GT';
    return parts.slice(0, 2).map((part) => part[0]?.toUpperCase() ?? '').join('') || 'GT';
  }

  protected trackReview(_index: number, review: FullReview): string {
    return review.id;
  }

  protected isReviewReported(review: FullReview): boolean {
    return review.reportedByCurrentUser || !!this.reportedReviewIds()[review.id];
  }

  protected isReviewReporting(reviewId: string): boolean {
    return !!this.reportingReviewIds()[reviewId];
  }

  protected reportReview(review: FullReview): void {
    if (!this.auth.isLoggedIn()) {
      this.reportMessage.set('');
      this.reportError.set('Log in to report a review.');
      return;
    }

    const targetType = this.reportTargetType();
    const reviewId = parseInt(review.id, 10);
    if (!targetType || isNaN(reviewId) || this.isReviewReporting(review.id)) return;

    this.reportMessage.set('');
    this.reportError.set('');
    this.setReviewReporting(review.id, true);

    this.reviewsService.reportReview(targetType, reviewId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          this.setReviewReporting(review.id, false);
          this.markReviewReported(review.id);
          this.reviews.update(current => current.map(item =>
            item.id === review.id
              ? { ...item, reportedByCurrentUser: true }
              : item,
          ));
          this.reportMessage.set(response.alreadyReported
            ? 'You have already reported this review.'
            : 'Thanks. A regional manager will review this report.');
        },
        error: (err) => {
          this.setReviewReporting(review.id, false);
          this.reportError.set(err?.error?.message || 'Could not report this review. Please try again.');
        },
      });
  }

  private loadReviews(entityTypeParam: string | null, entityIdParam: string | null): void {
    const entityType = this.normalizeEntityType(entityTypeParam);
    const entityId = parseInt(entityIdParam ?? '', 10);

    if (!entityType || isNaN(entityId) || entityId <= 0) {
      this.reviews.set([]);
      this.averageRating.set(null);
      this.reviewsCount.set(0);
      this.nextSkip.set(0);
      this.hasMore.set(false);
      this.loadingMore.set(false);
      this.loadState.set('notFound');
      return;
    }

    const requestKey = `${entityType}:${entityId}`;
    if (this.lastRequestKey === requestKey && this.loadState() === 'loading') return;

    this.lastRequestKey = requestKey;
    const seq = ++this.requestSeq;
    this.loadState.set('loading');
    this.loadingMore.set(false);
    this.hasMore.set(false);

    this.reviewsService.getReviews(entityType, entityId, 0, this.pageSize)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          if (seq !== this.requestSeq) return;
          this.reviews.set(result.reviews);
          this.averageRating.set(result.averageRating);
          this.reviewsCount.set(result.reviewsCount);
          this.nextSkip.set(result.nextSkip);
          this.hasMore.set(result.hasMore);
          this.reportMessage.set('');
          this.reportError.set('');
          this.reportingReviewIds.set({});
          this.reportedReviewIds.set({});
          this.loadState.set('loaded');
        },
        error: () => {
          if (seq !== this.requestSeq) return;
          this.reviews.set([]);
          this.averageRating.set(null);
          this.reviewsCount.set(0);
          this.nextSkip.set(0);
          this.hasMore.set(false);
          this.loadingMore.set(false);
          this.loadState.set('error');
        },
      });
  }

  private mergeReviews(current: FullReview[], incoming: FullReview[]): FullReview[] {
    const existingIds = new Set(current.map(review => review.id));
    return [
      ...current,
      ...incoming.filter(review => !existingIds.has(review.id)),
    ];
  }

  private normalizeEntityType(value: string | null): ReviewsEntityType | null {
    if (value === 'place' || value === 'stay' || value === 'event') return value;
    return null;
  }

  private reportTargetType(): ReviewReportTargetType | null {
    const entityType = this.normalizeEntityType(this.entityTypeParam());
    if (!entityType) return null;
    return entityType === 'event' ? 'event' : 'place';
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

  private readNavigationTitle(): string | null {
    const raw = history.state?.entityTitle;
    return typeof raw === 'string' && raw.trim() ? raw : null;
  }
}
