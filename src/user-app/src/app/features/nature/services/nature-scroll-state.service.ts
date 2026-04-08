import { Injectable, signal } from '@angular/core';

export interface NatureScrollState {
  scrollTop: number;
  pillsLeft: number;
  clickedItemId: string | null;
}

/**
 * Persists Nature listing page scroll state across route navigation.
 * State is saved synchronously at click time before router.navigate() fires —
 * the same pattern used by FoodMusicPageComponent and CultureHistoryPageComponent.
 */
@Injectable({ providedIn: 'root' })
export class NatureScrollStateService {
  private readonly _state = signal<NatureScrollState | null>(null);

  save(scrollTop: number, pillsLeft: number, clickedItemId: string | null = null): void {
    this._state.set({ scrollTop, pillsLeft, clickedItemId });
  }

  /** Returns and clears the saved state. Returns null if nothing was saved. */
  pop(): NatureScrollState | null {
    const state = this._state();
    this._state.set(null);
    return state;
  }
}
