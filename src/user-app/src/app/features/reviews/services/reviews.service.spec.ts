import '@angular/compiler';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ReviewsService } from './reviews.service';
import type { FullReviewsResult } from './reviews.service';

describe('ReviewsService', () => {
  let service: ReviewsService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });

    service = TestBed.inject(ReviewsService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    TestBed.resetTestingModule();
  });

  it('maps paginated place review responses and sends skip/take', () => {
    let actual: FullReviewsResult | undefined;

    service.getReviews('place', 42, 10, 5).subscribe(result => {
      actual = result;
    });

    const req = httpMock.expectOne('/api/reviews/places/42?skip=10&take=5');
    expect(req.request.method).toBe('GET');
    req.flush({
      items: [
        {
          id: 11,
          authorName: 'Mina',
          avatarUrl: null,
          rating: 5,
          timeAgo: '2d ago',
          body: 'Lovely.',
          helpfulCount: null,
          reportedByCurrentUser: true,
        },
      ],
      averageRating: 4.7,
      totalCount: 12,
      skip: 10,
      take: 5,
      nextSkip: 11,
      hasMore: true,
    });

    expect(actual).toEqual(makeExpectedResult());
  });

  it('keeps legacy array responses readable as a compatibility fallback', () => {
    let actualReviewsCount = -1;

    service.getReviews('event', 9).subscribe(result => {
      actualReviewsCount = result.reviewsCount;
    });

    const req = httpMock.expectOne('/api/reviews/events/9?skip=0&take=10');
    req.flush([
      { id: 1, authorName: null, avatarUrl: null, rating: 4, timeAgo: null, body: null },
      { id: 2, authorName: 'Traveler', avatarUrl: '', rating: 2, timeAgo: 'today', body: 'Okay' },
    ]);

    expect(actualReviewsCount).toBe(2);
  });

  it('preserves author avatar URLs for review avatar rendering', () => {
    let actualAvatarUrl = '';

    service.getReviews('place', 42).subscribe(result => {
      actualAvatarUrl = result.reviews[0]?.avatarUrl ?? '';
    });

    const req = httpMock.expectOne('/api/reviews/places/42?skip=0&take=10');
    req.flush({
      items: [
        {
          id: 12,
          authorName: 'Avatar User',
          avatarUrl: '/media/uploads/avatars/avatar-user.png',
          rating: 5,
          body: 'Great.',
        },
      ],
    });

    expect(actualAvatarUrl).toBe('/media/uploads/avatars/avatar-user.png');
  });

  it('creates destination reviews through the central reviews API', () => {
    service.createReview('destination', 17, 5, 'Excellent trip.').subscribe();

    const req = httpMock.expectOne('/api/reviews/destinations/17');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      rating: 5,
      comment: 'Excellent trip.',
    });

    req.flush({ id: 7, rating: 5, body: 'Excellent trip.' });
  });
});

function makeExpectedResult() {
  return {
    reviews: [
      {
        id: '11',
        authorUserId: 0,
        authorName: 'Mina',
        avatarUrl: '',
        rating: 5,
        timeAgo: '2d ago',
        body: 'Lovely.',
        helpfulCount: 0,
        reportedByCurrentUser: true,
        isOwnReview: false,
      },
    ],
    averageRating: 4.7,
    reviewsCount: 12,
    skip: 10,
    take: 5,
    nextSkip: 11,
    hasMore: true,
  };
}
