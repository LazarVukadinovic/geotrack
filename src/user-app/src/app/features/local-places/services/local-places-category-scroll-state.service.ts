import { Injectable, signal } from '@angular/core';

export interface LocalPlacesCategoryScrollState {
  scrollTop: number;
  clickedItemId: string | null;
}

/**
 * Persists Local Places category-results page scroll state across route navigation.
 * State is saved synchronously at card-click time before router.navigate() fires.
 */
@Injectable({ providedIn: 'root' })
export class LocalPlacesCategoryScrollStateService {
  private readonly _state = signal<LocalPlacesCategoryScrollState | null>(null);

  save(scrollTop: number, clickedItemId: string | null = null): void {
    this._state.set({ scrollTop, clickedItemId });
  }

  /** Returns and clears the saved state. Returns null if nothing was saved. */
  pop(): LocalPlacesCategoryScrollState | null {
    const state = this._state();
    this._state.set(null);
    return state;
  }
}
