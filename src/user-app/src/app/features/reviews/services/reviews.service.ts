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

export interface ReportReviewResponse {
  reportId: number;
  alreadyReported: boolean;
  status: string;
  message: string;
}

@Injectable({ providedIn: 'root' })
export class ReviewsService {
  private readonly http = inject(HttpClient);

  getReviews(entityType: ReviewsEntityType, entityId: number): Observable<FullReviewsResult> {
    return this.http.get<ApiReviewDto[]>(this.buildEndpoint(entityType, entityId)).pipe(
      map((reviews) => {
        const mapped = reviews.map((review) => ({
          id: String(review.id),
          authorName: review.authorName || 'GeoTrack traveler',
          avatarUrl: review.avatarUrl ?? '',
          rating: review.rating,
          timeAgo: review.timeAgo ?? '',
          body: review.body ?? '',
          helpfulCount: review.helpfulCount ?? 0,
          reportedByCurrentUser: review.reportedByCurrentUser ?? false,
        }));
        const averageRating = mapped.length
          ? Math.round((mapped.reduce((sum, review) => sum + review.rating, 0) / mapped.length) * 10) / 10
          : null;

        return {
          reviews: mapped,
          averageRating,
          reviewsCount: mapped.length,
        };
      }),
    );
  }

  reportReview(targetType: ReviewReportTargetType, reviewId: number): Observable<ReportReviewResponse> {
    return this.http.post<ReportReviewResponse>(`/api/reviews/reports/${targetType}/${reviewId}`, {
      reason: 'offensive_content',
    });
  }

  private buildEndpoint(entityType: ReviewsEntityType, entityId: number): string {
    const collection = entityType === 'event' ? 'events' : 'places';
    return `/api/reviews/${collection}/${entityId}`;
  }
}
