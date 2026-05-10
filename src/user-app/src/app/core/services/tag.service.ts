import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { Tag } from '../models/tag.model';

export interface UserInterestItem {
  tagId: number;
  name: string;
  slug: string;
  description?: string;
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
  private readonly _interestRevision = signal(0);

  readonly tags = computed(() => this._tags());
  readonly interestRevision = computed(() => this._interestRevision());

  loadTags(): Observable<Tag[]> {
    return this.http.get<Tag[]>('/api/tags?forUser=true').pipe(
      tap(tags => this._tags.set(tags)),
    );
  }

  loadUserInterests(): Observable<UserInterestsResponse> {
    return this.http.get<UserInterestsResponse>('/api/user/me/interests');
  }

  /**
   * Replace the current user's interests with the given tag IDs (full-replace
   * semantics server-side). Errors propagate, callers surface them.
   */
  saveUserTags(tagIds: number[]): Observable<void> {
    return this.http.put<void>('/api/user/me/interests', { tagIds }).pipe(
      tap(() => this.bumpInterestRevision()),
    );
  }

  dismissInferredInterest(tagId: number): Observable<void> {
    return this.http.delete<void>(`/api/user/me/interests/inferred/${tagId}`).pipe(
      tap(() => this.bumpInterestRevision()),
    );
  }

  private bumpInterestRevision(): void {
    this._interestRevision.update(value => value + 1);
  }
}
