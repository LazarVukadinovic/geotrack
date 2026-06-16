import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map } from 'rxjs';

export type ReviewsEntityType = 'place' | 'stay' | 'event' | 'destination' | 'location';
export type ReviewReportTargetType = 'place' | 'event' | 'destination' | 'location';

export interface FullReview {
  id: string;
  authorUserId: number;
  authorName: string;
  avatarUrl: string;
  rating: number;
  timeAgo: string;
  body: string;
  helpfulCount: number;
  reportedByCurrentUser: boolean;
  isOwnReview: boolean;
}

export interface FullReviewsResult {
  reviews: FullReview[];
  averageRating: number | null;
  reviewsCount: number;
  skip: number;
  take: number;
  nextSkip: number;
  hasMore: boolean;
  isEntityOwnedByCurrentUser: boolean;
}

export interface UserReviewItem {
  id: number;
  entityType: 'place' | 'event' | 'destination' | 'location';
  entityId: number;
  entityName: string;
  rating: number;
  body: string;
  timeAgo: string;
}

export interface UserReviewsResult {
  items: UserReviewItem[];
  totalCount: number;
  skip: number;
  take: number;
  nextSkip: number;
  hasMore: boolean;
}

export interface ReviewReason {
  value: string;
  label: string;
}

export interface ReportReviewResponse {
  reportId: number;
  alreadyReported: boolean;
  status: string;
  message: string;
}

interface ApiReviewDto {
  id: number;
  authorUserId?: number | null;
  authorName?: string | null;
  avatarUrl?: string | null;
  rating: number;
  createdAt?: string | null;
  updatedAt?: string | null;
  timeAgo?: string | null;
  body?: string | null;
  helpfulCount?: number | null;
  reportedByCurrentUser?: boolean | null;
  isOwnReview?: boolean | null;
}

interface ApiPagedReviewsResponseDto {
  items?: ApiReviewDto[] | null;
  averageRating?: number | null;
  totalCount?: number | null;
  skip?: number | null;
  take?: number | null;
  nextSkip?: number | null;
  hasMore?: boolean | null;
  isEntityOwnedByCurrentUser?: boolean | null;
}

interface ApiUserReviewItemDto {
  id: number;
  entityType: string;
  entityId: number;
  entityName?: string | null;
  rating: number;
  body?: string | null;
  timeAgo?: string | null;
}

interface ApiPagedUserReviewsResponseDto {
  items?: ApiUserReviewItemDto[] | null;
  totalCount?: number | null;
  skip?: number | null;
  take?: number | null;
  nextSkip?: number | null;
  hasMore?: boolean | null;
}

@Injectable({ providedIn: 'root' })
export class ReviewsService {
  private readonly http = inject(HttpClient);

  getReviews(
    entityType: ReviewsEntityType,
    entityId: number,
    skip = 0,
    take = 10,
  ): Observable<FullReviewsResult> {
    return this.http
      .get<ApiPagedReviewsResponseDto | ApiReviewDto[]>(this.buildEndpoint(entityType, entityId, skip, take))
      .pipe(
        map((response) => {
          const apiReviews = Array.isArray(response) ? response : response.items ?? [];
          const mapped = apiReviews.map((review) => ({
            id: String(review.id),
            authorUserId: review.authorUserId ?? 0,
            authorName: review.authorName || 'GeoTrack traveler',
            avatarUrl: review.avatarUrl ?? '',
            rating: review.rating,
            timeAgo: review.timeAgo ?? '',
            body: review.body ?? '',
            helpfulCount: review.helpfulCount ?? 0,
            reportedByCurrentUser: review.reportedByCurrentUser ?? false,
            isOwnReview: review.isOwnReview ?? false,
          }));
          const fallbackAverageRating = mapped.length
            ? Math.round((mapped.reduce((sum, review) => sum + review.rating, 0) / mapped.length) * 10) / 10
            : null;

          if (Array.isArray(response)) {
            return {
              reviews: mapped,
              averageRating: fallbackAverageRating,
              reviewsCount: mapped.length,
              skip,
              take,
              nextSkip: skip + mapped.length,
              hasMore: false,
              isEntityOwnedByCurrentUser: false,
            };
          }

          return {
            reviews: mapped,
            averageRating: response.averageRating ?? fallbackAverageRating,
            reviewsCount: response.totalCount ?? mapped.length,
            skip: response.skip ?? skip,
            take: response.take ?? take,
            nextSkip: response.nextSkip ?? skip + mapped.length,
            hasMore: response.hasMore ?? false,
            isEntityOwnedByCurrentUser: response.isEntityOwnedByCurrentUser ?? false,
          };
        }),
      );
  }

