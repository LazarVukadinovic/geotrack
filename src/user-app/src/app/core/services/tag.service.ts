import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of, catchError, tap } from 'rxjs';
import { Tag } from '../models/tag.model';

/**
 * Fallback tags used until the backend GET /api/tags endpoint exists.
 * IDs and slugs are provisional — they will be replaced by real backend data.
 */
const FALLBACK_TAGS: Tag[] = [
  { id: 1,  name: 'Food & Drink',  slug: 'food-and-drink' },
  { id: 2,  name: 'History',       slug: 'history' },
  { id: 3,  name: 'Beaches',       slug: 'beaches' },
  { id: 4,  name: 'Nature',        slug: 'nature' },
  { id: 5,  name: 'Museums',       slug: 'museums' },
  { id: 6,  name: 'Local Events',  slug: 'local-events' },
  { id: 7,  name: 'Nightlife',     slug: 'nightlife' },
  { id: 8,  name: 'Festivals',     slug: 'festivals' },
  { id: 9,  name: 'Adventure',     slug: 'adventure' },
  { id: 10, name: 'Scenic Walks',  slug: 'scenic-walks' },
  { id: 11, name: 'Culture',       slug: 'culture' },
  { id: 12, name: 'Shopping',      slug: 'shopping' },
];

@Injectable({ providedIn: 'root' })
export class TagService {
  private readonly http = inject(HttpClient);
  private readonly _tags = signal<Tag[]>(FALLBACK_TAGS);

  readonly tags = computed(() => this._tags());

  /**
   * Load available tags from the backend.
   * TODO: wire to GET /api/tags when the endpoint exists.
   * Currently falls back to the hardcoded list.
   */
  loadTags(): Observable<Tag[]> {
    return this.http.get<Tag[]>('/api/tags').pipe(
      tap(tags => this._tags.set(tags)),
      catchError(() => {
        this._tags.set(FALLBACK_TAGS);
        return of(FALLBACK_TAGS);
      }),
    );
  }

  /**
   * Save selected tag IDs for the current user.
   * TODO: wire to POST /api/user/tags (or similar) when the endpoint exists.
   * Currently a no-op that resolves successfully.
   */
  saveUserTags(tagIds: number[]): Observable<void> {
    return this.http.post<void>('/api/user/tags', { tagIds }).pipe(
      catchError(() => of(undefined)),
    );
  }
}
