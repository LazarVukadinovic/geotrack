import { Injectable, effect, inject, signal } from '@angular/core';
import { AuthService } from './auth.service';

export type FavoriteEntityType = 'place' | 'event';

type FavoriteStateSnapshot = Record<FavoriteEntityType, Record<string, boolean>>;

@Injectable({ providedIn: 'root' })
export class FavoriteStateService {
  private readonly auth = inject(AuthService);
  private readonly _state = signal<FavoriteStateSnapshot>({
    place: {},
    event: {},
  });

  constructor() {
    effect(() => {
      if (!this.auth.isLoggedIn()) {
        this._state.set({
          place: {},
          event: {},
        });
      }
    });
  }

  resolve(entityType: FavoriteEntityType, id: string, fallback: boolean): boolean {
    return this._state()[entityType][id] ?? fallback;
  }

  set(entityType: FavoriteEntityType, id: string, saved: boolean): void {
    this._state.update((state) => ({
      ...state,
      [entityType]: {
        ...state[entityType],
        [id]: saved,
      },
    }));
  }

  setMany(entityType: FavoriteEntityType, ids: Iterable<string | number>, saved: boolean): void {
    const nextEntries: Record<string, boolean> = {};
    for (const id of ids) {
      nextEntries[String(id)] = saved;
    }

    if (Object.keys(nextEntries).length === 0) return;

    this._state.update((state) => ({
      ...state,
      [entityType]: {
        ...state[entityType],
        ...nextEntries,
      },
    }));
  }

  reconcileKnown(entityType: FavoriteEntityType, savedIds: Iterable<string | number>): void {
    const savedSet = new Set(Array.from(savedIds, id => String(id)));

    this._state.update((state) => {
      const knownIds = new Set([
        ...Object.keys(state[entityType]),
        ...savedSet,
      ]);

      const nextEntries: Record<string, boolean> = {};
      for (const id of knownIds) {
        nextEntries[id] = savedSet.has(id);
      }

      return {
        ...state,
        [entityType]: nextEntries,
      };
    });
  }
}