  getMyReviews(skip = 0, take = 10): Observable<UserReviewsResult> {
    return this.http
      .get<ApiPagedUserReviewsResponseDto>(`/api/reviews/my?skip=${skip}&take=${take}`)
      .pipe(map(this.mapUserReviewsResult));
  }

  getUserReviews(userId: number, skip = 0, take = 10): Observable<UserReviewsResult> {
    return this.http
      .get<ApiPagedUserReviewsResponseDto>(`/api/reviews/users/${userId}?skip=${skip}&take=${take}`)
      .pipe(map(this.mapUserReviewsResult));
  }

  createReview(entityType: ReviewsEntityType, entityId: number, rating: number, comment: string): Observable<ApiReviewDto> {
    return this.http.post<ApiReviewDto>(`/api/reviews/${this.collectionFor(entityType)}/${entityId}`, {
      rating,
      comment,
    });
  }

  getReportReasons(): Observable<ReviewReason[]> {
    return this.http.get<ReviewReason[]>('/api/reviews/report-reasons');
  }

  reportReview(
    targetType: ReviewReportTargetType,
    reviewId: number,
    reason: string,
    note?: string,
  ): Observable<ReportReviewResponse> {
    return this.http.post<ReportReviewResponse>(`/api/reviews/reports/${targetType}/${reviewId}`, {
      reason,
      note: note ?? '',
    });
  }

  deleteReview(targetType: ReviewReportTargetType, reviewId: number): Observable<void> {
    return this.http.delete<void>(`/api/reviews/${this.collectionFor(targetType)}/${reviewId}`);
  }

  updateReview(
    targetType: ReviewReportTargetType,
    reviewId: number,
    rating: number,
    comment: string,
  ): Observable<ApiReviewDto> {
    return this.http.put<ApiReviewDto>(`/api/reviews/${this.collectionFor(targetType)}/${reviewId}`, { rating, comment });
  }

  private mapUserReviewsResult = (response: ApiPagedUserReviewsResponseDto): UserReviewsResult => {
    const items = (response.items ?? []).map((item) => ({
      id: item.id,
      entityType: this.normalizeUserReviewEntityType(item.entityType),
      entityId: item.entityId,
      entityName: item.entityName ?? '',
      rating: item.rating,
      body: item.body ?? '',
      timeAgo: item.timeAgo ?? '',
    }));

    return {
      items,
      totalCount: response.totalCount ?? items.length,
      skip: response.skip ?? 0,
      take: response.take ?? 10,
      nextSkip: response.nextSkip ?? items.length,
      hasMore: response.hasMore ?? false,
    };
  };

  private normalizeUserReviewEntityType(value: string): 'place' | 'event' | 'destination' | 'location' {
    switch ((value ?? '').toLowerCase()) {
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

  private buildEndpoint(entityType: ReviewsEntityType, entityId: number, skip: number, take: number): string {
    const collection = this.collectionFor(entityType);
    return `/api/reviews/${collection}/${entityId}?skip=${skip}&take=${take}`;
  }

  private collectionFor(entityType: ReviewsEntityType | ReviewReportTargetType): string {
    switch (entityType) {
      case 'event':
        return 'events';
      case 'destination':
        return 'destinations';
      case 'location':
        return 'locations';
      default:
        return 'places';
    }
  }
}
