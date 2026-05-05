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

@Injectable({ providedIn: 'root' })
export class TagService {
  private readonly http = inject(HttpClient);
  private readonly _tags = signal<Tag[]>([]);

  readonly tags = computed(() => this._tags());

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
    return this.http.put<void>('/api/user/me/interests', { tagIds });
  }

  dismissInferredInterest(tagId: number): Observable<void> {
    return this.http.delete<void>(`/api/user/me/interests/inferred/${tagId}`);
  }
}
