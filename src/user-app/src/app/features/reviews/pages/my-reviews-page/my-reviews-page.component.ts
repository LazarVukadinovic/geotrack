import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ReviewsService } from '../../services/reviews.service';
import type { UserReviewItem } from '../../services/reviews.service';

@Component({
  selector: 'app-my-reviews-page',
  standalone: true,
  imports: [],
  templateUrl: './my-reviews-page.component.html',
  styleUrl: './my-reviews-page.component.css',
})
export class MyReviewsPageComponent implements OnInit {
  private readonly collapsedReviewLength = 140;
  protected readonly collapsedReviewLines = 3;
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly reviewsService = inject(ReviewsService);

  protected readonly items = signal<UserReviewItem[]>([]);
  protected readonly isLoading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly hasMore = signal(false);
  protected readonly nextSkip = signal(0);
  protected readonly isLoadingMore = signal(false);
  protected readonly totalCount = signal(0);
  protected readonly expandedReviewIds = signal<Set<string>>(new Set());

  private readonly pageSize = 10;

  ngOnInit(): void {
    this.load();
  }

  protected goBack(): void {
    this.router.navigate(['/profile']);
  }

  protected reload(): void {
    this.load();
  }

  protected loadMore(): void {
    if (!this.hasMore() || this.isLoadingMore()) return;
    this.isLoadingMore.set(true);

    this.reviewsService.getMyReviews(this.nextSkip(), this.pageSize)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.items.update(current => [...current, ...result.items]);
          this.hasMore.set(result.hasMore);
          this.nextSkip.set(result.nextSkip);
          this.isLoadingMore.set(false);
        },
        error: () => {
          this.isLoadingMore.set(false);
        },
      });
  }

  protected openDetail(item: UserReviewItem): void {
    const route = this.detailRouteFor(item.entityType);
    void this.router.navigate([route, item.entityId]);
  }

  protected detailLabel(item: UserReviewItem): string {
    switch (item.entityType) {
      case 'event':
        return 'event';
      case 'destination':
        return 'destination';
      case 'location':
        return 'location';
      default:
        return 'place';
    }
  }

  private detailRouteFor(entityType: UserReviewItem['entityType']): string {
    switch (entityType) {
      case 'event':
        return '/events';
      case 'destination':
        return '/destinations';
      case 'location':
        return '/locations';
      default:
        return '/places';
    }
  }

  protected starArray(rating: number): number[] {
    return Array.from({ length: 5 }, (_, i) => i + 1);
  }

  protected isStarFilled(star: number, rating: number): boolean {
    return star <= rating;
  }

  protected hasLongReview(body: string | null | undefined): boolean {
    const value = (body ?? '').trim();
    const lineCount = value ? value.split(/\r\n|\r|\n/).length : 0;
    return value.length > this.collapsedReviewLength || lineCount > this.collapsedReviewLines;
  }

  protected isReviewExpanded(reviewId: string | number): boolean {
    return this.expandedReviewIds().has(String(reviewId));
  }

  protected toggleReviewExpanded(event: Event, reviewId: string | number): void {
    event.stopPropagation();
    const key = String(reviewId);
    this.expandedReviewIds.update(current => {
      const next = new Set(current);
      if (next.has(key)) {
        next.delete(key);
      } else {
        next.add(key);
      }
      return next;
    });
  }

  private load(): void {
    this.isLoading.set(true);
    this.loadError.set(null);
    this.items.set([]);
    this.expandedReviewIds.set(new Set());
    this.hasMore.set(false);
    this.nextSkip.set(0);

    this.reviewsService.getMyReviews(0, this.pageSize)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.items.set(result.items);
          this.totalCount.set(result.totalCount);
          this.hasMore.set(result.hasMore);
          this.nextSkip.set(result.nextSkip);
          this.isLoading.set(false);
        },
        error: () => {
          this.loadError.set('Could not load your reviews. Please try again.');
          this.isLoading.set(false);
        },
      });
  }
}
