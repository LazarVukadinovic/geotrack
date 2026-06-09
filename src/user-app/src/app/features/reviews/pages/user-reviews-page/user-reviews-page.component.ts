import { Component, DestroyRef, OnInit, inject, signal, computed } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs/operators';
import { ReviewsService } from '../../services/reviews.service';
import type { UserReviewItem } from '../../services/reviews.service';

@Component({
  selector: 'app-user-reviews-page',
  standalone: true,
  imports: [],
  templateUrl: './user-reviews-page.component.html',
  styleUrl: './user-reviews-page.component.css',
})
export class UserReviewsPageComponent implements OnInit {
  private readonly collapsedReviewLength = 140;
  protected readonly collapsedReviewLines = 3;
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly reviewsService = inject(ReviewsService);

  private readonly userIdParam = toSignal(
    this.route.paramMap.pipe(map((p) => p.get('userId'))),
    { initialValue: null },
  );

  protected readonly authorName = signal(this.readNavigationAuthorName());
  private readonly isOwnProfile = signal(this.readNavigationIsOwn());
  protected readonly pageTitle = computed(() => {
    if (this.isOwnProfile()) return 'My Reviews';
    const name = this.authorName();
    return name ? `${name}'s Reviews` : 'User Reviews';
  });

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
    globalThis.history.back();
  }

  protected reload(): void {
    this.load();
  }

  protected loadMore(): void {
    if (!this.hasMore() || this.isLoadingMore()) return;
    const userId = parseInt(this.userIdParam() ?? '', 10);
    if (isNaN(userId)) return;

    this.isLoadingMore.set(true);

    this.reviewsService.getUserReviews(userId, this.nextSkip(), this.pageSize)
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
    const route = item.entityType === 'event' ? '/events' : '/places';
    void this.router.navigate([route, item.entityId]);
  }

  protected starArray(): number[] {
    return [1, 2, 3, 4, 5];
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
    const userId = parseInt(this.userIdParam() ?? '', 10);
    if (isNaN(userId) || userId <= 0) {
      this.loadError.set('Invalid user.');
      this.isLoading.set(false);
      return;
    }

    this.isLoading.set(true);
    this.loadError.set(null);
    this.items.set([]);
    this.expandedReviewIds.set(new Set());
    this.hasMore.set(false);
    this.nextSkip.set(0);

    this.reviewsService.getUserReviews(userId, 0, this.pageSize)
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
          this.loadError.set('Could not load reviews. Please try again.');
          this.isLoading.set(false);
        },
      });
  }

  private readNavigationAuthorName(): string | null {
    const raw = history.state?.authorName;
    return typeof raw === 'string' && raw.trim() ? raw : null;
  }

  private readNavigationIsOwn(): boolean {
    return history.state?.isOwn === true;
  }
}
