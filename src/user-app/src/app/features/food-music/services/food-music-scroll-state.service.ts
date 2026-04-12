import { Injectable, signal } from '@angular/core';

export interface FoodMusicScrollState {
  scrollTop: number;
  pillsLeft: number;
  clickedItemId: string | null;
}

/**
 * Persists Food & Music listing page scroll state across route navigation.
 * State is saved synchronously at click time before router.navigate() fires —
 * the same pattern used by HotelsListingPageComponent.
 */
@Injectable({ providedIn: 'root' })
export class FoodMusicScrollStateService {
  private readonly _state = signal<FoodMusicScrollState | null>(null);

  save(scrollTop: number, pillsLeft: number, clickedItemId: string | null = null): void {
    this._state.set({ scrollTop, pillsLeft, clickedItemId });
  }

  /** Returns and clears the saved state. Returns null if nothing was saved. */
  pop(): FoodMusicScrollState | null {
    const state = this._state();
    this._state.set(null);
    return state;
  }
}
