import { Injectable, computed, signal } from '@angular/core';
import { delay, Observable, of } from 'rxjs';
import type { FoodMusicItem } from '../models/food-music.model';
import { MOCK_FOOD_MUSIC } from '../data/mock-food-music.data';

/**
 * Food & Music listing data access.
 * Replace mock observables with HttpClient calls when the API is ready.
 */
@Injectable({ providedIn: 'root' })
export class FoodMusicService {
  private readonly _items = signal<FoodMusicItem[]>([...MOCK_FOOD_MUSIC]);
  private readonly _savedOverrides = signal<Record<string, boolean>>({});

  readonly items = computed(() => {
    const overrides = this._savedOverrides();
    return this._items().map((item) => ({
      ...item,
      isSaved: overrides[item.id] !== undefined ? overrides[item.id] : item.isSaved,
    }));
  });

  /** API-shaped stream — swap for HttpClient.get<FoodMusicItem[]>('/api/food-music') when ready. */
  getItems$(): Observable<FoodMusicItem[]> {
    return of(this.items()).pipe(delay(0));
  }

  toggleSaved(id: string): void {
    const current = this.items().find((item) => item.id === id)?.isSaved ?? false;
    this._savedOverrides.update((m) => ({ ...m, [id]: !current }));
  }
}
