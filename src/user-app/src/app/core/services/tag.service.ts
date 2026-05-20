import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map, tap } from 'rxjs';
import { Tag } from '../models/tag.model';

export interface InterestTopicGroup {
  group: string;
  items: Tag[];
}

export interface UserInterestItem {
  interestTopicId?: number;
  tagId: number; // backwards-compatible alias from the backend
  name: string;
  slug: string;
  description?: string;
  iconKey?: string;
  group?: string;
  sortOrder?: number;
}

export interface InferredInterestItem extends UserInterestItem {
  score: number;
  lastInteractionAt: string;
  lastReason?: string | null;
}

export interface UserInterestsResponse {
  explicitTags?: UserInterestItem[];
  inferredTags?: InferredInterestItem[];
  tags: UserInterestItem[];
}

export function hasAnyInterestSource(interests: UserInterestsResponse | null | undefined): boolean {
  return (interests?.tags?.length ?? 0) > 0 ||
    (interests?.explicitTags?.length ?? 0) > 0 ||
    (interests?.inferredTags?.length ?? 0) > 0;
}

@Injectable({ providedIn: 'root' })
export class TagService {
  private readonly http = inject(HttpClient);
  private readonly _tags = signal<Tag[]>([]);
  private readonly _interestGroups = signal<InterestTopicGroup[]>([]);
  private readonly _interestRevision = signal(0);

  readonly tags = computed(() => this._tags());
  readonly interestGroups = computed(() => this._interestGroups());
  readonly interestRevision = computed(() => this._interestRevision());

  loadTags(): Observable<Tag[]> {
    return this.http.get<InterestTopicGroup[]>('/api/interests/onboarding').pipe(
      map(groups => this.flattenGroups(groups)),
      tap(({ groups, tags }) => {
        this._interestGroups.set(groups);
        this._tags.set(tags);
      }),
      map(({ tags }) => tags),
    );
  }

  loadUserInterests(): Observable<UserInterestsResponse> {
    return this.http.get<UserInterestsResponse>('/api/user/me/interests');
  }

  /**
   * Replace the current user's interests with the given interest topic IDs
   * (full-replace semantics server-side). Errors propagate, callers surface them.
   */
  saveUserTags(interestTopicIds: number[]): Observable<void> {
    return this.http.put<void>('/api/user/me/interests', { interestTopicIds }).pipe(
      tap(() => this.bumpInterestRevision()),
    );
  }

  dismissInferredInterest(interestTopicId: number): Observable<void> {
    return this.http.delete<void>(`/api/user/me/interests/inferred/${interestTopicId}`).pipe(
      tap(() => this.bumpInterestRevision()),
    );
  }

  private flattenGroups(groups: InterestTopicGroup[]): { groups: InterestTopicGroup[]; tags: Tag[] } {
    const normalizedGroups = (groups ?? [])
      .map(group => ({
        group: group.group || 'Interests',
        items: (group.items ?? []).map(item => ({ ...item, group: item.group || group.group || 'Interests' })),
      }))
      .filter(group => group.items.length > 0);

    const tags = normalizedGroups.flatMap(group => group.items);
    return { groups: normalizedGroups, tags };
  }

  private bumpInterestRevision(): void {
    this._interestRevision.update(value => value + 1);
  }
}
