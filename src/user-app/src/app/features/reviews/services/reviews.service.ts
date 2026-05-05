import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map } from 'rxjs';

export type ReviewsEntityType = 'place' | 'stay' | 'event';
export type ReviewReportTargetType = 'place' | 'event';

export interface FullReview {
  id: string;
  authorName: string;
  avatarUrl: string;
  rating: number;
  timeAgo: string;
  body: string;
  helpfulCount: number;
  reportedByCurrentUser: boolean;
}

export interface FullReviewsResult {
  reviews: FullReview[];
  averageRating: number | null;
  reviewsCount: number;
  skip: number;
  take: number;
  nextSkip: number;
  hasMore: boolean;
}

interface ApiReviewDto {
  id: number;
  authorName?: string | null;
  avatarUrl?: string | null;
  rating: number;
  timeAgo?: string | null;
  body?: string | null;
  helpfulCount?: number | null;
  reportedByCurrentUser?: boolean | null;
}

interface ApiPagedReviewsResponseDto {
  items?: ApiReviewDto[] | null;
  averageRating?: number | null;
  totalCount?: number | null;
  skip?: number | null;
  take?: number | null;
  nextSkip?: number | null;
  hasMore?: boolean | null;
}

export interface ReportReviewResponse {
  reportId: number;
  alreadyReported: boolean;
  status: string;
  message: string;
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
            authorName: review.authorName || 'GeoTrack traveler',
            avatarUrl: review.avatarUrl ?? '',
            rating: review.rating,
            timeAgo: review.timeAgo ?? '',
            body: review.body ?? '',
            helpfulCount: review.helpfulCount ?? 0,
            reportedByCurrentUser: review.reportedByCurrentUser ?? false,
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
          };
        }),
      );
  }

  reportReview(targetType: ReviewReportTargetType, reviewId: number): Observable<ReportReviewResponse> {
    return this.http.post<ReportReviewResponse>(`/api/reviews/reports/${targetType}/${reviewId}`, {
      reason: 'offensive_content',
    });
  }

  private buildEndpoint(entityType: ReviewsEntityType, entityId: number, skip: number, take: number): string {
    const collection = entityType === 'event' ? 'events' : 'places';
    return `/api/reviews/${collection}/${entityId}?skip=${skip}&take=${take}`;
  }
}
