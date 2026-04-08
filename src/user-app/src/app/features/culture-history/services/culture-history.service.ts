import { Injectable, computed, signal } from '@angular/core';
import { delay, Observable, of } from 'rxjs';
import type { CultureHistoryItem } from '../models/culture-history.model';
import { MOCK_CULTURE_HISTORY } from '../data/mock-culture-history.data';

/**
 * Culture & History listing data access.
 * Replace mock observables with HttpClient calls when the API is ready.
 */
@Injectable({ providedIn: 'root' })
export class CultureHistoryService {
  private readonly _items = signal<CultureHistoryItem[]>([...MOCK_CULTURE_HISTORY]);
  private readonly _savedOverrides = signal<Record<string, boolean>>({});

  readonly items = computed(() => {
    const overrides = this._savedOverrides();
    return this._items().map((item) => ({
      ...item,
      isSaved: overrides[item.id] !== undefined ? overrides[item.id] : item.isSaved,
    }));
  });

  /** API-shaped stream — swap for HttpClient.get<CultureHistoryItem[]>('/api/culture-history') when ready. */
  getItems$(): Observable<CultureHistoryItem[]> {
    return of(this.items()).pipe(delay(0));
  }

  toggleSaved(id: string): void {
    const current = this.items().find((item) => item.id === id)?.isSaved ?? false;
    this._savedOverrides.update((m) => ({ ...m, [id]: !current }));
  }
}
