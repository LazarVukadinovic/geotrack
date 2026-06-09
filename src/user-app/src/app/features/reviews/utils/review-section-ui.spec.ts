import { describe, expect, it } from 'vitest';
import { hasRealReviews, reviewsMoreActionLabel } from './review-section-ui';

describe('review section UI rules', () => {
  it('does not show More for missing or empty review lists', () => {
    expect(hasRealReviews(undefined)).toBe(false);
    expect(hasRealReviews(null)).toBe(false);
    expect(hasRealReviews([])).toBe(false);
    expect(reviewsMoreActionLabel([])).toBeNull();
  });

  it('does not count non-review CTA placeholders as real reviews', () => {
    expect(hasRealReviews([{ label: 'Write a Review' }])).toBe(false);
    expect(reviewsMoreActionLabel([{ label: 'Write a Review' }])).toBeNull();
  });

  it('shows More when at least one real review exists', () => {
    expect(hasRealReviews([{ id: '15' }])).toBe(true);
    expect(reviewsMoreActionLabel([{ id: '15' }])).toBe('More');
  });
});
