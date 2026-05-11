import { describe, expect, it } from 'vitest';
import { hasAnyInterestSource, type UserInterestsResponse } from './tag.service';

describe('hasAnyInterestSource', () => {
  const empty: UserInterestsResponse = {
    tags: [],
    explicitTags: [],
    inferredTags: [],
  };

  it('returns false when no explicit or inferred interests are present', () => {
    expect(hasAnyInterestSource(empty)).toBe(false);
  });

  it('detects selected interests from the current explicitTags field', () => {
    expect(hasAnyInterestSource({
      ...empty,
      explicitTags: [{ tagId: 1, name: 'Nature', slug: 'nature' }],
    })).toBe(true);
  });

  it('detects selected interests from the legacy tags field', () => {
    expect(hasAnyInterestSource({
      tags: [{ tagId: 1, name: 'Nature', slug: 'nature' }],
    })).toBe(true);
  });

  it('detects inferred interests from activity', () => {
    expect(hasAnyInterestSource({
      ...empty,
      inferredTags: [{
        tagId: 2,
        name: 'History',
        slug: 'history',
        score: 5,
        lastInteractionAt: '2026-05-10T00:00:00Z',
      }],
    })).toBe(true);
  });
});
