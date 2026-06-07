export function hasRealReviews(reviews: readonly unknown[] | null | undefined): boolean {
  return !!reviews?.some((review) => {
    if (!review || typeof review !== 'object') return false;
    const id = (review as { id?: unknown }).id;
    return id != null && String(id).trim().length > 0;
  });
}

export function reviewsMoreActionLabel(reviews: readonly unknown[] | null | undefined): 'More' | null {
  return hasRealReviews(reviews) ? 'More' : null;
}
