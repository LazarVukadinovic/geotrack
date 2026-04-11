import { Injectable, computed, signal } from '@angular/core';
import { delay, Observable, of } from 'rxjs';
import type { NatureItem } from '../models/nature.model';
import { MOCK_NATURE } from '../data/mock-nature.data';

/**
 * Nature listing data access.
 * Replace mock observables with HttpClient calls when the API is ready.
 */
@Injectable({ providedIn: 'root' })
export class NatureService {
  private readonly _items = signal<NatureItem[]>([...MOCK_NATURE]);
  private readonly _savedOverrides = signal<Record<string, boolean>>({});

  readonly items = computed(() => {
    const overrides = this._savedOverrides();
    return this._items().map((item) => ({
      ...item,
      isSaved: overrides[item.id] !== undefined ? overrides[item.id] : item.isSaved,
    }));
  });

  /** API-shaped stream — swap for HttpClient.get<NatureItem[]>('/api/nature') when ready. */
  getItems$(): Observable<NatureItem[]> {
    return of(this.items()).pipe(delay(0));
  }

  toggleSaved(id: string): void {
    const current = this.items().find((item) => item.id === id)?.isSaved ?? false;
    this._savedOverrides.update((m) => ({ ...m, [id]: !current }));
  }
}
