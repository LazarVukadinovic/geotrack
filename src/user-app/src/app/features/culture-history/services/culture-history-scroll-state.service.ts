import { Injectable, signal } from '@angular/core';

export interface CultureHistoryScrollState {
  scrollTop: number;
  pillsLeft: number;
  clickedItemId: string | null;
}

/**
 * Persists Culture & History listing page scroll state across route navigation.
 * State is saved synchronously at click time before router.navigate() fires —
 * the same pattern used by FoodMusicPageComponent and HotelsListingPageComponent.
 */
@Injectable({ providedIn: 'root' })
export class CultureHistoryScrollStateService {
  private readonly _state = signal<CultureHistoryScrollState | null>(null);

  save(scrollTop: number, pillsLeft: number, clickedItemId: string | null = null): void {
    this._state.set({ scrollTop, pillsLeft, clickedItemId });
  }

  /** Returns and clears the saved state. Returns null if nothing was saved. */
  pop(): CultureHistoryScrollState | null {
    const state = this._state();
    this._state.set(null);
    return state;
  }
}
