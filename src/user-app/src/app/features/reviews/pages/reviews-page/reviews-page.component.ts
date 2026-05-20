import { Component, DestroyRef, computed, effect, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs/operators';
import { ImageFallbackDirective } from '../../../../shared/directives/image-fallback.directive';
import { AuthService } from '../../../../core/services/auth.service';
import { ReviewsService } from '../../services/reviews.service';
import { ReviewInteractionsService } from '../../../../shared/services/review-interactions.service';
import type { FullReview, ReviewsEntityType } from '../../services/reviews.service';

type ReviewsLoadState = 'idle' | 'loading' | 'loaded' | 'notFound' | 'error';

@Component({
  selector: 'app-reviews-page',
  standalone: true,
  imports: [ImageFallbackDirective],
  providers: [ReviewInteractionsService],
  templateUrl: './reviews-page.component.html',
  styleUrl: './reviews-page.component.scss',
})
export class ReviewsPageComponent {
  private readonly pageSize = 10;
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly reviewsService = inject(ReviewsService);
  protected readonly auth = inject(AuthService);

  protected readonly ri = inject(ReviewInteractionsService);

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
    this.ri.configure({
      targetType: () => {
        const et = this.normalizeEntityType(this.entityTypeParam());
        if (!et) return null;
        return et === 'event' ? 'event' : 'place';
      },
      onEditSuccess: (reviewId, updated) => {
        this.reviews.update(current => current.map(item =>
          item.id === reviewId ? { ...item, rating: updated.rating, body: updated.body } : item,
        ));
      },
      onDeleteSuccess: (reviewId) => {
        this.reviews.update(current => current.filter(item => item.id !== reviewId));
        this.reviewsCount.update(n => Math.max(0, n - 1));
      },
      onReportSuccess: (reviewId) => {
        this.reviews.update(current => current.map(item =>
          item.id === reviewId ? { ...item, reportedByCurrentUser: true } : item,
        ));
      },
    });

    effect(() => {
      this.loadReviews(this.entityTypeParam(), this.entityIdParam());
    });
  }

  protected goBack(): void {
    globalThis.history.back();
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
          this.ri.reportError.set('Could not load more reviews. Please try again.');
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

  private readNavigationTitle(): string | null {
    const raw = history.state?.entityTitle;
    return typeof raw === 'string' && raw.trim() ? raw : null;
  }
}
